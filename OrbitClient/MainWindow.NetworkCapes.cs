using OrbitClient.Capes;
using OrbitClient.Scripts;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OrbitClient;

public partial class MainWindow
{
    private readonly SemaphoreSlim networkCapeWork = new(2, 2);
    private readonly SemaphoreSlim networkCapePublish = new(1, 1);
    private readonly Dictionary<string, long> networkCapeRequests = new();
    private readonly HashSet<string> networkCapePending = new();
    private readonly CancellationTokenSource networkCapeLifetime = new();

    private async Task HandleNetworkCapeRequestAsync(OrbitPipeMessage message)
    {
        if (windowClosed || message.Data.Length > 2048 || networkCapePending.Count >= 34) return;
        CapePresenceRequest? request = null;
        var remote = message.Type == "cape_remote_request";
        var localLocked = false;
        var workLocked = false;
        var pendingKey = "";
        try
        {
            if (message.Type != "cape_presence_clear")
            {
                request = JsonSerializer.Deserialize<CapePresenceRequest>(message.Data, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (request == null || !request.HasValidBinding() || (request.Revision?.Length ?? 0) > 512) return;
            }
            var key = remote ? "remote:" + request!.Actor : message.Type;
            var now = Environment.TickCount64;
            if (networkCapeRequests.TryGetValue(key, out var last) && now - last < 2000) return;
            if (!networkCapePending.Add(key)) return;
            pendingKey = key;
            if (networkCapeRequests.Count > 64) networkCapeRequests.Clear();
            networkCapeRequests[key] = now;
            await networkCapeWork.WaitAsync(networkCapeLifetime.Token); workLocked = true;
            if (!remote) { await networkCapePublish.WaitAsync(networkCapeLifetime.Token); localLocked = true; }
            if (message.Type == "cape_presence_clear")
            {
                if (orbitAccount != null) await orbitAccount.ClearCapePresenceAsync(networkCapeLifetime.Token);
                return;
            }
            var account = orbitAccount ?? throw new IOException("Sign in to share capes.");
            var owner = account.Account?.Id;
            var watch = Stopwatch.StartNew();
            CapePresenceMessage response;
            if (remote) response = await account.ResolveCapePresenceAsync(request!, networkCapeLifetime.Token);
            else
            {
                if (!capeService.IsVerified || capeService.SelectedId.Length == 0) throw new IOException("Equip an owned cape first.");
                var selected = capeService.SelectedId;
                response = await account.PublishCapePresenceAsync(selected, request!, networkCapeLifetime.Token);
                if (selected != capeService.SelectedId || !capeService.IsVerified) throw new IOException("Cape selection changed.");
                response.Cape = new CapeDefinition { Id = selected };
            }
            response.ValidForSeconds -= (int)Math.Ceiling(watch.Elapsed.TotalSeconds);
            if (account != orbitAccount || owner != account.Account?.Id) throw new IOException("Your account changed.");
            if (response.ValidForSeconds <= 0) throw new IOException("Cape verification expired during download.");
            if (!windowClosed && pipeServer.IsConnected)
                await pipeServer.SendAsync(remote ? "cape_remote_image" : "cape_presence", JsonSerializer.Serialize(response));
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Orbit Network Capes] " + ex.Message);
            if (!windowClosed && request != null)
                await pipeServer.SendAsync(remote ? "cape_remote_remove" : "cape_presence_failed", JsonSerializer.Serialize(request));
        }
        finally
        {
            if (localLocked) networkCapePublish.Release();
            if (workLocked) networkCapeWork.Release();
            if (pendingKey.Length > 0) networkCapePending.Remove(pendingKey);
        }
    }
}
