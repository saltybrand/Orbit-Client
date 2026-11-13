using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Collections.Concurrent;

namespace OrbitClient.Scripts;

public sealed record OrbitContent<T>(T Value, bool IsCached);

public sealed class OrbitContentService : IDisposable
{
    public
    const string Bucket = "orbit-assets";
    private static readonly HashSet<string> Documents = new(StringComparer.Ordinal) {
    "discover",
    "news",
    "mods",
    "updates",
    "status",
    "capes"
  };
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly HttpClient api, assets;
    private readonly string cacheRoot;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> pending = new(StringComparer.Ordinal);
    private bool disposed;
    public string ProjectUrl
    {
        get;
    }

    public OrbitContentService(OrbitAccountConfig config, string dataRoot, HttpMessageHandler? apiHandler = null, HttpMessageHandler? assetHandler = null)
    {
        config.Validate();
        ProjectUrl = config.ProjectUrl.Trim().TrimEnd('/');
        api = new(apiHandler ?? OrbitNetworkPolicy.CreateHandler());
        assets = new(assetHandler ?? OrbitNetworkPolicy.CreateHandler());
        api.BaseAddress = new Uri(ProjectUrl + "/");
        api.DefaultRequestHeaders.Add("apikey", config.PublishableKey);
        api.Timeout = Timeout.InfiniteTimeSpan;
        assets.Timeout = Timeout.InfiniteTimeSpan;
        cacheRoot = Path.Combine(dataRoot, "online-cache", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ProjectUrl))));
    }

    public async Task<OrbitContent<T>> ReadAsync<T>(string document, bool allowCached = true, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Documents.Contains(document)) throw new IOException("Unknown Orbit content document.");
        allowCached &= document is not ("status" or "updates");
        var cache = Path.Combine(cacheRoot, document + ".json");
        try
        {
            var flight = pending.GetOrAdd(document, key => new Lazy<Task<string>>(() => FetchAsync(key)));
            string payload;
            var request = flight.Value;
            try { payload = await request.WaitAsync(cancellationToken).ConfigureAwait(false); }
            finally
            {
                if (request.IsCompleted) pending.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(document, flight));
                else _ = request.ContinueWith(completed =>
                {
                    _ = completed.Exception;
                    pending.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(document, flight));
                }, TaskScheduler.Default);
            }
            var result = JsonSerializer.Deserialize<T>(payload, Json) ??
              throw new IOException("Invalid Orbit content document.");
            try
            {
                Directory.CreateDirectory(cacheRoot);
                WorkspaceService.AtomicWrite(cache, Encoding.UTF8.GetBytes(payload));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return new(result, false);
        }
        catch (Exception ex) when (allowCached && !cancellationToken.IsCancellationRequested && !lifetime.IsCancellationRequested
            && (ex is IOException or HttpRequestException or TaskCanceledException or JsonException)
            && (ex is not OrbitApiException apiError || OrbitHttp.IsTransient(apiError.Status)))
        {
            if (!File.Exists(cache)) throw;
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cache);
            if (age > TimeSpan.FromDays(7) || age < TimeSpan.Zero) throw new IOException("Saved content has expired. Connect to refresh it.");
            if (new FileInfo(cache).Length > 4 * 1024 * 1024) throw new IOException("The saved content is too large.");
            using var saved = File.OpenRead(cache);
            using var savedResponse = new HttpResponseMessage { Content = new StreamContent(saved) };
            var payload = Encoding.UTF8.GetString(await OrbitHttp.ReadBoundedAsync(savedResponse, 4 * 1024 * 1024, cancellationToken).ConfigureAwait(false));
            using var cachedDocument = JsonDocument.Parse(payload);
            ValidatePayload(document, cachedDocument);
            return new(JsonSerializer.Deserialize<T>(payload, Json) ??
              throw new IOException("The saved content is invalid."), true);
        }
    }

    private async Task<string> FetchAsync(string document)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await OrbitHttp.GetAsync(api,
            () => new HttpRequestMessage(HttpMethod.Get, "rest/v1/orbit_content?select=payload&key=eq." + document), deadline.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new OrbitApiException(response.StatusCode == HttpStatusCode.TooManyRequests ? "Too many requests. Try again shortly." : "Content is unavailable. Try again later.", response.StatusCode);
        using var rows = JsonDocument.Parse(await OrbitHttp.ReadBoundedAsync(response, 4 * 1024 * 1024, deadline.Token).ConfigureAwait(false));
        if (rows.RootElement.ValueKind != JsonValueKind.Array || rows.RootElement.GetArrayLength() != 1
            || rows.RootElement[0].ValueKind != JsonValueKind.Object || !rows.RootElement[0].TryGetProperty("payload", out var value))
            throw new IOException("Content is missing or invalid: " + document);
        var payload = value.GetRawText();
        using var parsed = JsonDocument.Parse(payload);
        ValidatePayload(document, parsed);
        return payload;
    }

    private static void ValidatePayload(string document, JsonDocument parsed)
    {
        var value = parsed.RootElement;
        var valid = document == "status" ? value.ValueKind == JsonValueKind.Object && value.TryGetProperty("online", out var online)
            && online.ValueKind is JsonValueKind.True or JsonValueKind.False : value.ValueKind == JsonValueKind.Array;
        if (!valid) throw new IOException("Content has an invalid format: " + document);
    }

    public string StorageUrl(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Split('/').Any(segment => segment.Length == 0 || segment is "."
            or "..") || path.Contains('\\'))
            throw new IOException("Invalid Supabase asset path.");
        return ProjectUrl + "/storage/v1/object/public/" + Bucket + "/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    }

    public string ResolveAssetUrl(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return source;
        if (source.StartsWith("orbit://assets/", StringComparison.Ordinal)) return StorageUrl(source[15..]);
        if (source.StartsWith("orbit://approved-mods/", StringComparison.Ordinal))
        {
            var parts = source[22..].Split('/');
            if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "D", out _) || !parts[1].EndsWith(".dll",StringComparison.Ordinal) || !Guid.TryParseExact(parts[1][..^4],"D",out _))
                throw new IOException("Invalid approved mod download path.");
            return ProjectUrl + "/storage/v1/object/public/orbit-approved-mods/" + string.Join('/', parts.Select(Uri.EscapeDataString));
        }
        if (Uri.TryCreate(source, UriKind.Absolute, out
            var uri) && uri.Host.Equals("orbitclientapi.netlify.app", StringComparison.OrdinalIgnoreCase))
            return StorageUrl(Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')));
        return source;
    }

    public async Task<byte[]> DownloadAsync(string source, int maximumBytes, TimeSpan timeout, CancellationToken cancellationToken =
      default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (maximumBytes <= 0 || timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        using
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        deadline.CancelAfter(timeout);
        var uri = ValidateDownloadUri(ResolveAssetUrl(source));
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            using
            var response = await OrbitHttp.GetAsync(assets, () => new HttpRequestMessage(HttpMethod.Get, uri), deadline.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect or HttpStatusCode.SeeOther)
            {
                if (response.Headers.Location is null) throw new IOException("The asset redirect has no destination.");
                uri = ValidateDownloadUri(ResolveAssetUrl(new Uri(uri, response.Headers.Location).AbsoluteUri));
                continue;
            }
            if (!response.IsSuccessStatusCode) throw new OrbitApiException("The file is unavailable. Try again later.", response.StatusCode);
            if (response.Content.Headers.ContentType?.MediaType == "text/html") throw new IOException("The server returned a web page instead of a file.");
            return await OrbitHttp.ReadBoundedAsync(response, maximumBytes, deadline.Token).ConfigureAwait(false);
        }
        throw new IOException("Too many asset redirects.");
    }

    public async Task<List<OrbitClient.DataModels.DiscoverMod>> ReadPublishedModsAsync(CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var mods = new List<OrbitClient.DataModels.DiscoverMod>();
        for (var page = 0; page < 100; page++)
        {
            using var response = await OrbitHttp.GetAsync(api, () => new HttpRequestMessage(HttpMethod.Get,
                "rest/v1/rpc/orbit_published_mods?requested_page=" + page), deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new IOException("The community catalog could not be loaded.");
            var bytes = await OrbitHttp.ReadBoundedAsync(response, 1024 * 1024, deadline.Token).ConfigureAwait(false);
            var rows = JsonSerializer.Deserialize<List<OrbitClient.DataModels.DiscoverMod>>(bytes, Json) ?? throw new IOException("Invalid community catalog.");
            foreach (var mod in rows)
            {
                if (mod is null || !mod.id.StartsWith("submission-", StringComparison.Ordinal)
                    || !Guid.TryParseExact(mod.id[11..], "D", out var id)
                    || !mod.download.StartsWith("orbit://approved-mods/",StringComparison.Ordinal)
                    || mod.download[22..].Split('/') is not [var binary,var filename]
                    || !Guid.TryParseExact(binary,"D",out _) || filename!=id.ToString("D")+".dll"
                    || !System.Text.RegularExpressions.Regex.IsMatch(mod.sha256, "^[a-f0-9]{64}$"))
                    throw new IOException("Invalid community mod listing.");
                mods.Add(mod);
            }
            if (rows.Count < 100) return mods;
        }
        throw new IOException("The community catalog exceeds the supported size. Refine the server catalog before retrying.");
    }

    public static bool HasApprovedModHash(OrbitClient.DataModels.DiscoverMod mod) =>
        mod.sha256 is not null && System.Text.RegularExpressions.Regex.IsMatch(mod.sha256, "\\A[a-f0-9]{64}\\z");

    public static void RequireModApproval(OrbitClient.DataModels.DiscoverMod mod)
    {
        if (!HasApprovedModHash(mod))
            throw new IOException("Installation is unavailable until the catalog maintainer publishes this DLL's approved SHA-256. Refresh Discover after it is approved.");
    }

    public static void VerifyModDownload(OrbitClient.DataModels.DiscoverMod mod, byte[] bytes)
    {
        RequireModApproval(mod);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != mod.sha256)
            throw new IOException("The downloaded DLL does not match the approved file. Installation was stopped.");
    }

    private static Uri ValidateDownloadUri(string source)
    {
        return OrbitNetworkPolicy.ValidateHttpsUri(source);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        api.Dispose();
        assets.Dispose();
    }
}
