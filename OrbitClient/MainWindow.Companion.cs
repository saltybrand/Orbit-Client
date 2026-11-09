using OrbitClient.Scripts;
using System.Windows;

namespace OrbitClient;

public partial class MainWindow
{
    private async Task InstallCompanionAsync(bool installLoader = false)
    {
        await WithFileOperation(async root =>
        {
            if (installLoader)
                await BepInExInstaller.EnsureInstalledAsync(root,
                    () => MessageBox.Show(this, "BepInEx is needed for the Orbit companion. Install it now?",
                        "BepInEx required", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes,
                    () => InstallBepInEx(root));
            SetFeatureStatus("Checking the latest Orbit companion…");
            var installed = await new OrbitCompanionInstaller().InstallAsync(root,
                () => orbitContent.DownloadAsync(OrbitCompanionInstaller.DownloadSource, 64 * 1024 * 1024,
                    TimeSpan.FromSeconds(preferences.DownloadTimeout)),
                preferences.AutoSnapshot ? () => workspace.CreateSnapshot(root) : null);
            SetFeatureStatus(installed ? "Orbit companion installed. Press F4 in game to open it." : "Orbit companion is up to date. Press F4 in game to open it.");
        });
    }

    private async Task AutoInstallCompanionAsync()
    {
        if (WorkspaceService.IsGameRunning())
        {
            SetFeatureStatus("Companion update skipped while Gorilla Tag is running. Restart Orbit with the game closed to update.");
            return;
        }
        try { await InstallCompanionAsync(); }
        catch (Exception ex)
        {
            SetFeatureStatus("Companion was not updated: " + ex.Message);
            Console.WriteLine("[Orbit Companion] " + ex.Message);
        }
    }
}
