using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace OrbitClient.Scripts;

public sealed class OrbitAccountConfig
{
    public string ProjectUrl
    {
        get;
        set;
    } = "https://nghwithekvbjbfcyteoc.supabase.co";
    public string PublishableKey
    {
        get;
        set;
    } = "sb_publishable_bgThJnZ9M45DekZToDrCEA_8hHg--Ek";
    public string AccountPageUrl
    {
        get;
        set;
    } = "";
    public static OrbitAccountConfig Load(string path) => File.Exists(path) ?
      JsonSerializer.Deserialize<OrbitAccountConfig>(File.ReadAllText(path)) ?? new() :
      new();
    public void Save(string path)
    {
        Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
        File.Move(path + ".tmp", path, true);
    }
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProjectUrl) || !Uri.TryCreate(ProjectUrl.Trim(), UriKind.Absolute, out
            var url) || url.Scheme != "https" ||
          !string.IsNullOrEmpty(url.UserInfo) || url.AbsolutePath != "/" || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
            throw new IOException("Enter the HTTPS Supabase project URL, with no extra path.");
        if (string.IsNullOrWhiteSpace(PublishableKey) || !PublishableKey.StartsWith("sb_publishable_", StringComparison.Ordinal) || PublishableKey.Any(char.IsWhiteSpace))
            throw new IOException("Use a publishable key beginning sb_publishable_. Never use a secret key.");
        AccountPageUrl ??= "";
        if (AccountPageUrl.Length > 0 && (!Uri.TryCreate(AccountPageUrl, UriKind.Absolute, out
              var page) ||
            page.Scheme != "https" || page.IsLoopback || page.UserInfo.Length > 0 || page.Query.Length > 0 || page.Fragment.Length > 0))
            throw new IOException("Enter a public HTTPS account page URL without localhost, a query or fragment.");
    }
}

public sealed record OrbitAccount(string Id, string Email, string DisplayName, string AvatarUrl = "");
public sealed record OrbitProfile(string Id, string Username, string Bio);
public sealed class OrbitAuthException(string message, HttpStatusCode status) : IOException(message)
{
    public HttpStatusCode Status
    {
        get;
    } = status;
}

public sealed partial class OrbitAccountService : IDisposable
{
    private readonly HttpClient client;
    private readonly IOrbitCredentialStore credentials;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private string access = "", refresh = "";
    private DateTimeOffset expires;
    private bool remember;
    public OrbitAccountConfig Config
    {
        get;
    }
    public OrbitAccount? Account
    {
        get;
        private set;
    }
    public string? StorageWarning
    {
        get;
        private set;
    }

    public OrbitAccountService(OrbitAccountConfig config, IOrbitCredentialStore? store = null, HttpMessageHandler? handler = null, HttpMessageHandler? submissionHandler = null)
    {
        config.Validate();
        Config = config;
        Config.ProjectUrl = config.ProjectUrl.Trim().TrimEnd('/');
        credentials = store ?? new OrbitCredentialStore();
        client = handler is null ? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        }) : new HttpClient(handler);
        client.BaseAddress = new Uri(Config.ProjectUrl + "/");
        client.Timeout = TimeSpan.FromSeconds(25);
        client.DefaultRequestHeaders.Add("apikey", Config.PublishableKey);
        submissionFiles = new HttpClient(submissionHandler ?? new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = client.BaseAddress, Timeout = TimeSpan.FromMinutes(3) };
        submissionFiles.DefaultRequestHeaders.Add("apikey", Config.PublishableKey);
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body = null, string? bearer = null,
      CancellationToken cancellationToken =
      default)
    {
        using
        var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(25));
        HttpRequestMessage CreateRequest()
        {
            var message = new HttpRequestMessage(method, path);
            if (body is not null) message.Content = JsonContent.Create(body);
            if (bearer is not null) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (path.StartsWith("rest/", StringComparison.Ordinal)) message.Headers.Add("Prefer", "return=representation");
            return message;
        }
        using
        var request = CreateRequest();
        using
        var response = method == HttpMethod.Get ? await OrbitHttp.GetAsync(client, CreateRequest, linked.Token).ConfigureAwait(false)
            : await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if (bearer is not null && response.StatusCode == HttpStatusCode.Unauthorized && !path.StartsWith("auth/v1/logout", StringComparison.Ordinal))
            {
                Clear();
                throw new OrbitAuthException("Your session is no longer valid. Sign in again.", response.StatusCode);
            }

            var code = "";
            try
            {
                using
                var error = JsonDocument.Parse(await OrbitHttp.ReadBoundedAsync(response, 64 * 1024, linked.Token).ConfigureAwait(false));
                code = Text(error.RootElement, "error_code");
            }
            catch (JsonException) { }
            var message = code
            switch
            {
                "email_not_confirmed" => "Confirm your email before signing in.",
                "otp_expired" => "That confirmation is invalid or has expired. Click Resend confirmation and use the newest email.",
                "captcha_failed" => "A security check is required. Try signing in through your browser.",
                "weak_password" => "Choose a stronger password, at least 12 characters.",
                "provider_disabled" => "Discord sign-in is unavailable. Use email instead.",
                _ when (int)response.StatusCode == 429 => "Too many attempts. Wait a little before trying again.",
                _ when path.Contains("grant_type=password") => "Sign-in failed. Check your email and password.",
                _ when response.StatusCode == HttpStatusCode.Conflict => "That username is already taken. Choose another.",
                _ when OrbitHttp.IsTransient(response.StatusCode) => "The account service is temporarily unavailable. Try again later.",
                _ when path.StartsWith("rest/") => "Your profile could not be saved or loaded. Try again.",
                _ => "The account request failed. Try again later."
            };
            throw new OrbitAuthException(message, response.StatusCode);
        }
        var text = Encoding.UTF8.GetString(await OrbitHttp.ReadBoundedAsync(response, 1024 * 1024, linked.Token).ConfigureAwait(false));
        if (string.IsNullOrWhiteSpace(text)) return default;
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException) { throw new IOException("The account service returned an invalid response. Try again later."); }
    }

    private static string Text(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out
      var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static OrbitAccount User(JsonElement user)
    {
        var id = Text(user, "id");
        if (!Guid.TryParse(id, out _)) throw new IOException("Supabase returned an invalid account.");
        var name = "";
        var avatar = "";
        if (user.TryGetProperty("user_metadata", out
            var metadata))
        {
            name = Text(metadata, "full_name") is
            {
                Length: > 0
            }
            full ? full : Text(metadata, "name");
            avatar = Text(metadata, "avatar_url") is
            {
                Length: > 0
            }
            image ? image : Text(metadata, "picture");
            if (!Uri.TryCreate(avatar, UriKind.Absolute, out
                var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0) avatar = "";
        }
        return new(id, Text(user, "email"), name, avatar);
    }
    private void Install(JsonElement result, bool persist)
    {
        var token = Text(result, "access_token");
        var refreshToken = Text(result, "refresh_token");
        if (token.Length == 0 || refreshToken.Length == 0 || !result.TryGetProperty("user", out
            var user))
            throw new IOException("No sign-in session was returned. Confirm your email and try again.");
        var account = User(user);
        access = token;
        refresh = refreshToken;
        Account = account;
        remember = persist;
        var seconds = result.TryGetProperty("expires_in", out
          var duration) && duration.TryGetInt32(out
          var value) ? value : 3600;
        expires = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(seconds, 0, 86400));
        StorageWarning = null;
        try
        {
            if (remember) credentials.Write(Config.ProjectUrl, refresh);
            else credentials.Delete(Config.ProjectUrl);
        }
        catch (IOException)
        {
            remember = false;
            try
            {
                credentials.Delete(Config.ProjectUrl);
                StorageWarning = "Signed in for this run. Windows could not save Remember me securely.";
            }
            catch (IOException)
            {
                StorageWarning = "Signed in for this run. Windows could not remove an older saved sign-in; check Windows Credential Manager before closing Orbit.";
            }
        }
    }

    public async Task SignInAsync(string email, string password, bool persist)
    {
        ValidateEmail(email);
        if (string.IsNullOrEmpty(password)) throw new IOException("Enter your password.");
        await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            Install(await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=password", new
            {
                email = email.Trim(),
                password
            }).ConfigureAwait(false), persist);
        }
        finally
        {
            gate.Release();
        }
    }
    public async Task SignUpAsync(string email, string password)
    {
        ValidateEmail(email);
        if (password.Length < 12) throw new IOException("Use a password with at least 12 characters.");

        var path = "auth/v1/signup" + RedirectQuery();
        await SendAsync(HttpMethod.Post, path, new
        {
            email = email.Trim(),
            password
        }).ConfigureAwait(false);
    }
    public async Task ResendConfirmationAsync(string email)
    {
        ValidateEmail(email);
        await SendAsync(HttpMethod.Post, "auth/v1/resend" + RedirectQuery(), new
        {
            type = "signup",
            email = email.Trim()
        }).ConfigureAwait(false);
    }
    public async Task VerifyEmailAsync(string email, string code, bool persist)
    {
        ValidateEmail(email);
        code = code.Trim();
        object verification;
        if (System.Text.RegularExpressions.Regex.IsMatch(code, "^[0-9]{6,10}$"))
            verification = new
            {
                email = email.Trim(),
                token = code,
                type = "email"
            };
        else
        {

            var project = new Uri(Config.ProjectUrl);
            if (code.Length > 4096 || !Uri.TryCreate(code, UriKind.Absolute, out
                var link) ||
              link.Scheme != "https" || link.Host != project.Host || link.Port != project.Port ||
              link.UserInfo.Length != 0 || link.Fragment.Length != 0 || link.AbsolutePath != "/auth/v1/verify")
                throw new IOException("Paste the Confirm email address link from your newest email, or enter its verification code.");
            var fields = new Dictionary<string,
              string>(StringComparer.Ordinal);
            foreach (var pair in link.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length != 2 || !fields.TryAdd(Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts[1])))
                    throw new IOException("That confirmation link is invalid. Copy it again from the email.");
            }
            if (!fields.TryGetValue("token", out
                var hash) || !fields.TryGetValue("type", out
                var type) ||
              type is not ("signup"
                or "email") ||
              !System.Text.RegularExpressions.Regex.IsMatch(hash, "^[A-Za-z0-9_-]{20,256}$"))
                throw new IOException("Use the signup confirmation link from your email.");
            verification = new
            {
                token_hash = hash,
                type
            };
        }
        await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            Install(await SendAsync(HttpMethod.Post, "auth/v1/verify", verification).ConfigureAwait(false), persist);
        }
        finally
        {
            gate.Release();
        }
    }
    public async Task ResetPasswordAsync(string email)
    {
        ValidateEmail(email);
        if (Config.AccountPageUrl.Length == 0) throw new IOException("Password resets are not configured yet. Contact Orbit support.");
        await SendAsync(HttpMethod.Post, "auth/v1/recover" + RedirectQuery(), new
        {
            email = email.Trim()
        }).ConfigureAwait(false);
    }
    private string RedirectQuery() => Config.AccountPageUrl.Length == 0 ? "" : "?redirect_to=" + Uri.EscapeDataString(Config.AccountPageUrl);
    private static void ValidateEmail(string email)
    {
        if (!MailAddress.TryCreate(email.Trim(), out
            var address) || address.Address != email.Trim())
            throw new IOException("Enter a valid email address.");
    }

    public async Task<bool> RestoreAsync()
    {
        await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            var stored = credentials.Read(Config.ProjectUrl);
            if (stored is null) return false;
            try
            {
                Install(await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=refresh_token", new
                {
                    refresh_token = stored
                }).ConfigureAwait(false), true);
                return true;
            }
            catch (OrbitAuthException ex) when (ex.Status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                Clear();
                return false;
            }
        }
        finally
        {
            gate.Release();
        }
    }
    private async Task<string> TokenAsync()
    {
        await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            if (Account is null) throw new IOException("Sign in first.");
            if (expires <= DateTimeOffset.UtcNow.AddSeconds(60))
            {
                try
                {
                    Install(await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=refresh_token", new
                    {
                        refresh_token = refresh
                    }).ConfigureAwait(false), remember);
                }
                catch (OrbitAuthException ex) when (ex.Status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    Clear();
                    throw new IOException("Your session expired. Sign in again.");
                }
            }
            return access;
        }
        finally
        {
            gate.Release();
        }
    }
    public async Task<OrbitProfile?> GetProfileAsync()
    {
        var token = await TokenAsync().ConfigureAwait(false);
        var owner = Account?.Id ?? throw new IOException("Sign in first.");
        var result = await SendAsync(HttpMethod.Get, "rest/v1/profiles?select=id,username,bio&id=eq." + owner, bearer: token).ConfigureAwait(false);
        if (Account?.Id != owner || result.ValueKind != JsonValueKind.Array || result.GetArrayLength() > 1
            || (result.GetArrayLength() == 1 && Text(result[0], "id") != owner))
            throw new IOException("Your profile could not be verified. Sign in again.");
        return result.GetArrayLength() == 0 ? null : Profile(result[0]);
    }
    private static OrbitProfile Profile(JsonElement value) => new(Text(value, "id"), Text(value, "username"), Text(value, "bio"));
    public async Task<OrbitProfile> SaveProfileAsync(string username, string bio, bool create)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(username, "^[a-z0-9_]{3,24}$"))
            throw new IOException("Username: 3–24 lowercase letters, numbers, or underscores.");
        if (bio.Length > 500) throw new IOException("Keep your bio under 501 characters.");
        var token = await TokenAsync().ConfigureAwait(false);
        var owner = Account?.Id ?? throw new IOException("Sign in first.");
        object body = create ? new
        {
            id = owner,
            username,
            bio
        } : new
        {
            username,
            bio
        };
        var result = await SendAsync(create ? HttpMethod.Post : HttpMethod.Patch,
          "rest/v1/profiles" + (create ? "" : "?id=eq." + owner), body, token).ConfigureAwait(false);
        if (Account?.Id != owner || result.ValueKind != JsonValueKind.Array || result.GetArrayLength() != 1 || Text(result[0], "id") != owner)
            throw new IOException("The saved profile could not be verified. Try again.");
        return Profile(result[0]);
    }
    public async Task SaveAvatarAsync(byte[] jpeg)
    {
        if (jpeg.Length < 3 || jpeg.Length > 1024 * 1024 || jpeg[0] != 0xff || jpeg[1] != 0xd8 || jpeg[2] != 0xff)
            throw new IOException("Choose a JPEG profile picture under 1 MB.");
        var token = await TokenAsync().ConfigureAwait(false);
        using
        var request = new HttpRequestMessage(HttpMethod.Post, "storage/v1/object/orbit-avatars/" + Account!.Id + "/avatar.jpg");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("x-upsert", "true");
        request.Content = new ByteArrayContent(jpeg);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        using
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, lifetime.Token).ConfigureAwait(false);
        RejectRevokedAvatarSession(response);
        if (!response.IsSuccessStatusCode) throw new IOException("Your picture could not be saved. Check your connection and Orbit's profile-picture storage setup.");
    }
    public async Task<byte[]?> GetAvatarAsync()
    {
        var token = await TokenAsync().ConfigureAwait(false);
        using
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        using
        var request = new HttpRequestMessage(HttpMethod.Get, "storage/v1/object/authenticated/orbit-avatars/" + Account!.Id + "/avatar.jpg");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        RejectRevokedAvatarSession(response);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest) return null;
        if (!response.IsSuccessStatusCode) throw new IOException("Your profile picture could not be loaded.");
        if (response.Content.Headers.ContentLength > 1024 * 1024) throw new IOException("The profile picture is too large.");
        using
        var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using
        var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + read > 1024 * 1024) throw new IOException("The profile picture is too large.");
            await bytes.WriteAsync(buffer.AsMemory(0, read), deadline.Token).ConfigureAwait(false);
        }
        return bytes.ToArray();
    }
    public async Task RemoveAvatarAsync()
    {
        var token = await TokenAsync().ConfigureAwait(false);
        using
        var request = new HttpRequestMessage(HttpMethod.Delete, "storage/v1/object/orbit-avatars/" + Account!.Id + "/avatar.jpg");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, lifetime.Token).ConfigureAwait(false);
        RejectRevokedAvatarSession(response);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            throw new IOException("Your picture could not be removed. Please try again.");
    }
    private void RejectRevokedAvatarSession(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Unauthorized) return;
        Clear();
        throw new IOException("Your session is no longer valid. Sign in again.");
    }
    public async Task SignInWithDiscordAsync(bool persist, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await SendAsync(HttpMethod.Get, "auth/v1/settings", cancellationToken: cancellationToken).ConfigureAwait(false);
            if (settings.TryGetProperty("external", out
                var providers) &&
              providers.TryGetProperty("discord", out
                var enabled) && enabled.ValueKind == JsonValueKind.False)
                throw new IOException("Discord sign-in is currently unavailable. Use email to sign in.");
            using
            var callback = new OrbitOAuthCallback();
            using
            var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var url = Config.ProjectUrl + "/auth/v1/authorize?provider=discord&scopes=identify%20email&redirect_to=" +
              Uri.EscapeDataString(callback.RedirectUri.AbsoluteUri) + "&code_challenge=" + callback.Challenge + "&code_challenge_method=s256";
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
            var code = await callback.WaitForCodeAsync(timeout.Token).ConfigureAwait(false);
            Install(await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=pkce", new
            {
                auth_code = code,
                code_verifier = callback.Verifier
            }, cancellationToken: timeout.Token).ConfigureAwait(false), persist);
        }
        finally
        {
            gate.Release();
        }
    }
    public async Task SignOutAsync(bool allDevices = false)
    {
        await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            try
            {
                if (access.Length > 0) await SendAsync(HttpMethod.Post, "auth/v1/logout?scope=" + (allDevices ? "global" : "local"), bearer: access).ConfigureAwait(false);
            }
            finally
            {
                Clear();
            }
        }
        finally
        {
            gate.Release();
        }
    }
    private void Clear()
    {
        access = refresh = "";
        Account = null;
        remember = false;
        credentials.Delete(Config.ProjectUrl);
    }
    public void Dispose()
    {
        lifetime.Cancel();
        client.Dispose();
        submissionFiles.Dispose();
    }
}
