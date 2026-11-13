using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OrbitClient.DataModels;
using OrbitClient.Scripts;

namespace OrbitClient;

public partial class MainWindow
{
    private bool discoverInstalling;
    private List<LocalMod> discoverInventory = [];
    private TextBlock? discoverDetailsStatus;
    private Button? discoverDetailsInstall;
    private ProgressBar? discoverDetailsProgress;
    private string discoverInstallMessage = "";

    private void RefreshDiscoverInventory()
    {
        try { discoverInventory = workspace.ReadMods(InstallPath.Text); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { discoverInventory = []; }
    }

    private string DiscoverInstallationStatus(DiscoverMod mod)
    {
        string name;
        try { name = DiscoverInstallation.FileName(mod); }
        catch (IOException) { return "Download unavailable"; }
        var matches = discoverInventory.Where(x => string.Equals(Path.GetFileName(x.Path).Replace(".disabled", "", StringComparison.OrdinalIgnoreCase), name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count > 1) return "Duplicate copies · resolve in Mod Library";
        if (matches.Count == 0) return OrbitContentService.HasApprovedModHash(mod) ? "Not installed" : "Not installed · awaiting DLL approval";
        var local = matches[0];
        return $"Installed{(local.Enabled ? "" : " · disabled")} · v{local.Version}" + (local.Version == mod.version ? "" : " · catalog v" + mod.version);
    }

    private void ReportDiscoverInstall(string message)
    {
        discoverInstallMessage = message;
        DiscoverStatusText.Text = message;
        if (discoverDetailsStatus is not null) discoverDetailsStatus.Text = message;
    }

    private void OpenDiscoverDetails(DiscoverMod mod)
    {
        Nav_Click(NavPanel.Children.OfType<Button>().Single(b => Equals(b.Tag, "Discover")), new RoutedEventArgs());
        Discover.Visibility = Visibility.Collapsed;
        FeaturePage.Visibility = Visibility.Visible;
        FeatureContent.Children.Clear();
        featurePage = "Mod details";
        PageTitle.Text = mod.name;
        PageSubtitle.Text = "Discover · Mod details";
        FeatureContent.Children.Add(FeatureButton("Back to Discover", () => { Nav_Click(NavPanel.Children.OfType<Button>().Single(b => Equals(b.Tag, "Discover")), new RoutedEventArgs()); return Task.CompletedTask; }));
        var panel = new StackPanel();
        panel.Children.Add(FeatureText(mod.name, 28));
        panel.Children.Add(FeatureText($"By {mod.author} · v{mod.version} · {mod.category}", 14, true));
        panel.Children.Add(FeatureText(mod.description, 15));
        panel.Children.Add(FeatureText("Release notes", 20));
        panel.Children.Add(FeatureText(string.IsNullOrWhiteSpace(mod.release_notes) ? "No release notes provided for this version." : mod.release_notes, 14, true));
        RefreshDiscoverInventory();
        discoverDetailsStatus = FeatureText(DiscoverInstallationStatus(mod), 13, true);
        panel.Children.Add(discoverDetailsStatus);
        featureStatus = discoverDetailsStatus;
        var progress = new ProgressBar { Height = 4, IsIndeterminate = discoverInstalling, Visibility = discoverInstalling ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 12) };
        progress.SetResourceReference(Control.ForegroundProperty, "Accent");
        discoverDetailsProgress = progress;
        panel.Children.Add(progress);
        discoverDetailsInstall = FeatureButton(OrbitContentService.HasApprovedModHash(mod) ? "Install " + mod.name : "Approval required", async () =>
        {
            progress.Visibility = Visibility.Visible; progress.IsIndeterminate = true;
            try { await InstallDiscoverMod(mod); }
            finally { progress.Visibility = Visibility.Collapsed; progress.IsIndeterminate = false; }
        }, true);
        discoverDetailsInstall.Tag = mod;
        discoverDetailsInstall.IsEnabled = !discoverInstalling && !string.IsNullOrWhiteSpace(mod.download) && OrbitContentService.HasApprovedModHash(mod);
        if (!OrbitContentService.HasApprovedModHash(mod))
            panel.Children.Add(FeatureText("The catalog maintainer must publish this DLL's approved SHA-256 before it can be installed. Refresh Discover after approval.", 13, true));
        panel.Children.Add(discoverDetailsInstall);
        panel.Children.Add(FeatureText("Disabled installations stay disabled when updated. You can enable them in Mod Library.", 12, true));
        FeatureContent.Children.Add(FeatureCard(panel));
        AnimateActivePage();
    }
}
