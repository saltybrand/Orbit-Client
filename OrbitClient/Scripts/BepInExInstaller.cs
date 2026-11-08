using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace OrbitClient.Scripts;

public sealed class BepInExInstaller
{
    private static readonly string[] RequiredFiles =
        ["BepInEx/core/BepInEx.dll", "BepInEx/core/BepInEx.Preloader.dll", "winhttp.dll", "doorstop_config.ini"];
    private const int MaximumBytes = 32 * 1024 * 1024;

    public static bool IsInstalled(string root) => RequiredFiles.All(relative =>
{
   var path = Path.Combine(root, relative);
   WorkspaceService.EnsureNoLinks(path);
   return File.Exists(path) && new FileInfo(path).Length > 0;
});

    public static async Task EnsureInstalledAsync(string root, Func<bool> confirm, Func<Task> install)
    {
        if (IsInstalled(root)) return;
        if (!confirm()) throw new OperationCanceledException("BepInEx is required. Mod download cancelled.");
        await install();
        if (!IsInstalled(root)) throw new IOException("BepInEx installation is incomplete. Mod download blocked.");
    }

    public async Task<string> InstallAsync(string root, int timeoutSeconds)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitClient/1.0");
        using var response = await client.GetAsync("https://api.github.com/repos/BepInEx/BepInEx/releases?per_page=100");
        response.EnsureSuccessStatusCode();
        using var releases = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var release in releases.RootElement.EnumerateArray())
        {
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith("v5.", StringComparison.Ordinal) || release.GetProperty("prerelease").GetBoolean()
                || release.GetProperty("draft").GetBoolean()) continue;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.StartsWith("BepInEx_win_x64_", StringComparison.Ordinal) || !name.EndsWith(".zip", StringComparison.Ordinal)) continue;
                var url = new Uri(asset.GetProperty("browser_download_url").GetString()!);
                if (url.Scheme != "https" || url.Host != "github.com" || !url.AbsolutePath.StartsWith("/BepInEx/BepInEx/releases/download/", StringComparison.Ordinal))
                    throw new IOException("GitHub returned an unexpected BepInEx download URL.");
                using var download = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                download.EnsureSuccessStatusCode();
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                await using var stream = await download.Content.ReadAsStreamAsync(cancellation.Token);
                using var archive = new MemoryStream();
                var buffer = new byte[81920];
                int count;
                while ((count = await stream.ReadAsync(buffer, cancellation.Token)) > 0)
                {
                    if (archive.Length + count > MaximumBytes) throw new IOException("BepInEx download exceeds the size limit.");
                    archive.Write(buffer, 0, count);
                }
                await Task.Run(() => InstallArchive(root, archive.ToArray()));
                return tag;
            }
        }
        throw new IOException("No stable BepInEx 5 Windows x64 release was found on GitHub.");
    }

    public static void InstallArchive(string gamePath, byte[] bytes)
    {
        var root = WorkspaceService.VerifyGame(gamePath);
        WorkspaceService.RequireGameClosed();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var relative = entry.FullName.Replace('\\', '/');
            if (relative.StartsWith('/') || relative.Contains(':') || relative.Split('/').Any(x => x is ".." or "."))
                throw new IOException("Unsafe path in the BepInEx archive.");
            if (relative.EndsWith('/')) continue;

            if (!relative.StartsWith("BepInEx/core/", StringComparison.OrdinalIgnoreCase)
                && relative is not ("winhttp.dll" or "doorstop_config.ini" or ".doorstop_version")) continue;
            total += entry.Length;
            if (total > MaximumBytes) throw new IOException("BepInEx archive exceeds the size limit.");
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Unsafe BepInEx destination.");
            WorkspaceService.EnsureNoLinks(path);
            using var input = entry.Open();
            using var output = new MemoryStream();
            input.CopyTo(output);
            if (!files.TryAdd(path, output.ToArray())) throw new IOException("Duplicate file in the BepInEx archive.");
        }
        if (RequiredFiles.Any(x => !files.TryGetValue(Path.GetFullPath(Path.Combine(root, x)), out var content) || content.Length == 0))
            throw new IOException("The BepInEx archive is missing required loader files.");
        var previous = files.Keys.ToDictionary(x => x, x => File.Exists(x) ? File.ReadAllBytes(x) : null);
        var written = new List<string>();
        try
        {
            foreach (var (path, content) in files)
            {
                WorkspaceService.RequireGameClosed();
                WorkspaceService.EnsureNoLinks(path);
                written.Add(path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                WorkspaceService.AtomicWrite(path, content);
            }
            if (!IsInstalled(root)) throw new IOException("BepInEx installation could not be verified.");
        }
        catch
        {
            foreach (var path in written.AsEnumerable().Reverse())
            {
                if (previous[path] is { } original) WorkspaceService.AtomicWrite(path, original);
                else if (File.Exists(path)) File.Delete(path);
            }
            throw;
        }
    }
}
