using OrbitClient.Scripts;
using System.Windows.Controls;

namespace OrbitClient;

public partial class MainWindow
{
    private void BuildBepInEx()
    {
        var card = new StackPanel();
        var installed = false;
        try
        {
            var root = WorkspaceService.VerifyGame(InstallPath.Text);
            installed = BepInExInstaller.IsInstalled(root);
            card.Children.Add(FeatureText(installed ? "BepInEx is installed" : "BepInEx is not installed or needs repair", 20));
        }
        catch (Exception ex) { card.Children.Add(FeatureText(ex.Message, 16)); }
        card.Children.Add(FeatureText("Required for mods. Close the game before installing.", 14, true));
        var actions = new WrapPanel();
        actions.Children.Add(FeatureButton(installed ? "Repair BepInEx" : "Install BepInEx", async () =>
        {
            string version = "";
            await WithFileOperation(async root => { version = await InstallBepInEx(root); });
            if (featurePage == "BepInEx") OpenFeaturePage("BepInEx");
            SetFeatureStatus("Ready for mods.");
        }, true));
        actions.Children.Add(FeatureButton("Choose game folder", () =>
        {
            Browse_Click(this, new());
            OpenFeaturePage("BepInEx");
            return Task.CompletedTask;
        }));
        actions.Children.Add(FeatureButton("Refresh", () => { OpenFeaturePage("BepInEx"); return Task.CompletedTask; }));
        card.Children.Add(actions);
        FeatureContent.Children.Add(FeatureCard(card));
    }

    private async Task<string> InstallBepInEx(string root)
    {
        SetFeatureStatus("Downloading and installing BepInEx from GitHub…");
        return await new BepInExInstaller().InstallAsync(root, preferences.DownloadTimeout);
    }
}
