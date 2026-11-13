using System.Windows.Documents;
using Microsoft.Win32;
using OrbitClient.Scripts;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace OrbitClient;

public partial class MainWindow
{
    private Border? themePreview;
    private TextBlock? settingsStatus;
    private readonly List<StackPanel> customSettingsPanels = new();
    private static readonly string[] AppearanceKeys = ["Accent", "Background", "Compact", "Stars", "Font", "TextScale", "SidebarWidth",
    "ShowNavLabels", "CornerRadius", "SurfaceOpacity", "BackgroundOpacity", "ImageFit", "StarCount", "StarSpeed", "ReducedMotion"
    ];

    private void InitializeCustomization()
    {
        NormalizePreferences();
        if (preferences.RememberWindow)
        {
            Width = preferences.WindowWidth;
            Height = preferences.WindowHeight;
        }
        if (preferences.StartMaximized) WindowState = WindowState.Maximized;
        discoverPageSize = preferences.DiscoverPageSize;
        UpdateDiscoverChoices();
        MaterialSymbols.Label(DiscoverRefreshButton, "Refresh", "refresh");
        MaterialSymbols.Label(DiscoverClearButton, "Clear filters", "restart_alt");
        DiscoverPreviousButton.Content = "Previous"; DiscoverNextButton.Content = "Next";
        var searchSymbol = MaterialSymbols.Icon("search", 18);
        ((System.Windows.Shapes.Path)searchSymbol).SetResourceReference(Shape.FillProperty, "Muted");
        DiscoverSearchIconHost.Content = searchSymbol;
        BuildExtraSettings();
        ApplyAppearance();

        Closing += (_, e) =>
        {
            if (preferences.ConfirmClose && MessageBox.Show(this, "Close Orbit?", "Orbit", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
        };
    }

    private void NormalizePreferences()
    {
        static double Limit(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        preferences.TextScale = Limit(preferences.TextScale, .85, 1.2, 1);
        preferences.SidebarWidth = Limit(preferences.SidebarWidth, 180, 260, 200);
        preferences.CornerRadius = Limit(preferences.CornerRadius, 0, 24, 12);
        preferences.SurfaceOpacity = Limit(preferences.SurfaceOpacity, .6, 1, .95);
        preferences.BackgroundOpacity = Limit(preferences.BackgroundOpacity, .1, 1, .35);
        preferences.StarSpeed = Limit(preferences.StarSpeed, .25, 2, 1);
        preferences.StarCount = Math.Clamp(preferences.StarCount, 10, 120);
        preferences.DownloadTimeout = Math.Clamp(preferences.DownloadTimeout, 20, 180);
        preferences.WindowWidth = Limit(preferences.WindowWidth, 1000, 3840, 1220);
        preferences.WindowHeight = Limit(preferences.WindowHeight, 650, 2160, 760);
        preferences.Background ??= "Midnight";
        preferences.GamePath ??= "";
        preferences.ImageFit ??= "Fill";
        if (preferences.Font is not ("Segoe UI"
            or "Arial")) preferences.Font = "Segoe UI";
        if (preferences.StartupPage is not ("Home"
            or "Discover"
            or "Appearance"
            or "Settings")) preferences.StartupPage = "Home";
        if (preferences.DiscoverPageSize is not (8 or 16 or 24)) preferences.DiscoverPageSize = 8;
        if (!Regex.IsMatch(preferences.Accent ?? "", "^#[0-9a-fA-F]{6}$")) preferences.Accent = "#9333EA";
    }

    private void SaveWindowPreferences()
    {
        if (preferences.RememberWindow)
        {
            var bounds = RestoreBounds;
            preferences.WindowWidth = bounds.IsEmpty ? Width : bounds.Width;
            preferences.WindowHeight = bounds.IsEmpty ? Height : bounds.Height;
        }
        SaveFeaturePreferences();
    }

    private StackPanel CustomSection(string title, string description, string icon, Panel parent)
    {
        var panel = new StackPanel();
        var heading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var symbol = MaterialSymbols.Icon(icon, 22);
        ((System.Windows.Shapes.Path)symbol).SetResourceReference(Shape.FillProperty, "AccentLight");
        heading.Children.Add(symbol);
        var label = FeatureText(title, 19);
        label.Margin = new Thickness(8, 0, 0, 0);
        heading.Children.Add(label);
        panel.Children.Add(heading);
        panel.Children.Add(FeatureText(description, 12, true));
        parent.Children.Add(FeatureCard(panel));
        return panel;
    }

    private void ToggleSetting(Panel parent, string label, string description, bool value, Action<bool> change)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 6, 0, 6)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });
        var words = new StackPanel
        {
            Margin = new Thickness(0, 0, 14, 0)
        };
        var title = FeatureText(label, 13);
        title.Margin = new Thickness(0, 0, 0, 3);
        words.Children.Add(title);
        var detail = FeatureText(description, 11, true);
        detail.Margin = new Thickness(0);
        words.Children.Add(detail);
        row.Children.Add(words);
        var toggle = new ToggleButton
        {
            IsChecked = value,
            Style = (Style)FindResource("Toggle"),
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetName(toggle, label);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        toggle.Click += (_, _) =>
        {
            change(toggle.IsChecked == true);
            ApplyAppearance();
            SaveFeaturePreferences();
        };
        parent.Children.Add(row);
    }

    private void SliderSetting(Panel parent, string label, double min, double max, double value, double step, Action<double> change, string suffix = "")
    {
        var line = new DockPanel
        {
            Margin = new Thickness(0, 6, 0, 0)
        };
        var number = FeatureText($"{value:0.##}{suffix}", 12, true);
        Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
        number.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(number, Dock.Right);
        line.Children.Add(number);
        line.Children.Add(FeatureText(label, 13));
        parent.Children.Add(line);
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 0, 0, 6),
            Style = (Style)FindResource("OrbitSlider")
        };
        System.Windows.Automation.AutomationProperties.SetName(slider, label);
        slider.ValueChanged += (_, _) =>
        {
            number.Text = $"{slider.Value:0.##}{suffix}";
            change(slider.Value);
            ApplyAppearance();
            SaveFeaturePreferences();
        };
        parent.Children.Add(slider);
    }

    private void Choices(Panel parent, string label, string[] values, string selected, Action<string> change)
    {
        parent.Children.Add(FeatureText(label, 13));
        var row = new WrapPanel();
        foreach (var value in values)
        {
            var button = FeatureButton(value, () =>
            {
                change(value);
                ApplyAppearance();
                SaveFeaturePreferences();
                foreach (var item in row.Children.OfType<Button>()) item.SetResourceReference(BackgroundProperty, (string)item.Tag == value ? "Accent" : "ControlSurface");
                return Task.CompletedTask;
            });
            button.Tag = value;
            button.SetResourceReference(BackgroundProperty, selected == value ? "Accent" : "ControlSurface");
            row.Children.Add(button);
        }
        parent.Children.Add(row);
    }

    private void BuildAppearance()
    {
        var hero = new Grid
        {
            Margin = new Thickness(0, 0, 0, 12)
        };
        hero.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        hero.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(230)
        });
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0)
        };
        text.Children.Add(FeatureText("Pick your style", 26));
        text.Children.Add(FeatureText("Changes save automatically.", 13, true));
        hero.Children.Add(text);
        themePreview = new Border
        {
            Height = 124,
            Padding = new Thickness(15),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(75, 61, 91))
        };
        Grid.SetColumn(themePreview, 1);
        hero.Children.Add(themePreview);
        FeatureContent.Children.Add(hero);
        var presets = CustomSection("Themes", "Choose a look.", "palette", FeatureContent);
        var tiles = new WrapPanel();
        foreach (var (name, accent, background, tint) in new[] {
      ("Orbit", "#9333EA", "Midnight", "#17101F"), ("Aurora", "#047857", "Aurora", "#0D302C"),
      ("Tidal", "#0369A1", "Deep ocean", "#0C233A"), ("Ember", "#BE185D", "Ember", "#38172A"),
      ("Nebula", "#7C3AED", "Nebula", "#28153B"),
      ("Slate", "#475569", "Slate", "#1E293B"), ("Forest", "#15803D", "Forest", "#102A1C"),
      ("Sunset", "#C2410C", "Sunset", "#351A16"), ("Cherry", "#BE123C", "Cherry", "#321223"),
      ("Arctic", "#0E7490", "Arctic", "#12303A"), ("Sand", "#A16207", "Sand", "#2D2615"),
      ("Graphite", "#52525B", "Graphite", "#18181B")
    })
        {
            var button = FeatureButton(name, () =>
            {
                preferences.Accent = accent;
                preferences.Background = background;
                ApplyAppearance();
                SaveFeaturePreferences();
                OpenFeaturePage("Appearance");
                return Task.CompletedTask;
            });
            button.Width = 132;
            button.Padding = new Thickness(0);
            button.Background = Brushes.Transparent;
            var inner = new StackPanel();
            var art = new Grid
            {
                Height = 58,
                Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(tint), Color.FromRgb(9, 8, 15), 30)
            };
            art.Children.Add(new Border
            {
                Width = 58,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = (Brush)new BrushConverter().ConvertFromString(accent)!,
                VerticalAlignment = VerticalAlignment.Center
            });
            inner.Children.Add(art);
            var caption = FeatureText(name, 12);
            caption.Margin = new Thickness(6, 6, 6, 6);
            inner.Children.Add(caption);
            button.Content = new Border
            {
                Child = inner,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(68, 53, 81)),
                ClipToBounds = true
            };
            tiles.Children.Add(button);
        }
        presets.Children.Add(tiles);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(18)
        });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        var right = new StackPanel();
        Grid.SetColumn(right, 2);
        columns.Children.Add(left);
        columns.Children.Add(right);
        FeatureContent.Children.Add(new Expander
        {
            Header = "Customize",
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 12),
            Content = columns
        });
        var colors = CustomSection("Color & surfaces", "Set the accent and shape of your interface.", "palette", left);
        var swatches = new WrapPanel();
        foreach (var (label, hex) in new[] {
      ("Violet", "#9333EA"), ("Ocean", "#0369A1"), ("Mint", "#047857"), ("Rose", "#BE185D"), ("Amber", "#92400E")
    })
        {
            var button = FeatureButton(label, () =>
            {
                preferences.Accent = hex;
                ApplyAppearance();
                SaveFeaturePreferences();
                return Task.CompletedTask;
            });
            button.Width = 36;
            button.Height = 36;
            button.Padding = new Thickness(0);
            button.Background = (Brush)new BrushConverter().ConvertFromString(hex)!;
            button.Content = "";
            button.ToolTip = label;
            swatches.Children.Add(button);
        }
        colors.Children.Add(swatches);
        var hexRow = new DockPanel
        {
            Margin = new Thickness(0, 6, 0, 6)
        };
        var hexBox = new TextBox
        {
            Text = preferences.Accent,
            Padding = new Thickness(6),
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(11, 9, 17)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(66, 50, 78)),
            MinWidth = 95
        };
        System.Windows.Automation.AutomationProperties.SetName(hexBox, "Custom accent hex");
        var apply = FeatureButton("Apply hex", () =>
        {
            if (!Regex.IsMatch(hexBox.Text, "^#[0-9a-fA-F]{6}$")) throw new IOException("Enter a six-digit color such as #7C3AED.");
            preferences.Accent = hexBox.Text;
            ApplyAppearance();
            SaveFeaturePreferences();
            SetFeatureStatus("Custom accent saved.");
            return Task.CompletedTask;
        });
        DockPanel.SetDock(apply, Dock.Right);
        hexRow.Children.Add(apply);
        hexRow.Children.Add(hexBox);
        colors.Children.Add(hexRow);
        SliderSetting(colors, "Card rounding", 0, 24, preferences.CornerRadius, 2, x => preferences.CornerRadius = x, " px");
        SliderSetting(colors, "Card opacity", 60, 100, preferences.SurfaceOpacity * 100, 5, x => preferences.SurfaceOpacity = x / 100, "%");

        var wallpaper = CustomSection("Background", "Layer a gradient or your own image behind Orbit.", "image", right);
        Choices(wallpaper, "Backdrop", ["Midnight", "Nebula", "Deep ocean", "Aurora", "Ember"], preferences.Background, x => preferences.Background = x);
        wallpaper.Children.Add(FeatureButton("Choose image…", () =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp"
            };
            if (dialog.ShowDialog(this) == true)
            {
                preferences.Background = dialog.FileName;
                ApplyAppearance();
                SaveFeaturePreferences();
            }
            return Task.CompletedTask;
        }));
        SliderSetting(wallpaper, "Image visibility", 10, 100, preferences.BackgroundOpacity * 100, 5, x => preferences.BackgroundOpacity = x / 100, "%");
        Choices(wallpaper, "Image fit", ["Fill", "Fit", "Stretch"], preferences.ImageFit, x => preferences.ImageFit = x);

        var layout = CustomSection("Type & layout", "Choose the balance between space and information.", "dashboard", left);
        Choices(layout, "Typeface", ["Segoe UI", "Arial"], preferences.Font, x => preferences.Font = x);
        SliderSetting(layout, "Content scale", 85, 120, preferences.TextScale * 100, 5, x => preferences.TextScale = x / 100, "%");
        SliderSetting(layout, "Sidebar width", 180, 260, preferences.SidebarWidth, 10, x => preferences.SidebarWidth = x, " px");
        ToggleSetting(layout, "Compact sidebar", "Use tighter navigation spacing.", preferences.Compact, x => preferences.Compact = x);
        ToggleSetting(layout, "Show navigation labels", "Turn off for an icon-only sidebar.", preferences.ShowNavLabels, x => preferences.ShowNavLabels = x);

        var motion = CustomSection("Interaction feedback", "Short transitions confirm actions and reveal loaded content.", "settings", right);
        ToggleSetting(motion, "Reduce motion", "Use static skeletons and immediate state changes.", preferences.ReducedMotion, x => preferences.ReducedMotion = x);

        var footer = new WrapPanel();
        footer.Children.Add(FeatureButton("Export theme", ExportTheme));
        footer.Children.Add(FeatureButton("Import theme", ImportTheme));
        footer.Children.Add(FeatureButton("Reset appearance", () =>
        {
            CopyAppearance(new ClientPreferences());
            ApplyAppearance();
            SaveFeaturePreferences();
            OpenFeaturePage("Appearance");
            return Task.CompletedTask;
        }));
        FeatureContent.Children.Add(footer);
        RefreshThemePreview();
    }

    private void CopyAppearance(ClientPreferences source)
    {
        foreach (var name in AppearanceKeys)
        {
            var property = typeof(ClientPreferences).GetProperty(name)!;
            property.SetValue(preferences, property.GetValue(source));
        }
        NormalizePreferences();
    }

    private Task ExportTheme()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Orbit theme|*.orbit-theme.json",
            FileName = "My Orbit.orbit-theme.json"
        };
        if (dialog.ShowDialog(this) != true) return Task.CompletedTask;
        var all = JsonSerializer.SerializeToNode(preferences)!.AsObject();
        var appearance = new JsonObject();
        foreach (var key in AppearanceKeys) appearance[key] = all[key]?.DeepClone();

        if (File.Exists(preferences.Background)) appearance["Background"] = "Midnight";
        File.WriteAllText(dialog.FileName, new JsonObject
        {
            ["format"] = 1,
            ["appearance"] = appearance
        }.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        SetFeatureStatus("Theme exported. Personal settings and local image paths are excluded.");
        return Task.CompletedTask;
    }

    private Task ImportTheme()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Orbit theme|*.json"
        };
        if (dialog.ShowDialog(this) != true) return Task.CompletedTask;
        if (new FileInfo(dialog.FileName).Length > 64 * 1024) throw new IOException("Theme files must be smaller than 64 KB.");
        var document = JsonNode.Parse(File.ReadAllText(dialog.FileName));
        if (document?["format"]?.GetValue<int>() != 1 || document["appearance"] == null) throw new IOException("This is not a supported Orbit theme.");
        var incoming = document["appearance"]!.Deserialize<ClientPreferences>() ??
          throw new IOException("Theme is empty.");
        if (incoming.Background is not ("Midnight"
            or "Nebula"
            or "Deep ocean"
            or "Aurora"
            or "Ember")) incoming.Background = "Midnight";
        CopyAppearance(incoming);
        ApplyAppearance();
        SaveFeaturePreferences();
        OpenFeaturePage("Appearance");
        SetFeatureStatus("Theme imported.");
        return Task.CompletedTask;
    }

    private void BuildExtraSettings()
    {
        foreach (var panel in customSettingsPanels)
            if (panel.Parent is Border card) SettingsContent.Children.Remove(card.Parent as UIElement ?? card);
        customSettingsPanels.Clear();
        var startup = CustomSection("Startup & window", "Decide how Orbit opens and closes.", "dashboard", SettingsContent);
        customSettingsPanels.Add(startup);
        settingsStatus = FeatureText("", 12, true);
        startup.Children.Insert(2, settingsStatus);
        Choices(startup, "Open on startup", ["Home", "Discover", "Appearance", "Settings"], preferences.StartupPage, x => preferences.StartupPage = x);
        ToggleSetting(startup, "Remember window size", "Restore your preferred dimensions next time.", preferences.RememberWindow, x => preferences.RememberWindow = x);
        ToggleSetting(startup, "Start maximized", "Open Orbit using the whole screen.", preferences.StartMaximized, x => preferences.StartMaximized = x);
        ToggleSetting(startup, "Confirm before closing", "Ask before closing the launcher window.", preferences.ConfirmClose, x => preferences.ConfirmClose = x);
        var mods = CustomSection("Mods & downloads", "Set your preferred checks and download behavior.", "extension", SettingsContent);
        customSettingsPanels.Add(mods);
        ToggleSetting(mods, "Check setup before launch", "Review setup issues before starting Gorilla Tag.", preferences.CheckBeforeLaunch, x => preferences.CheckBeforeLaunch = x);
        ToggleSetting(mods, "Automatic snapshots", "Save a recovery point before installs and updates.", preferences.AutoSnapshot, x => preferences.AutoSnapshot = x);
        SliderSetting(mods, "Download timeout", 20, 180, preferences.DownloadTimeout, 10, x => preferences.DownloadTimeout = (int)x, " seconds");
        Choices(mods, "Discover results per page", ["8", "16", "24"], preferences.DiscoverPageSize.ToString(), x =>
        {
            preferences.DiscoverPageSize = discoverPageSize = int.Parse(x);
            UpdateDiscoverChoices();
            ApplyDiscoverFilters();
        });
        var data = CustomSection("Data & companion", "Manage local Orbit files and the base game companion.", "folder_open", SettingsContent);
        customSettingsPanels.Add(data);
        var actions = new WrapPanel();
        actions.Children.Add(FeatureButton("Open Orbit data", () =>
        {
            Directory.CreateDirectory(workspace.DataRoot);
            OpenLocal(workspace.DataRoot);
            return Task.CompletedTask;
        }));
        actions.Children.Add(FeatureButton("Install companion", async () =>
        {
            await InstallCompanionAsync(installLoader: true);
            MessageBox.Show(this, "The base companion is installed. Restart the game to load it.", "Orbit");
        }));
        data.Children.Add(actions);
        foreach (var panel in customSettingsPanels)
        {
            if (panel.Parent is not Border card) continue;
            var index = SettingsContent.Children.IndexOf(card);
            SettingsContent.Children.Remove(card);
            SettingsContent.Children.Insert(index, new Expander
            {
                Header = panel == startup ? "Startup" : panel == mods ? "Downloads" : "More",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10),
                Content = card
            });
        }
    }

    private void ApplyAppearance()
    {
        NormalizePreferences();
        InteractionMotion.SetReduced(this, preferences.ReducedMotion);
        var accent = (Color)ColorConverter.ConvertFromString(preferences.Accent);
        Resources["Accent"] = new SolidColorBrush(accent);
        var perceptual = PerceptualColor.ToOklch(accent);
        Resources["AccentForeground"] = new SolidColorBrush(PerceptualColor.Foreground(accent));
        Resources["AccentLight"] = new SolidColorBrush(PerceptualColor.FromOklch(perceptual with { L = .84, C = Math.Min(perceptual.C, .13) }));
        Resources["AccentSoft"] = new SolidColorBrush(PerceptualColor.FromOklch(perceptual with { L = .28, C = Math.Min(perceptual.C, .07) }));
        foreach (var skeleton in VisualDescendants(this).OfType<FrameworkElement>().Where(Skeleton.GetActive)) Skeleton.Update(skeleton);
        if (preferences.ReducedMotion) { CancelNewsTransition(); foreach (var card in DiscoverModsPanel.Children.OfType<UIElement>()) { card.BeginAnimation(OpacityProperty, null); card.Opacity = 1; } }
        Resources["Surface"] = new SolidColorBrush(Color.FromArgb((byte)(255 * preferences.SurfaceOpacity), 24, 19, 30));
        Resources["ControlSurface"] = new SolidColorBrush(Color.FromRgb(37, 29, 45));
        Resources["CardRadius"] = new CornerRadius(preferences.CornerRadius);
        FontFamily = new FontFamily(preferences.Font);
        var icons = new Dictionary<string,
          string>
        {
            ["Home"] = "home",
            ["Accounts"] = "person",
            ["Steam accounts"] = "person",
            ["Discover"] = "explore",
            ["Mod Library"] = "extension",
            ["Capes"] = "palette",
            ["News"] = "newspaper",
            ["Snapshots"] = "backup",
            ["BepInEx"] = "download",
            ["Appearance"] = "palette",
            ["Settings"] = "settings"
        };
        foreach (var button in NavPanel.Children.OfType<Button>().Append(ProfileNav))
        {
            var key = (string)button.Tag;
            if (key == "Accounts") UpdateProfileNav();
            else MaterialSymbols.Label(button, key == "Discover" ? "Browse mods" : key == "Mod Library" ? "Installed mods" : key == "Steam accounts" ? "Steam" : key, icons[key], preferences.ShowNavLabels);
            button.Padding = new Thickness(14, preferences.Compact ? 7 : 11, 14, preferences.Compact ? 7 : 11);
            button.Style = (Style)FindResource(preferences.ReducedMotion ? "Nav" : "AnimatedNav");
        }
        foreach (var label in NavPanel.Children.OfType<TextBlock>()) label.Visibility = preferences.ShowNavLabels ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(preferences.ShowNavLabels ? preferences.SidebarWidth : 78);
        SidebarBrandText.Visibility = preferences.ShowNavLabels ? Visibility.Visible : Visibility.Collapsed;
        foreach (var page in new[] {
      HomePage,
      Accounts,
      SteamAccountsPage,
      Discover,
      News,
      SettingsPage,
      FeaturePage
    })
        {
            if (page.Content is FrameworkElement content) content.LayoutTransform = new ScaleTransform(preferences.TextScale, preferences.TextScale);
            page.Style = null;
            if (preferences.ReducedMotion)
            {
                page.BeginAnimation(OpacityProperty, null);
                page.Opacity = 1;
                page.RenderTransform = Transform.Identity;
            }
            else if (page != FeaturePage)
            {
                page.ClearValue(OpacityProperty);
                page.ClearValue(RenderTransformProperty);
            }
        }
        foreach (var border in VisualDescendants(this).OfType<Border>().Where(x => x.Tag as string == "MotionCard"))
            border.Style = (Style)FindResource(preferences.ReducedMotion ? "Card" : "AnimatedCard");
        ContentBackground.Background = BackgroundBrush();
        foreach (var child in StarField.Children.OfType<Ellipse>()) child.BeginAnimation(OpacityProperty, null);
        StarField.Children.Clear();
        FitSidebar();
        if (preferences.ReducedMotion)
        {
            HeroContent.BeginAnimation(OpacityProperty, null); HeroContent.Opacity = 1;
            HeroTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null); HeroTranslate.Y = 0;
        }
        RefreshThemePreview();
    }

    private Brush BackgroundBrush()
    {
        var color = OrbitClient.Appearance.ThemeMessage.BackgroundHex(preferences.Background);
        Brush brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(color), Color.FromRgb(8, 6, 14), 35);
        if (!File.Exists(preferences.Background)) return brush;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 1920;
            image.UriSource = new Uri(preferences.Background, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return new ImageBrush(image)
            {
                Opacity = preferences.BackgroundOpacity,
                Stretch = preferences.ImageFit
              switch
                {
                    "Fit" => Stretch.Uniform,
                    "Stretch" => Stretch.Fill,
                    _ => Stretch.UniformToFill
                }
            };
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            SetFeatureStatus("Background image could not be loaded. Using Midnight.");
            return brush;
        }
    }

    private void RefreshThemePreview()
    {
        if (themePreview == null) return;
        themePreview.Background = BackgroundBrush();
        themePreview.CornerRadius = new CornerRadius(preferences.CornerRadius);
        var preview = new StackPanel();
        preview.Children.Add(FeatureText("ORBIT  /  LIVE PREVIEW", 10, true));
        preview.Children.Add(new Border
        {
            Background = (Brush)Resources["Surface"],
            CornerRadius = new CornerRadius(preferences.CornerRadius / 2),
            Padding = new Thickness(6),
            Child = FeatureText("Ready for your next session", 12)
        });
        preview.Children.Add(new Border
        {
            Background = (Brush)Resources["Accent"],
            Width = 90,
            Height = 7,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 6, 0, 0)
        });
        themePreview.Child = preview;
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield
            return child;
            foreach (var nested in VisualDescendants(child)) yield
            return nested;
        }
    }
}
