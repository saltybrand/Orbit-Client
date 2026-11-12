using OrbitClient.Capes;
using OrbitClient.Scripts;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitClient;

public partial class MainWindow
{
    private OrbitCapeService capeService = null!;
    private readonly DispatcherTimer capeRefresh = new() { Interval = TimeSpan.FromSeconds(20) };
    private bool capeSyncBusy, capeCatalogLoaded;
    private string capeStatus = "";
    private string capeObservedAccount = "";

    private void InitializeDesktopCapes(bool startRefresh = true)
    {
        capeService = new(new OrbitAccountCapeAuthority(() => orbitAccount), workspace.DataRoot, (type, data) => pipeServer.SendAsync(type, data));
        capeRefresh.Tick += async (_, _) => { if (pipeServer.IsConnected) await RefreshDesktopCapesAsync(); };
        if (startRefresh) capeRefresh.Start();
        Closed += (_, _) => { capeRefresh.Stop(); capeService.Dispose(); networkCapeLifetime.Cancel(); };
    }

    private void ObserveCapeAccount()
    {
        if (capeService == null) return;
        var account = orbitAccount?.Account?.Id ?? "";
        if (account == capeObservedAccount) return;
        capeObservedAccount = account;
        _ = pipeServer.SendAsync("cape_network_reset", "");
        capeArtwork.Clear(); capeGalleryGeneration++; capeFocusedId = "";
        capeCatalogLoaded = false; capeStatus = "";
        _ = capeService.AccountChangedAsync();
        if (featurePage == "Capes" && capeTab == 2) OpenFeaturePage("Capes");
        _ = RefreshDesktopCapesAsync();
    }

    private async Task RefreshDesktopCapesAsync()
    {
        if (capeSyncBusy || windowClosed) return;
        capeSyncBusy = true;
        try
        {
            await capeService.SyncAsync();
            capeCatalogLoaded = true;
            capeStatus = "Your dressing room is up to date.";
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or OperationCanceledException or UnauthorizedAccessException)
        {
            capeStatus = "Cape checks are unavailable. Sign in, check your connection, or ask an admin to finish cape setup.";
            if (!windowClosed) await pipeServer.SendAsync("cape_error", capeStatus);
        }
        finally { capeSyncBusy = false; }
        RefreshCapeView();
    }

    private async Task HandleCapeRequestAsync(OrbitPipeMessage message)
    {
        try
        {
            if (message.Type == "capes_request") { CapePipeRequests.Empty(message.Data); await RefreshDesktopCapesAsync(); return; }
            if (message.Type == "cape_clear")
            {
                CapePipeRequests.Empty(message.Data);
                await capeService.ClearAsync(false);
                capeStatus = "Desktop cape selection cleared from the game.";
            }
            else
            {
                var request = CapePipeRequests.Selection(message.Data);
                if (message.Type == "cape_preview") await capeService.PreviewAsync(request);
                else if (message.Type == "cape_select")
                {
                    await capeService.SelectAsync(request.Id, request.RequestId);
                    capeStatus = "Cape selection saved from the game.";
                }
            }
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or OperationCanceledException or UnauthorizedAccessException)
        {
            capeStatus = "Cape unavailable: " + ex.Message;
            if (!windowClosed) await pipeServer.SendAsync("cape_error", capeStatus);
        }
        RefreshCapeView();
    }

    private void BuildCapes() => BuildCapeGallery();
    private void RefreshCapeView()
    {
        if (windowClosed || featurePage != "Capes") return;
        // Preserve the masked key control during background catalog checks.
        if (capeTab != 2) OpenFeaturePage("Capes");
        SetFeatureStatus(capeStatus);
    }
}

