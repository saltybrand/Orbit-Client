using OrbitClient.Capes;
using OrbitClient.Core;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OrbitClient.Scripts;

public sealed partial class OrbitAccountService
{
    public async Task<CapePresenceMessage> PublishCapePresenceAsync(string id, CapePresenceRequest request, CancellationToken cancellationToken)
    {
        ValidateCapeId(id);
        if (!request.HasValidBinding()) throw new IOException("Invalid room binding.");
        var owner = Account?.Id ?? throw new IOException("Sign in to share capes.");
        var token = await TokenAsync().ConfigureAwait(false);
        var result = await SendAsync(HttpMethod.Post, "rest/v1/rpc/orbit_publish_cape_presence",
            new { requested_id = id, requested_context = request.Context, requested_actor = request.Actor }, token, cancellationToken).ConfigureAwait(false);
        if (Account?.Id != owner) throw new IOException("Your account changed.");
        return ReadPresence(result, request, false);
    }

    public async Task ClearCapePresenceAsync(CancellationToken cancellationToken)
    {
        if (Account == null) return;
        var token = await TokenAsync().ConfigureAwait(false);
        await SendAsync(HttpMethod.Post, "rest/v1/rpc/orbit_clear_cape_presence", new { }, token, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CapePresenceMessage> ResolveCapePresenceAsync(CapePresenceRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasValidBinding() || !Guid.TryParseExact(request.Token, "D", out _)) throw new IOException("Invalid cape presence.");
        var owner = Account?.Id ?? throw new IOException("Sign in to see shared capes.");
        var token = await TokenAsync().ConfigureAwait(false);
        var result = await SendAsync(HttpMethod.Post, "rest/v1/rpc/orbit_resolve_cape_presence",
            new { requested_token = request.Token, requested_context = request.Context, requested_actor = request.Actor }, token, cancellationToken).ConfigureAwait(false);
        if (Account?.Id != owner) throw new IOException("Your account changed.");
        var presence = ReadPresence(result, request, true);
        var cape = presence.Cape!;
        // Always recheck the lease; only unchanged, already displayed images skip download.
        if (request.Revision != CapeDisplayRevision(cape))
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            using var download = new HttpRequestMessage(HttpMethod.Get, "storage/v1/object/authenticated/orbit-capes/" + string.Join('/', cape.Image.Split('/').Select(Uri.EscapeDataString)));
            download.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(download, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            RejectRevokedAvatarSession(response);
            if (!response.IsSuccessStatusCode) throw new IOException("Shared cape image unavailable.");
            var bytes = await OrbitHttp.ReadBoundedAsync(response, 4 * 1024 * 1024, deadline.Token).ConfigureAwait(false);
            if (Account?.Id != owner || !CapeImageRules.TryGetDimensions(bytes, out _, out _)) throw new IOException("Invalid shared cape image.");
            presence.ImageBase64 = Convert.ToBase64String(bytes);
        }
        return presence;
    }

    public static string CapeDisplayRevision(CapeDefinition cape) => cape.Id + "|" + cape.Revision + "|" + cape.Image;

    private static CapePresenceMessage ReadPresence(JsonElement result, CapePresenceRequest request, bool remote)
    {
        var presence = result.Deserialize<CapePresenceMessage>(CapeJson) ?? throw new IOException("Cape presence unavailable.");
        if (presence.Context != request.Context || presence.Actor != request.Actor || !Guid.TryParseExact(presence.Token, "D", out _)
            || (remote && presence.Token != request.Token) || presence.ValidForSeconds is < 1 or > 40)
            throw new IOException("Cape presence unavailable. Apply the network cape migration if needed.");
        if (remote && (presence.Cape == null || !presence.Cape.Enabled || !presence.Cape.HasValidId()
            || !presence.Cape.HasValidFit() || !presence.Cape.HasPrivateImagePath())) throw new IOException("Invalid shared cape metadata.");
        return presence;
    }
}
