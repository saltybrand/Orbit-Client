using Microsoft.Win32;
using OrbitClient.DataModels;
using OrbitClient.Scripts;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
namespace OrbitClient;

public partial class MainWindow : Window
{
    private static readonly Brush ActiveBackground = new SolidColorBrush(Color.FromRgb(40, 23, 55));
    private static readonly Brush ActiveForeground = new SolidColorBrush(Color.FromRgb(248, 242, 252));
    private static readonly Brush InactiveForeground = new SolidColorBrush(Color.FromRgb(150, 139, 157));
    public static string steamapiKey = "";
    public static string steamId = "";
    public
    const float currentVersion = 2f;
    public static bool IsGtagRunning = false;
    private
    const int GtagAppID = 1533390;
    private readonly OrbitPipeServer pipeServer = new OrbitPipeServer();
    private bool startupStarted;
    private bool windowClosed;
    public MainWindow() : this(null) { }

    public MainWindow(WorkspaceService? workspaceOverride)
    {
        if (workspaceOverride is not null) workspace = workspaceOverride;
        InitializeComponent();
        InitializeFeatures();
        InitializeOrbitAccounts();
        InitializeOrbitNetworking();
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => windowClosed = true;
        pipeServer.MessageReceived += PipeServer_MessageReceived;
        pipeServer.ModConnected += PipeServer_ModConnected;
        pipeServer.ModDisconnected += PipeServer_ModDisconnected;
    }
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (startupStarted) return;
        startupStarted = true;
        var restoreProfile = RestoreOrbitAccountAsync();
        try
        {
            SetLoadingProgress("Loading Steam account...", 15);
            LoadAccounts();
            LoadCurrentSteamAccount();
            SetLoadingProgress("Loading Gorilla Tag...", 35);
            LoadGtagVersion();
            SetLoadingProgress("Loading playtime...", 55);
            LoadLocalPlaytime();
            SetLoadingProgress("Loading Orbit services...", 75);
            try
            {
                await Task.WhenAll(CheckStatus(), LoadNewsData());
            }
            catch { }
            UpdateText.Text = $"You are on Version {currentVersion}";
            SetLoadingProgress("Ready", 100);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Orbit] Startup error: " + ex);
        }
        LoadingOverlay.Visibility = Visibility.Collapsed;
        LoadingOverlay.IsHitTestVisible = false;
        await restoreProfile;
        if (windowClosed) return;
        var startup = NavPanel.Children.OfType<Button>().FirstOrDefault(x => x.Tag as string == preferences.StartupPage);
        if (startup != null) Nav_Click(startup, new RoutedEventArgs());
        _ = StartPipeServerSafe();
        if (UpdateButton.IsChecked == true)
        {
            await CheckUpdate();
        }
    }
    private void SetLoadingProgress(string status, double percent)
    {
        LoadingStatus.Text = status;
        LoadingPercent.Text = $"{percent:0}%";
        DoubleAnimation animation = new DoubleAnimation
        {
            To = 240 * (percent / 100.0),
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            }
        };
        LoadingProgressBar.BeginAnimation(FrameworkElement.WidthProperty, animation);
    }
    private async Task StartPipeServerSafe()
    {
        try
        {
            await pipeServer.StartAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Orbit Pipe] " + ex);
        }
    }
    private void PipeServer_ModConnected()
    {
        Dispatcher.BeginInvoke(() => { });
    }
    private void PipeServer_ModDisconnected()
    {
        Dispatcher.BeginInvoke(() => { });
    }
    private void PipeServer_MessageReceived(OrbitPipeMessage message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            Console.WriteLine($"[MOD -> ORBIT] {message.Type}: {message.Data}");
            switch (message.Type)
            {
                case "connect":
                case "connected":
                    break;
                case "hello":
                    Console.WriteLine("Mod says: " + message.Data);
                    break;
                case "room":
                    Console.WriteLine("Room: " + message.Data);
                    break;
                case "players":
                    Console.WriteLine("Players: " + message.Data);
                    break;
            }
        });
    }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }
    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button selected || selected.Tag is not string page) return;
        foreach (Button button in NavPanel.Children.OfType<Button>().Append(ProfileNav))
        {
            button.Background = Brushes.Transparent;
            button.Foreground = InactiveForeground;
        }
        selected.SetResourceReference(BackgroundProperty, "AccentSoft");
        selected.Foreground = ActiveForeground;
        HomePage.Visibility = page == "Home" ? Visibility.Visible : Visibility.Collapsed;
        Accounts.Visibility = page == "Accounts" ? Visibility.Visible : Visibility.Collapsed;
        SteamAccountsPage.Visibility = page == "Steam accounts" ? Visibility.Visible : Visibility.Collapsed;
        Discover.Visibility = page == "Discover" ? Visibility.Visible : Visibility.Collapsed;
        News.Visibility = page == "News" ? Visibility.Visible : Visibility.Collapsed;
        NewsDetailsOverlay.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        FeaturePage.Visibility = Visibility.Collapsed;
        featurePage = "";
        (PageTitle.Text, PageSubtitle.Text) = page
        switch
        {
            "Accounts" => ("Profile", "Sign in and manage your Orbit profile."),
            "Steam accounts" => ("Steam accounts", "Choose the Steam account to launch your game."),
            "Discover" => ("Discover", "Discover Gorilla Tag mods."),
            "Settings" => ("Settings", "Configure Orbit Client and your game."),
            "News" => ("News", "Updates, stories, and guides from Orbit."),
            _ => ("Home", "Main Menu.")
        };
        if (page == "Discover" && discoverMods.Count == 0) _ = LoadDiscoverMods();
        if (page is "Snapshots"
          or "Mod Library"
          or "BepInEx"
          or "Appearance") OpenFeaturePage(page);
    }
    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (!LaunchButton.IsEnabled || fileOperation.CurrentCount == 0) return;
        try
        {
            IsGtagRunning = WorkspaceService.IsGameRunning();
            if (!IsGtagRunning && preferences.CheckBeforeLaunch)
            {
                try
                {
                    LaunchButton.IsEnabled = false;
                    var root = InstallPath.Text;
                    var results = await Task.Run(() => workspace.Diagnose(root));
                    if (results.Any(x => !x.Passed) && MessageBox.Show(this, $"The launch check found {results.Count(x => !x.Passed)} items to review. Launch anyway?", "Launch check", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    {
                        Nav_Click(NavPanel.Children.OfType<Button>().Single(x => x.Tag as string == "Settings"), new RoutedEventArgs());
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Launch check");
                    return;
                }
                finally
                {
                    LaunchButton.IsEnabled = true;
                }
            }
            if (fileOperation.CurrentCount == 0) return;
            if (!IsGtagRunning)
            {
                LaunchButton.IsEnabled = false;
                LaunchTitle.Text = "LAUNCHING GAME";
                LaunchStatus.Text = "STARTING GAME";
                LaunchButton.Style = (Style)FindResource("StopButton");
                LaunchGorillaTag();
                await Task.Delay(900);
                LaunchTitle.Text = "STOP GAME";
                LaunchStatus.Text = "GAME IS RUNNING";
                LaunchButton.IsEnabled = true;
                if (CloseAppAfterLaunchButton.IsChecked == true)
                {
                    Application.Current.Shutdown();
                }
            }
            else
            {
                LaunchButton.IsEnabled = false;
                LaunchTitle.Text = "STOPPING GORILLA TAG";
                LaunchStatus.Text = "CLOSING GAME";
                StopGorillaTag();
                await Task.Delay(900);
                IsGtagRunning = false;
                LaunchTitle.Text = "LAUNCH GORILLA TAG";
                LaunchStatus.Text = "READY TO LAUNCH";
                LaunchButton.Style = (Style)FindResource("Primary");
                LaunchButton.IsEnabled = true;
            }
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ErrorManager.LogError("Launch failed", ex);
            MessageBox.Show(this, "The game could not be started or stopped. Check Steam and try again.", "Orbit");
        }
        finally
        {
            IsGtagRunning = WorkspaceService.IsGameRunning();
            LaunchTitle.Text = IsGtagRunning ? "STOP GAME" : "LAUNCH GORILLA TAG";
            LaunchStatus.Text = IsGtagRunning ? "GAME IS RUNNING" : "READY TO LAUNCH";
            LaunchButton.Style = (Style)FindResource(IsGtagRunning ? "StopButton" : "Primary");
            LaunchButton.IsEnabled = fileOperation.CurrentCount > 0;
        }
    }
    private static void LaunchGorillaTag()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = SteamAccountManager.SteamExe,
            Arguments = "-applaunch 1533390",
            UseShellExecute = true
        });
    }
    private void StopGorillaTag()
    {
        foreach (Process process in Process.GetProcessesByName("Gorilla Tag"))
        {
            using (process)
            {
                if (process.HasExited) continue;
                process.Kill(true);
            }
        }
    }
    private void LoadCurrentSteamAccount()
    {
        try
        {
        SteamAccount? account = SteamAccountManager.GetCurrentAccount();
        if (account == null)
        {
            CurrentSteamAccount.Text = "Steam: Not detected";
            return;
        }
        string name = string.IsNullOrWhiteSpace(account.PersonalName) ? account.AccountName : account.PersonalName;
        CurrentSteamAccount.Text = name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            CurrentSteamAccount.Text = "Steam unavailable";
            ErrorManager.LogError("Steam account unavailable", ex);
        }
    }
    private List<SteamAccount> steamAccounts = [];
    private string activeSteamAccount = "";
    private bool steamSwitching;

    private void LoadAccounts()
    {
        if (steamSwitching) return;
        try
        {
            steamAccounts = SteamAccountManager.GetAccounts();
            activeSteamAccount = SteamAccountManager.GetCurrentAccount()?.AccountName ?? "";
            SteamAccountStatus.Text = steamAccounts.Count == 0 ? "Sign in through Steam, then refresh here." : $"{steamAccounts.Count} saved accounts · Switching restarts Steam.";
            LoadCurrentSteamAccount();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            steamAccounts = [];
            SteamAccountStatus.Text = "Steam accounts are unavailable. Open Steam and try Refresh.";
            ErrorManager.LogError("Steam accounts unavailable", ex);
        }
        RenderSteamAccounts();
    }

    private void SteamAccountSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (AccountList != null) RenderSteamAccounts();
    }

    private void RenderSteamAccounts()
    {
        AccountList.Children.Clear();
        var query = SteamAccountSearch.Text.Trim();
        var matches = steamAccounts.Where(x => x.AccountName.Contains(query, StringComparison.OrdinalIgnoreCase) || x.PersonalName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => string.Equals(x.AccountName, activeSteamAccount, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var account in matches)
        {
            var active = string.Equals(account.AccountName, activeSteamAccount, StringComparison.OrdinalIgnoreCase);
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            info.Children.Add(FeatureText(string.IsNullOrWhiteSpace(account.PersonalName) ? account.AccountName : account.PersonalName, 18));
            info.Children.Add(FeatureText(account.AccountName + (active ? " · Active" : ""), 12, true));
            var button = new Button
            {
                Content = active ? "Active" : "Switch",
                Tag = account,
                Style = (Style)FindResource(active ? "Ghost" : "Primary"),
                IsEnabled = !active && !steamSwitching,
                MinWidth = 94,
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Automation.AutomationProperties.SetName(button, active ? "Active Steam account: " + account.AccountName : "Switch to " + account.AccountName);
            button.Click += LaunchAccount_Click;
            Grid.SetColumn(button, 1);
            grid.Children.Add(info);
            grid.Children.Add(button);
            AccountList.Children.Add(FeatureCard(grid));
        }
        if (matches.Count == 0)
        {
            AccountList.Children.Add(FeatureText(query.Length > 0 ? "No matching accounts." : "No saved accounts yet.", 18));
            if (query.Length == 0) AccountList.Children.Add(FeatureButton("Open Steam", () => { SteamAccountManager.OpenSteam(); return Task.CompletedTask; }, true));
        }
    }

    private void RefreshAccounts_Click(object sender, RoutedEventArgs e) => LoadAccounts();

    private async void LaunchAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SteamAccount account } || steamSwitching) return;
        if (!await fileOperation.WaitAsync(0)) { SteamAccountStatus.Text = "Wait for the current operation to finish."; return; }
        steamSwitching = true;
        AccountList.IsEnabled = false;
        LaunchButton.IsEnabled = false;
        SteamAccountStatus.Text = "Switching account…";
        try
        {
            await SteamAccountManager.SwitchGtagAccount(account);
            activeSteamAccount = account.AccountName;
            SteamAccountStatus.Text = "Selected " + account.AccountName + ". Finish any Steam sign-in, then launch from Home.";
            LoadCurrentSteamAccount();
            LoadLocalPlaytime();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException or InvalidOperationException)
        {
            SteamAccountStatus.Text = ex is IOException ? ex.Message : "Could not switch accounts. Open Steam and try again.";
            ErrorManager.LogError("Steam account switch failed", ex);
        }
        finally
        {
            steamSwitching = false;
            fileOperation.Release();
            AccountList.IsEnabled = true;
            LaunchButton.IsEnabled = true;
            RenderSteamAccounts();
        }
    }
    public void LoadLocalPlaytime()
    {
        double? hours = GetLocalGorillaTagPlaytime();
        if (hours.HasValue)
        {
            PlaytimeText.Text = $"{hours.Value:F1} hours";
        }
        else
        {
            PlaytimeText.Text = "Not Available";
        }
    }
    public static double? GetLocalGorillaTagPlaytime()
    {
        try
        {
            string? steamPath = GetSteamPath();
            if (string.IsNullOrWhiteSpace(steamPath))
            {
                return null;
            }
            string userdataPath = Path.Combine(steamPath, "userdata");
            if (!Directory.Exists(userdataPath))
            {
                return null;
            }
            foreach (string userFolder in Directory.GetDirectories(userdataPath))
            {
                string localConfig = Path.Combine(userFolder, "config", "localconfig.vdf");
                if (!File.Exists(localConfig))
                {
                    continue;
                }
                string text = File.ReadAllText(localConfig);
                Match appMatch = Regex.Match(text, @"""1533390""\s*\{(?<data>[\s\S]*?)\n\s*\}", RegexOptions.IgnoreCase);
                if (!appMatch.Success) continue;
                string appData = appMatch.Groups["data"].Value;
                Match playtimeMatch = Regex.Match(appData, @"""Playtime""\s+""(\d+)""", RegexOptions.IgnoreCase);
                if (!playtimeMatch.Success) continue;
                if (!long.TryParse(playtimeMatch.Groups[1].Value, out long minutes))
                {
                    continue;
                }
                return minutes / 60.0;
            }
        }
        catch (Exception ex)
        {
            ErrorManager.LogError("Playtime error: " + ex.Message);
        }
        return null;
    }
    private static string? GetSteamPath()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath")?.ToString();
        }
        catch
        {
            return null;
        }
    }
    public void LoadGtagVersion()
    {
        GameVersionText.Text = GetGorillaTagBuildId();
    }
    public static string
    GetGorillaTagBuildId()
    {
        try
        {
            string manifestPath = @"C:\Program Files (x86)\Steam\steamapps\appmanifest_1533390.acf";
            if (!File.Exists(manifestPath))
            {
                return "Not Installed";
            }
            string text = File.ReadAllText(manifestPath);
            Match match = Regex.Match(text, @"""buildid""\s+""(\d+)""");
            if (!match.Success) return "Unknown";
            return match.Groups[1].Value;
        }
        catch
        {
            return "Unknown";
        }
    }
    private List<NewsData> newsItems = new();
    private Button? activeNewsCard;
    private bool newsLoading;

    public async Task LoadNewsData()
    {
        if (newsLoading) return;
        newsLoading = true;
        try
        {
            var bundled = new List<NewsData>();
            try
            {
                using
                var bundledStream = typeof(MainWindow).Assembly.GetManifestResourceStream("OrbitClient.news.json");
                if (bundledStream is not null)
                    bundled = await JsonSerializer.DeserializeAsync<List<NewsData>>(bundledStream) ?? new();
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                ErrorManager.LogError("Bundled news unavailable: " + ex.Message);
            }
            if (newsItems.Count == 0) ShowNews(bundled);
            NewsStatusText.Text = "Checking for new articles…";
            var result = await orbitContent.ReadAsync<List<NewsData>>("news");
            var remote = result.Value.Where(x => x != null && !string.IsNullOrWhiteSpace(x.title)).ToList();
            foreach (var article in remote)
            {
                var existing = bundled.FindIndex(x => string.Equals(x.title, article.title, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0)
                {
                    var fallback = bundled[existing];
                    if (string.IsNullOrWhiteSpace(article.details)) article.details = fallback.details;
                    if (string.IsNullOrWhiteSpace(article.image)) article.image = fallback.image;
                    if (string.IsNullOrWhiteSpace(article.date)) article.date = fallback.date;
                    if (article.category == "Updates") article.category = fallback.category;
                }
            }
            ShowNews(remote);
            NewsStatusText.Text = result.IsCached ? $"{newsItems.Count} articles · Saved news" : $"{newsItems.Count} articles · Latest from Orbit";
        }
        catch (Exception ex)
        {
            NewsStatusText.Text = newsItems.Count > 0 ? "Showing saved articles · Live updates unavailable. Try Refresh." : "News could not be loaded. Try Refresh.";
            ErrorManager.LogError("News Error: " + ex.Message);
        }
        finally
        {
            newsLoading = false;
        }
    }

    private void ShowNews(IEnumerable<NewsData> items)
    {
        newsItems = items.Where(x => x != null).OrderBy(x => x.placement > 0 ? x.placement : int.MaxValue).ThenByDescending(x => x.PublishedDate).ToList();
        NewsListPanel.Children.Clear();
        FeaturedNewsPanel.Children.Clear();
        NewsDetailsOverlay.Visibility = Visibility.Collapsed;
        if (newsItems.Count == 0)
        {
            News1Title.Text = "No news yet";
            News1Desc.Text = "";
            return;
        }
        var featured = newsItems[0];
        News1Title.Text = featured.title;
        News1Desc.Text = featured.description;
        FeaturedNewsPanel.Children.Add(CreateNewsCard(featured, true));
        foreach (var item in newsItems.Skip(1)) NewsListPanel.Children.Add(CreateNewsCard(item));
        ResizeNewsCards();
    }

    private static TextBlock NewsText(string text, double size, string color, FontWeight weight =
      default) => new()
      {
          Text = text,
          FontSize = size,
          Foreground = (Brush)new BrushConverter().ConvertFromString(color)!,
          FontWeight = weight ==
          default ? FontWeights.Normal : weight,
          TextWrapping = TextWrapping.Wrap
      };

    private Image CreateNewsImage(NewsData item, double height)
    {
        var image = new Image
        {
            Height = height,
            Stretch = Stretch.UniformToFill,
            Source = new BitmapImage(new Uri("pack://application:,,,/OrbitClient;component/Assets/gorilla-forest.jpg"))
        };
        var source = string.IsNullOrWhiteSpace(item.image) ? item.logo : item.image;
        if (Uri.TryCreate(source, UriKind.Absolute, out
            var uri))
        {
            if (uri.Scheme == "pack")
            {
                try
                {
                    image.Source = new BitmapImage(uri);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }
            else if (uri.Scheme == "https" || uri.Scheme == "orbit") _ = LoadRemoteImage(image, source);
        }
        return image;
    }

    private Button CreateNewsCard(NewsData item, bool featured = false)
    {
        var button = new Button
        {
            Style = (Style)FindResource("NewsArticleButton"),
            Margin = new Thickness(0, 0, featured ? 0 : 16, 18),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        System.Windows.Automation.AutomationProperties.SetName(button, $"Read {item.title}");
        var grid = new Grid();
        var picture = CreateNewsImage(item, featured ? 235 : 150);
        var content = new StackPanel
        {
            Margin = new Thickness(18)
        };
        content.Children.Add(NewsText(featured ? $"FEATURED ARTICLE · {item.category.ToUpperInvariant()}" : item.category.ToUpperInvariant(), 10, "#C69CE4", FontWeights.SemiBold));
        var title = NewsText(item.title, featured ? 26 : 18, "#F4EFF7", FontWeights.SemiBold);
        title.Margin = new Thickness(0, 9, 0, 8);
        content.Children.Add(title);
        var description = NewsText(item.description, 13, "#B8ACBF");
        description.LineHeight = 20;
        content.Children.Add(description);
        var date = NewsText(item.DisplayDate, 11, "#A99AB2");
        date.Margin = new Thickness(0, 18, 0, 0);
        content.Children.Add(date);
        if (featured)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(content, 1);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            grid.RowDefinitions.Add(new RowDefinition());
            Grid.SetRow(content, 1);
            grid.MinHeight = 345;
        }
        grid.Children.Add(picture);
        grid.Children.Add(content);
        button.Content = grid;
        button.Click += (_, _) => { activeNewsCard = button; OpenNewsDetails(item); };
        return button;
    }

    private void NewsListPanel_SizeChanged(object sender, SizeChangedEventArgs e) => ResizeNewsCards();
    private void ResizeNewsCards()
    {
        var available = NewsListPanel.ActualWidth;
        if (available <= 0) return;
        int columns = available >= 790 ? 3 : available >= 510 ? 2 : 1;
        foreach (FrameworkElement card in NewsListPanel.Children) card.Width = Math.Max(100, available / columns - 16);
    }

    private void OpenNewsDetails(NewsData item)
    {
        NewsDetailsTitle.Text = item.title;
        NewsDetailsDescription.Text = item.description;
        NewsDetailsDate.Text = $"{item.category}  ·  {item.DisplayDate}";
        NewsDetailsContent.Text = string.IsNullOrWhiteSpace(item.details) ? item.description + "\n\nNo additional article text has been published yet." : item.details;
        NewsDetailsImageHost.Child = CreateNewsImage(item, 330);
        NewsDetailsOverlay.Visibility = Visibility.Visible;
        NewsDetailsScroll.ScrollToTop();
        NewsBackButton.Focus();
    }
    private void CloseNewsDetails_Click(object sender, RoutedEventArgs e)
    {
        NewsDetailsOverlay.Visibility = Visibility.Collapsed;
        activeNewsCard?.Focus();
    }
    private void NewsDetails_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseNewsDetails_Click(sender, e);
            e.Handled = true;
        }
    }
    private async void RefreshNews_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        button.IsEnabled = false;
        try
        {
            await LoadNewsData();
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
    public async Task CheckUpdate()
    {
        try
        {
            var items = (await orbitContent.ReadAsync<List<UpdateData>>("updates", false)).Value;
            if (items == null || items.Count == 0)
            {
                return;
            }
            foreach (UpdateData data in items)
            {
                if (data.version <= currentVersion)
                {
                    continue;
                }
                MessageBox.Show("A new update was detected. Please download it from the website.", $"Orbit Update - Current: {currentVersion}, New: {data.version}", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            }
        }
        catch (Exception ex)
        {
            ErrorManager.LogError($"Update error: " + ex);
        }
    }
    public async Task CheckStatus()
    {
        try
        {
            var status = (await orbitContent.ReadAsync<JsonElement>("status", false)).Value;
            bool online = status.GetProperty("online").GetBoolean();
            StatusCircle.Fill = online ? Brushes.LimeGreen : Brushes.Red;
            StatusText.Text = online ? "Service Online" : "Service Offline";
        }
        catch
        {
            StatusCircle.Fill = Brushes.Red;
            StatusText.Text = "Service Offline";
        }
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new OpenFolderDialog
        {
            Title = "Select your Gorilla Tag installation",
            InitialDirectory = InstallPath.Text
        };
        if (dialog.ShowDialog() == true)
        {
            InstallPath.Text = dialog.FolderName;
            SaveFeaturePreferences();
        }
    }
    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        button.IsEnabled = false;
        button.Content = "CHECKING…";
        await Task.Delay(1000);
        await CheckUpdate();
        await Task.Delay(900);
        button.Content = "CHECK FOR UPDATES";
        button.IsEnabled = true;
    }
    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        steamapiKey = SteamApiKeyBox.Password;
        steamId = SteamIdBox.Password;
        if (string.IsNullOrWhiteSpace(steamapiKey) || string.IsNullOrWhiteSpace(steamId))
        {
            MessageBox.Show("Please enter both your Steam API key and SteamID.");
        }
    }
    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
    private List<DiscoverMod> discoverMods = new();
    private readonly HashSet<string> discoverCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> discoverAuthors = new(StringComparer.OrdinalIgnoreCase);
    private int discoverPage;
    private int discoverPageSize = 8;
    private int discoverSort;
    private bool discoverLoading;
    private bool updatingDiscoverFilters;
    private string discoverLoadNotice = "";

    private static string DiscoverCategory(DiscoverMod mod) => mod.category?.Trim() ?? "";

    private static string DiscoverAuthor(DiscoverMod mod) => mod.author?.Trim() ?? "";

    public async Task LoadDiscoverMods()
    {
        if (discoverLoading) return;
        discoverLoading = true;
        DiscoverStatusText.Text = "Loading mods…";
        try
        {
            var result = await orbitContent.ReadAsync<List<DiscoverMod>>("mods");
            discoverMods = result.Value.Where(x => x != null && !string.IsNullOrWhiteSpace(x.name)).ToList();
            discoverLoadNotice = result.IsCached ? " · Saved catalog" : "";
            BuildDiscoverFilters();
            ApplyDiscoverFilters();
        }
        catch (Exception ex)
        {
            discoverLoadNotice = discoverMods.Count > 0 ? " · Refresh failed; showing previous results." : " · Unable to load catalog. Try Refresh.";
            ApplyDiscoverFilters();
            ErrorManager.LogError($"Discover error: " + ex);
        }
        finally
        {
            discoverLoading = false;
        }
    }

    private void BuildDiscoverFilters()
    {
        updatingDiscoverFilters = true;
        try
        {
            var categories = discoverMods.Select(DiscoverCategory).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            var authors = discoverMods.Select(DiscoverAuthor).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            discoverCategories.IntersectWith(categories);
            discoverAuthors.IntersectWith(authors);
            AddFilters(DiscoverCategoryFilters, categories, discoverCategories, true);
            AddFilters(DiscoverAuthorFilters, authors, discoverAuthors, false);
            DiscoverCategorySection.Visibility = categories.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            DiscoverAuthorSection.Visibility = authors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            updatingDiscoverFilters = false;
        }
    }

    private void AddFilters(StackPanel panel, IEnumerable<string> values, HashSet<string> selection, bool category)
    {
        panel.Children.Clear();
        foreach (var value in values)
        {
            int count = discoverMods.Count(x => string.Equals(category ? DiscoverCategory(x) : DiscoverAuthor(x), value, StringComparison.OrdinalIgnoreCase));
            var checkbox = new CheckBox
            {
                Style = (Style)FindResource("DiscoverFilterCheckBox"),
                Content = new TextBlock
                {
                    Text = $"{value}  ({count})",
                    TextWrapping = TextWrapping.Wrap
                },
                Foreground = new SolidColorBrush(Color.FromRgb(191, 178, 201)),
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 12,
                Cursor = Cursors.Hand,
                IsChecked = selection.Contains(value)
            };
            checkbox.Checked += (_, _) =>
            {
                selection.Add(value);
                if (!updatingDiscoverFilters) ApplyDiscoverFilters();
            };
            checkbox.Unchecked += (_, _) =>
            {
                selection.Remove(value);
                if (!updatingDiscoverFilters) ApplyDiscoverFilters();
            };
            panel.Children.Add(checkbox);
        }
    }

    private void ApplyDiscoverFilters(bool resetPage = true)
    {
        if (DiscoverModsPanel == null || DiscoverSearchBox == null || DiscoverPageText == null) return;
        if (resetPage) discoverPage = 0;
        string search = DiscoverSearchBox.Text.Trim();
        IEnumerable<DiscoverMod> filtered = discoverMods.Where(mod =>
          (string.IsNullOrEmpty(search) || $"{mod.name} {mod.description} {mod.author}".Contains(search, StringComparison.OrdinalIgnoreCase)) &&
          (discoverCategories.Count == 0 || discoverCategories.Contains(DiscoverCategory(mod))) &&
          (discoverAuthors.Count == 0 || discoverAuthors.Contains(DiscoverAuthor(mod))));
        filtered = discoverSort
        switch
        {
            1 => filtered.OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase),
            2 => filtered.OrderByDescending(x => x.name, StringComparer.OrdinalIgnoreCase),
            3 => filtered.OrderBy(x => x.author, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.name),
            _ => filtered
        };
        var results = filtered.ToList();
        int pages = Math.Max(1, (int)Math.Ceiling(results.Count / (double)discoverPageSize));
        discoverPage = Math.Clamp(discoverPage, 0, pages - 1);
        ShowDiscoverMods(results.Skip(discoverPage * discoverPageSize).Take(discoverPageSize));
        DiscoverStatusText.Text = $"{results.Count} of {discoverMods.Count} mods" + discoverLoadNotice;
        DiscoverEmptyState.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DiscoverPageText.Text = $"Page {discoverPage + 1} of {pages}";
        DiscoverPreviousButton.IsEnabled = discoverPage > 0;
        DiscoverNextButton.IsEnabled = discoverPage < pages - 1;
    }

    private void ShowDiscoverMods(IEnumerable<DiscoverMod> mods)
    {
        DiscoverModsPanel.Children.Clear();
        foreach (var mod in mods) DiscoverModsPanel.Children.Add(CreateDiscoverModCard(mod));
    }

    private Border CreateDiscoverModCard(DiscoverMod mod)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 23, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(57, 45, 66)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(86)
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var picture = new Image
        {
            Stretch = Stretch.Uniform,
            Source = new BitmapImage(new Uri("pack://application:,,,/OrbitClient;component/Assets/Orbit-removebg-preview.png"))
        };
        var imageHolder = new Border
        {
            Width = 74,
            Height = 74,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(42, 33, 50)),
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = picture
        };
        if (!string.IsNullOrWhiteSpace(mod.image)) _ = LoadRemoteImage(picture, mod.image);
        grid.Children.Add(imageHolder);
        var content = new StackPanel();
        Grid.SetColumn(content, 1);
        var title = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        };
        title.Inlines.Add(new System.Windows.Documents.Run(mod.name));
        if (!string.IsNullOrWhiteSpace(mod.author)) title.Inlines.Add(new System.Windows.Documents.Run("  by " + mod.author)
        {
            Foreground = new SolidColorBrush(Color.FromRgb(169, 154, 178)),
            FontSize = 12,
            FontWeight = FontWeights.Normal
        });
        content.Children.Add(title);
        content.Children.Add(new TextBlock
        {
            Text = mod.description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 179, 199)),
            FontSize = 12,
            LineHeight = 18,
            Margin = new Thickness(0, 7, 0, 12)
        });
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });
        var tags = new WrapPanel
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var label in new[] {
      DiscoverCategory(mod), string.IsNullOrWhiteSpace(mod.version) ? "" : "v" + mod.version
    })
        {
            if (string.IsNullOrEmpty(label)) continue;
            tags.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(51, 39, 61)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 5, 4),
                Child = new TextBlock
                {
                    Text = label,
                    Foreground = new SolidColorBrush(Color.FromRgb(210, 187, 225)),
                    FontSize = 10
                }
            });
        }
        footer.Children.Add(tags);
        var install = new Button
        {
            Content = "Install ↓",
            Tag = mod,
            Style = (Style)FindResource("Primary"),
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = !string.IsNullOrWhiteSpace(mod.download)
        };
        System.Windows.Automation.AutomationProperties.SetName(install, "Install " + mod.name);
        install.Click += InstallDiscoverMod_Click;
        Grid.SetColumn(install, 1);
        footer.Children.Add(install);
        content.Children.Add(footer);
        grid.Children.Add(content);
        card.Child = grid;
        return card;
    }

    private void DiscoverSort_Click(object sender, RoutedEventArgs e)
    {
        discoverSort = (discoverSort + 1) % 4;
        DiscoverSortButton.Content = new[] { "Sort: Featured ↕", "Sort: Name A–Z ↕", "Sort: Name Z–A ↕", "Sort: Author ↕" }[discoverSort];
        ApplyDiscoverFilters();
    }
    private void DiscoverPageSize_Click(object sender, RoutedEventArgs e)
    {
        discoverPageSize = discoverPageSize == 8 ? 16 : discoverPageSize == 16 ? 24 : 8;
        preferences.DiscoverPageSize = discoverPageSize;
        SaveFeaturePreferences();
        DiscoverPageSizeButton.Content = $"View: {discoverPageSize}";
        ApplyDiscoverFilters();
    }
    private void DiscoverPrevious_Click(object sender, RoutedEventArgs e)
    {
        discoverPage--;
        ApplyDiscoverFilters(false);
        Discover.ScrollToTop();
    }
    private void DiscoverNext_Click(object sender, RoutedEventArgs e)
    {
        discoverPage++;
        ApplyDiscoverFilters(false);
        Discover.ScrollToTop();
    }
    private void ClearDiscoverFilters_Click(object sender, RoutedEventArgs e)
    {
        discoverCategories.Clear();
        discoverAuthors.Clear();
        DiscoverSearchBox.Text = "";
        BuildDiscoverFilters();
        ApplyDiscoverFilters();
    }
    private async Task LoadRemoteImage(Image imageControl, string imageUrl)
    {
        try
        {
            byte[] bytes = await orbitContent.DownloadAsync(imageUrl, 8 * 1024 * 1024, TimeSpan.FromSeconds(15));
            using MemoryStream stream = new MemoryStream(bytes);
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            imageControl.Source = bitmap;
        }
        catch (Exception ex)
        {
            ErrorManager.LogError($"Failed loading mod image: " + ex);
        }
    }
    private async void RefreshDiscover_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        button.IsEnabled = false;
        try
        {
            await LoadDiscoverMods();
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
    private void DiscoverSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyDiscoverFilters();
    private async void InstallDiscoverMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.Tag is not DiscoverMod mod) return;
        button.IsEnabled = false;
        try { await InstallDiscoverMod(mod); }
        finally { button.IsEnabled = true; }
    }
    private async Task InstallDiscoverMod(DiscoverMod mod)
    {
        try
        {
            DiscoverStatusText.Text = $"Downloading {mod.name}...";
            await InstallModFile(mod);
            DiscoverStatusText.Text = $"{mod.name} installed.";
        }
        catch (Exception ex)
        {
            DiscoverStatusText.Text = $"Could not install {mod.name}: {ex.Message}";
            ErrorManager.LogError($"Could not install {mod.name}: {ex.Message}");
        }
    }

}
