using Microsoft.Win32;
using OrbitClient.DataModels;
using OrbitClient.Scripts;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace OrbitClient;

public partial class MainWindow
{
    private readonly WorkspaceService workspace = new();
    private ClientPreferences preferences = new();
    private readonly System.Threading.SemaphoreSlim fileOperation = new(1, 1);
    private TextBlock? featureStatus;
    private string featurePage = "";
    private string preferencesWarning = "";
    private bool catalogChecked;

    private void InitializeFeatures()
    {
        try { preferences = workspace.LoadPreferences(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { preferencesWarning = "Saved preferences could not be read: " + ex.Message; }
        InstallPath.Text = preferences.GamePath;
        CloseAppAfterLaunchButton.IsChecked = preferences.CloseAfterLaunch;
        UpdateButton.IsChecked = preferences.CheckUpdates;
        InstallPath.LostFocus += (_, _) => SaveFeaturePreferences();
        CloseAppAfterLaunchButton.Click += (_, _) => SaveFeaturePreferences();
        UpdateButton.Click += (_, _) => SaveFeaturePreferences();
        InitializeCustomization();
        Closing += (_, e) =>
        {
            if (fileOperation.CurrentCount > 0) return;
            e.Cancel = true;
            SetFeatureStatus("Finishing changes. Try closing again in a moment.");
        };
        Closed += (_, _) => { SaveWindowPreferences(); pipeServer.Stop(); };
    }

    private void SaveFeaturePreferences()
    {
        preferences.GamePath = InstallPath.Text.Trim();
        preferences.CloseAfterLaunch = CloseAppAfterLaunchButton.IsChecked == true;
        preferences.CheckUpdates = UpdateButton.IsChecked == true;
        try { workspace.SavePreferences(preferences); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, ex.Message, "Unable to save preferences"); }
    }

    private static TextBlock FeatureText(string text, double size = 14, bool muted = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = new SolidColorBrush(muted ? Color.FromRgb(179, 166, 189) : Color.FromRgb(244, 239, 247)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10)
    };

    private Button FeatureButton(string label, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = label, Style = (Style)FindResource(primary ? "Primary" : "Ghost"), Margin = new Thickness(0, 0, 8, 8) };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        var icon = label switch
        {
            "Refresh" or "Refresh files" => "refresh",
            "Run system check" => "health_and_safety",
            "Check for updates" => "refresh",
            "Create snapshot" or "Restore this snapshot" => "backup",
            "Export theme" => "upload",
            "Import theme" => "download",
            "Reset appearance" => "restart_alt",
            "Choose image…" => "image",
            "Open mods folder" or "Config folder" or "Open Orbit data" or "Open settings" => "folder_open",
            "Apply hex" => "check",
            _ => ""
        };
        if (icon.Length > 0) MaterialSymbols.Label(button, label, icon);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception ex) { SetFeatureStatus(ex.Message); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private Border FeatureCard(Panel content) => new() { Style = (Style)FindResource("Card"), Child = content, Margin = new Thickness(0, 0, 0, 14) };
    private void SetFeatureStatus(string message)
    {
        if (FeaturePage.Visibility == Visibility.Visible && featureStatus != null) featureStatus.Text = message;
        else if (SettingsPage.Visibility == Visibility.Visible && settingsStatus != null) settingsStatus.Text = message;
        else if (Discover.Visibility == Visibility.Visible) DiscoverStatusText.Text = message;
        else PageSubtitle.Text = message;
    }

    private void OpenFeaturePage(string page)
    {
        featurePage = page;
        FeaturePage.Visibility = Visibility.Visible;
        FeatureContent.Children.Clear();
        var subtitle = page switch
        {
            "System Check" => "Find setup problems and get a clear next step.",
            "Snapshots" => "Keep a recovery point for your mods and settings.",
            "Mod Library" => "Manage your installed mods.",
            "BepInEx" => "Set up your mod loader.",
            _ => "Make Orbit feel like yours."
        };
        PageTitle.Text = page;
        PageSubtitle.Text = subtitle;
        featureStatus = FeatureText(preferencesWarning, 12, true);
        FeatureContent.Children.Add(featureStatus);
        switch (page)
        {
            case "System Check": BuildSystemCheck(); break;
            case "Snapshots": BuildSnapshots(); break;
            case "Mod Library": BuildModLibrary(); break;
            case "Appearance": BuildAppearance(); break;
            case "BepInEx": BuildBepInEx(); break;
        }
    }

    private void BuildSystemCheck()
    {
        var results = new StackPanel();
        var actions = new WrapPanel();
        actions.Children.Add(FeatureButton("Run system check", async () =>
        {
            SetFeatureStatus("Checking installation, plugin dependencies, and startup log…");
            var root = InstallPath.Text;
            var checks = await Task.Run(() => workspace.Diagnose(root));
            results.Children.Clear();
            foreach (var check in checks)
            {
                var card = new StackPanel();
                card.Children.Add(FeatureText((check.Passed ? "✓  " : "!  ") + check.Title, 17));
                card.Children.Add(FeatureText(check.Detail, 13, true));
                results.Children.Add(FeatureCard(card));
            }
            SetFeatureStatus($"Check complete · {checks.Count(x => !x.Passed)} items to review.");
        }, true));
        actions.Children.Add(FeatureButton("Choose game folder", () => { Browse_Click(this, new()); return Task.CompletedTask; }));
        actions.Children.Add(FeatureButton("Open startup log", () =>
        {
            OpenLocal(System.IO.Path.Combine(WorkspaceService.VerifyGame(InstallPath.Text), "BepInEx", "LogOutput.log"), true);
            return Task.CompletedTask;
        }));
        FeatureContent.Children.Add(actions);
        FeatureContent.Children.Add(results);
    }

    private void BuildSnapshots()
    {
        var actions = new WrapPanel();
        actions.Children.Add(FeatureButton("Create snapshot", async () =>
        {
            await WithFileOperation(async root =>
            {
                SaveFeaturePreferences();
                await Task.Run(() => workspace.CreateSnapshot(root));
            });
            if (featurePage == "Snapshots") { OpenFeaturePage("Snapshots"); SetFeatureStatus("Snapshot created."); }
        }, true));
        actions.Children.Add(FeatureButton("Refresh", () => { OpenFeaturePage("Snapshots"); return Task.CompletedTask; }));
        FeatureContent.Children.Add(actions);
        var automatic = new CheckBox
        {
            Content = "Create a snapshot before mod installs and updates",
            IsChecked = preferences.AutoSnapshot,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 8, 0, 18)
        };
        automatic.Click += (_, _) => { preferences.AutoSnapshot = automatic.IsChecked == true; SaveFeaturePreferences(); };
        FeatureContent.Children.Add(automatic);
        FeatureContent.Children.Add(FeatureText("Save or restore your setup.", 13, true));
        try
        {
            var snapshots = workspace.ListSnapshots(InstallPath.Text);
            if (snapshots.Count == 0) FeatureContent.Children.Add(FeatureText("No snapshots yet. Save your current setup before the next update.", 16));
            foreach (var snapshot in snapshots)
            {
                var content = new StackPanel();
                content.Children.Add(FeatureText(snapshot.Created.ToString("dddd, MMM d · h:mm tt"), 17));
                content.Children.Add(FeatureText($"{snapshot.Bytes / 1048576d:F1} MB · {System.IO.Path.GetFileName(snapshot.Path)}", 12, true));
                content.Children.Add(FeatureButton("Restore this snapshot", async () =>
                {
                    if (MessageBox.Show(this, "Replace your current mods and settings with this snapshot? Orbit saves your current setup first.",
                        "Restore snapshot", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    await WithFileOperation(root => Task.Run(() => workspace.RestoreSnapshot(root, snapshot.Path)));
                    preferences = workspace.LoadPreferences();
                    InstallPath.Text = preferences.GamePath;
                    CloseAppAfterLaunchButton.IsChecked = preferences.CloseAfterLaunch;
                    UpdateButton.IsChecked = preferences.CheckUpdates;
                    ApplyAppearance();
                    BuildExtraSettings();
                    if (featurePage == "Snapshots") { OpenFeaturePage("Snapshots"); SetFeatureStatus("Snapshot restored. Previous folders are retained beside the restored folders for recovery."); }
                }));
                FeatureContent.Children.Add(FeatureCard(content));
            }
        }
        catch (Exception ex) { SetFeatureStatus(ex.Message); }
    }

    private async Task WithFileOperation(Func<string, Task> action)
    {
        if (!await fileOperation.WaitAsync(0)) throw new IOException("Another mod or snapshot operation is still running.");
        LaunchButton.IsEnabled = false;
        try
        {
            WorkspaceService.RequireGameClosed();
            var root = WorkspaceService.VerifyGame(InstallPath.Text);
            SetFeatureStatus("Working…");
            await action(root);
        }
        finally { fileOperation.Release(); LaunchButton.IsEnabled = true; }
    }

    private void BuildModLibrary()
    {
        var actions = new WrapPanel();
        actions.Children.Add(FeatureButton("Refresh files", () => { OpenFeaturePage("Mod Library"); return Task.CompletedTask; }));
        actions.Children.Add(FeatureButton("Check for updates", async () =>
        {
            await LoadDiscoverMods();
            catalogChecked = string.IsNullOrEmpty(discoverLoadNotice);
            if (featurePage == "Mod Library") OpenFeaturePage("Mod Library");
            SetFeatureStatus(catalogChecked ? "Updates checked." : "Updates unavailable. Try again later.");
        }, true));
        actions.Children.Add(FeatureButton("Open mods folder", () =>
        {
            OpenLocal(System.IO.Path.Combine(WorkspaceService.VerifyGame(InstallPath.Text), "BepInEx", "plugins"));
            return Task.CompletedTask;
        }));
        FeatureContent.Children.Add(actions);
        FeatureContent.Children.Add(FeatureText("Changes apply on the next game launch. Close Gorilla Tag before toggling or updating a mod.", 13, true));
        try
        {
            var mods = workspace.ReadMods(InstallPath.Text);
            if (mods.Count == 0) FeatureContent.Children.Add(FeatureText("No mod files found. Install a mod from Discover to get started.", 16));
            foreach (var mod in mods)
            {
                var content = new StackPanel();
                content.Children.Add(FeatureText(mod.Name, 18));
                content.Children.Add(FeatureText($"{(mod.Enabled ? "Enabled" : "Disabled")} · v{mod.Version}\n{System.IO.Path.GetRelativePath(InstallPath.Text, mod.Path)}", 12, true));
                if (mod.Error != null) content.Children.Add(FeatureText("Unreadable metadata: " + mod.Error, 12, true));
                var row = new WrapPanel();
                row.Children.Add(FeatureButton(mod.Enabled ? "Disable" : "Enable", async () =>
                {
                    await WithFileOperation(root => Task.Run(() => workspace.ToggleMod(root, mod)));
                    if (featurePage == "Mod Library") { OpenFeaturePage("Mod Library"); SetFeatureStatus("Mod state saved for the next launch."); }
                }));
                var config = string.IsNullOrWhiteSpace(mod.Guid) || mod.Guid.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ? ""
                    : System.IO.Path.Combine(InstallPath.Text, "BepInEx", "config", mod.Guid + ".cfg");
                if (File.Exists(config)) row.Children.Add(FeatureButton("Open settings", () => { OpenLocal(config, true); return Task.CompletedTask; }));
                else row.Children.Add(FeatureButton("Config folder", () => { OpenLocal(System.IO.Path.Combine(InstallPath.Text, "BepInEx", "config")); return Task.CompletedTask; }));
                var fileName = System.IO.Path.GetFileName(mod.Path).Replace(".disabled", "", StringComparison.OrdinalIgnoreCase);
                var match = discoverMods.FirstOrDefault(x => Uri.TryCreate(x.download, UriKind.Absolute, out var uri)
                    && string.Equals(System.IO.Path.GetFileName(uri.LocalPath), fileName, StringComparison.OrdinalIgnoreCase));
                var known = catalogChecked && match != null && Version.TryParse(match.version.TrimStart('v'), out _) && Version.TryParse(mod.Version.TrimStart('v'), out _);
                var newer = known && Version.Parse(match!.version.TrimStart('v')) > Version.Parse(mod.Version.TrimStart('v'));
                content.Children.Add(FeatureText(!known ? "Update status unknown · check the catalog or the mod author's release page."
                    : newer ? $"Update available · v{match!.version}" : "Up to date with the catalog.", 12, true));
                if (newer) row.Children.Add(FeatureButton("Update mod", async () =>
                {
                    await InstallModFile(match!, mod.Path);
                    if (featurePage == "Mod Library") { OpenFeaturePage("Mod Library"); SetFeatureStatus("Mod updated. Its enabled state was preserved."); }
                }, true));
                content.Children.Add(row);
                FeatureContent.Children.Add(FeatureCard(content));
            }
        }
        catch (Exception ex) { SetFeatureStatus(ex.Message); }
    }

    private async Task InstallModFile(DiscoverMod mod, string? existingPath = null, string? bundledSource = null)
    {
        await WithFileOperation(async root =>
        {
            await BepInExInstaller.EnsureInstalledAsync(root,
                () => MessageBox.Show(this,
                    "BepInEx is needed to install mods. Install it now? Choosing No cancels the download.",
                    "BepInEx required", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes,
                () => InstallBepInEx(root));
            if (!Uri.TryCreate(orbitContent.ResolveAssetUrl(mod.download), UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new IOException("A secure download URL is required.");
            var name = System.IO.Path.GetFileName(uri.LocalPath);
            if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                throw new IOException("Only DLL downloads are supported.");
            var plugins = System.IO.Path.Combine(root, "BepInEx", "plugins");
            var destination = existingPath ?? System.IO.Path.Combine(plugins, name);
            if (!System.IO.Path.GetFullPath(destination).StartsWith(plugins + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Invalid mod destination.");
            if (existingPath == null && File.Exists(destination + ".disabled")) destination += ".disabled";
            WorkspaceService.EnsureNoLinks(destination);
            var bytes = bundledSource != null ? await File.ReadAllBytesAsync(bundledSource) : await orbitContent.DownloadAsync(uri.AbsoluteUri, 64 * 1024 * 1024, TimeSpan.FromSeconds(preferences.DownloadTimeout));
            using (var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(bytes)))
                if (!pe.HasMetadata) throw new IOException("The download is not a managed DLL.");
            WorkspaceService.RequireGameClosed();
            if (preferences.AutoSnapshot) { SaveFeaturePreferences(); await Task.Run(() => workspace.CreateSnapshot(root)); }
            WorkspaceService.RequireGameClosed();
            Directory.CreateDirectory(plugins);
            await Task.Run(() => WorkspaceService.AtomicWrite(destination, bytes));
        });
    }

    private static void OpenLocal(string path, bool textFile = false)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) throw new IOException("Not found: " + path + ". Launch the game once to generate mod settings and logs.");
        if (textFile)
        {
            var start = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
            start.ArgumentList.Add(path);
            Process.Start(start);
        }
        else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

}
