using OrbitClient.Capes;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OrbitClient.Scripts;

public interface IOrbitCapeAuthority
{
    string OwnerKey { get; }
    Task<List<CapeDefinition>> ReadCatalogAsync(CancellationToken token);
    Task ClaimFreeAsync(string id, CancellationToken token);
    Task<string> RedeemAsync(string key, CancellationToken token);
    Task<byte[]> DownloadOwnedAsync(string id, CancellationToken token);
}

public sealed class OrbitAccountCapeAuthority(Func<OrbitAccountService?> account) : IOrbitCapeAuthority
{
    public string OwnerKey
    {
        get { var service = account(); return service?.Account is { } user ? service.Config.ProjectUrl + "|" + user.Id : ""; }
    }
    private OrbitAccountService Service => account() ?? throw new IOException("Connect your Orbit account service first.");
    public Task<List<CapeDefinition>> ReadCatalogAsync(CancellationToken token) => Service.ReadCapeCatalogAsync(token);
    public Task ClaimFreeAsync(string id, CancellationToken token) => Service.ClaimFreeCapeAsync(id, token);
    public Task<string> RedeemAsync(string key, CancellationToken token) => Service.RedeemCapeKeyAsync(key, token);
    public Task<byte[]> DownloadOwnedAsync(string id, CancellationToken token) => Service.DownloadOwnedCapeAsync(id, token);
}

public sealed partial class OrbitAccountService
{
    private static readonly JsonSerializerOptions CapeJson = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<CapeDefinition>> ReadCapeCatalogAsync(CancellationToken cancellationToken = default)
    {
        var owner = Account?.Id;
        var token = owner == null ? null : await TokenAsync().ConfigureAwait(false);
        var result = await SendAsync(HttpMethod.Post, "rest/v1/rpc/orbit_cape_catalog", new { }, token, cancellationToken).ConfigureAwait(false);
        if (Account?.Id != owner) throw new IOException("Your account changed. Refresh your capes.");
        return result.Deserialize<List<CapeDefinition>>(CapeJson) ?? throw new IOException("Invalid cape catalog.");
    }

    public async Task ClaimFreeCapeAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateCapeId(id);
        var owner = Account?.Id ?? throw new IOException("Sign in to claim a cape.");
        var token = await TokenAsync().ConfigureAwait(false);
        var result = await SendAsync(HttpMethod.Post, "rest/v1/rpc/orbit_claim_free_cape", new { requested_id = id }, token, cancellationToken).ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.True || Account?.Id != owner) throw new IOException("Your cape claim could not be verified.");
    }

    public async Task<string> RedeemCapeKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 256) throw new IOException("Paste a valid cape key.");
        var owner = Account?.Id ?? throw new IOException("Sign in to redeem a cape key.");
        var token = await TokenAsync().ConfigureAwait(false);
        var result = await SendAsync(HttpMethod.Post, "rest/v1/rpc/orbit_redeem_cape_key", new { redemption_key = key }, token, cancellationToken).ConfigureAwait(false);
        if (Account?.Id != owner) throw new IOException("Your account changed. Refresh your dressing room.");
        if (!result.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
            throw new IOException(Text(result, "error") switch
            {
                "already_owned" => "You already own this cape. The key has not been used.",
                "account_revoked" => "An administrator revoked this cape from your account. Ask them to restore it with Grant / restore. This key has not been used.",
                "rate_limited" => "Too many attempts. Wait one minute before trying again.",
                _ => "That key is invalid, expired, revoked, or already redeemed."
            });
        var id = Text(result, "capeId");
        ValidateCapeId(id);
        return id;
    }

    public async Task<byte[]> DownloadOwnedCapeAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateCapeId(id);
        var owner = Account?.Id ?? throw new IOException("Sign in to use your capes.");
        var token = await TokenAsync().ConfigureAwait(false);
        var grant = await SendAsync(HttpMethod.Post, "rest/v1/rpc/orbit_cape_access", new { requested_id = id }, token, cancellationToken).ConfigureAwait(false);
        var path = grant.TryGetProperty("imagePath", out var value) ? value.GetString() ?? "" : "";
        if (!grant.TryGetProperty("id", out var grantedId) || grantedId.GetString() != id
            || !new CapeDefinition { Image = path }.HasPrivateImagePath()) throw new IOException("Invalid cape asset authorization.");
        if (Account?.Id != owner) throw new IOException("Your account changed. Refresh your capes.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, "storage/v1/object/authenticated/orbit-capes/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        RejectRevokedAvatarSession(response);
        if (!response.IsSuccessStatusCode) throw new IOException("You no longer have access to this cape image.");
        var bytes = await OrbitHttp.ReadBoundedAsync(response, 4 * 1024 * 1024, deadline.Token).ConfigureAwait(false);
        if (Account?.Id != owner) throw new IOException("Your account changed while loading this cape.");
        return bytes;
    }

    private static void ValidateCapeId(string id)
    {
        if (!new CapeDefinition { Id = id }.HasValidId()) throw new IOException("Invalid cape ID.");
    }
}
