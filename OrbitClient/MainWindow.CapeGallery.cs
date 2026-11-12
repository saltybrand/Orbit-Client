using OrbitClient.Capes;
using OrbitClient.Scripts;
using System.IO;
using System.Net.Http;
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
    private int capeTab, capePage, capeGalleryGeneration;
    private string capeQuery = "", capeFocusedId = "";
    private readonly Dictionary<string, BitmapSource> capeArtwork = new();
    private readonly SemaphoreSlim capeArtworkSlots = new(2, 2);
    private StackPanel? capeTiles;
    private Border? capeHero;
    private const int CapePageSize = 8;

    private void BuildCapeGallery()
    {
        capeGalleryGeneration++;
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        var owned = capeService.Catalog.Items.Count(c => c.Owned);
        var labels = new[] { $"Dressing room · {owned}", "Shop", "Redeem key" };
        for (var i = 0; i < labels.Length; i++)
        {
            var index = i;
            tabs.Children.Add(FeatureButton(labels[i], () => { capeTab = index; capePage = 0; OpenFeaturePage("Capes"); return Task.CompletedTask; }, capeTab == i));
        }
        tabs.Children.Add(FeatureButton("Refresh", async () => { capeArtwork.Clear(); await RefreshDesktopCapesAsync(); }));
        FeatureContent.Children.Add(tabs);
        if (!capeService.SignedIn)
        {
            var welcome = new StackPanel();
            welcome.Children.Add(FeatureText("Your capes, collected.", 28));
            welcome.Children.Add(FeatureText("Sign in to claim free capes, redeem a key and keep your dressing room with your Orbit account.", 14, true));
            welcome.Children.Add(FeatureButton("Sign in to Orbit", () => { Nav_Click(ProfileNav, new RoutedEventArgs()); return Task.CompletedTask; }, true));
            FeatureContent.Children.Add(FeatureCard(welcome));
        }
        if (capeTab == 2) { BuildCapeRedemption(); SetFeatureStatus(capeStatus); return; }
        capeHero = new Border { Margin = new Thickness(0, 0, 0, 14) };
        FeatureContent.Children.Add(capeHero);
        RenderCapeHero();
        var search = new TextBox { Text = capeQuery, FontSize = 14, Padding = new Thickness(6, 6, 6, 6), Background = Brushes.Transparent, Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0), ToolTip = "Search cape names" };
        var searchRow = new DockPanel();
        var searchIcon = MaterialSymbols.Icon("search", 18);
        ((System.Windows.Shapes.Path)searchIcon).Fill = new SolidColorBrush(Color.FromRgb(155, 145, 170));
        searchIcon.Margin = new Thickness(10, 0, 4, 0); DockPanel.SetDock(searchIcon, Dock.Left); searchRow.Children.Add(searchIcon);
        var searchInput = new Grid(); searchInput.Children.Add(search);
        var placeholder = new TextBlock { Text = "Search your next cape…", Foreground = new SolidColorBrush(Color.FromRgb(155, 145, 170)), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Visibility = capeQuery.Length == 0 ? Visibility.Visible : Visibility.Collapsed };
        searchInput.Children.Add(placeholder); searchRow.Children.Add(searchInput);
        System.Windows.Automation.AutomationProperties.SetName(search, "Search capes");
        search.TextChanged += (_, _) => { capeQuery = search.Text; placeholder.Visibility = capeQuery.Length == 0 ? Visibility.Visible : Visibility.Collapsed; capePage = 0; RenderCapeTiles(); };
        FeatureContent.Children.Add(FeatureText(capeTab switch { 0 => "Your collection", 1 => "Discover your next cape", _ => "Redeem your cape" }, 22));
        FeatureContent.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = (Brush)FindResource("ControlSurface"), BorderBrush = new SolidColorBrush(Color.FromRgb(48, 39, 55)), BorderThickness = new Thickness(1), Child = searchRow, Margin = new Thickness(0, 0, 0, 10) });
        capeTiles = new StackPanel();
        FeatureContent.Children.Add(capeTiles);
        RenderCapeTiles();
        if (!capeCatalogLoaded && capeStatus.Length == 0) ShowCatalogSkeletons(capeTiles, 2);
        SetFeatureStatus(capeStatus);
        if (!capeCatalogLoaded && !capeSyncBusy && capeStatus.Length == 0) _ = RefreshDesktopCapesAsync();
    }

    private void RenderCapeHero()
    {
        if (capeHero == null) return;
        var cape = capeService.Catalog.Items.FirstOrDefault(c => c.Id == capeFocusedId)
            ?? capeService.Catalog.Items.FirstOrDefault(c => c.Id == capeService.SelectedId)
            ?? capeService.Catalog.Items.FirstOrDefault(c => c.Owned);
        if (cape == null) { capeHero.Child = null; return; }
        var layout = new Grid { Margin = new Thickness(4) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(138) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.Children.Add(CapeArt(cape, 128, 148));
        var details = new StackPanel { Margin = new Thickness(14, 6, 6, 6), VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(CapeBadge(cape.Id == capeService.SelectedId ? "Equipped" : CapeAccessLabel(cape)));
        details.Children.Add(FeatureText(cape.Title, 26));
        details.Children.Add(FeatureText(cape.Description, 14, true));
        var actions = new WrapPanel();
        actions.Children.Add(CapeAction(cape));
        if (capeService.SelectedId.Length > 0)
            actions.Children.Add(FeatureButton("Unequip", async () => { await capeService.ClearAsync(); capeStatus = "Cape unequipped."; OpenFeaturePage("Capes"); }));
        details.Children.Add(actions);
        details.Children.Add(FeatureText(pipeServer.IsConnected ? "Gorilla Tag is connected." : "Your selected cape applies when Gorilla Tag connects.", 12, true));
        Grid.SetColumn(details, 1); layout.Children.Add(details);
        capeHero.Style = (Style)FindResource("Card");
        capeHero.Child = layout;
    }

    private void RenderCapeTiles()
    {
        if (capeTiles == null) return;
        capeTiles.Children.Clear();
        var entries = capeService.Catalog.Items.Where(c => capeTab != 0 || c.Owned)
            .Where(c => c.Title.Contains(capeQuery, StringComparison.OrdinalIgnoreCase) || c.Description.Contains(capeQuery, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Owned).ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase).ToArray();
        capePage = Math.Clamp(capePage, 0, Math.Max(0, (entries.Length - 1) / CapePageSize));
        if (entries.Length == 0)
        {
            var empty = new StackPanel();
            empty.Children.Add(FeatureText(capeQuery.Length > 0 ? "No capes match that search." : capeTab == 0 ? "Your collection starts here." : "More capes are on the way.", 22));
            empty.Children.Add(FeatureText(capeTab == 0 ? "Visit the Shop to claim a free cape, or redeem a single-use key." : "Check back after the next collection update.", 14, true));
            if (capeTab == 0) empty.Children.Add(FeatureButton("Explore the shop", () => { capeTab = 1; OpenFeaturePage("Capes"); return Task.CompletedTask; }, true));
            capeTiles.Children.Add(FeatureCard(empty));
        }
        var grid = new UniformGrid { Columns = 2 };
        grid.SizeChanged += (_, _) => grid.Columns = grid.ActualWidth < 480 ? 1 : grid.ActualWidth >= 900 ? 3 : 2;
        foreach (var cape in entries.Skip(capePage * CapePageSize).Take(CapePageSize))
        {
            var body = new StackPanel { Margin = new Thickness(4) };
            body.Children.Add(CapeArt(cape, double.NaN, 140));
            body.Children.Add(CapeBadge(CapeAccessLabel(cape)));
            body.Children.Add(FeatureText(cape.Title, 19));
            var description = FeatureText(cape.Description, 13, true);
            description.MinHeight = 40; description.MaxHeight = 60;
            body.Children.Add(description);
            var actions = new WrapPanel();
            actions.Children.Add(CapeAction(cape));
            if (cape.Owned) actions.Children.Add(FeatureButton("Inspect", () => { capeFocusedId = cape.Id; RenderCapeHero(); return Task.CompletedTask; }));
            body.Children.Add(actions);
            var card = FeatureCard(body); card.Margin = new Thickness(0, 0, 8, 8);
            if (!preferences.ReducedMotion) card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            grid.Children.Add(card);
        }
        capeTiles.Children.Add(grid);
        if (entries.Length > CapePageSize)
        {
            var pages = new WrapPanel();
            var previous = FeatureButton("Previous", () => { capePage--; RenderCapeTiles(); return Task.CompletedTask; }); previous.IsEnabled = capePage > 0;
            pages.Children.Add(previous);
            pages.Children.Add(FeatureText($"Page {capePage + 1} of {(entries.Length + CapePageSize - 1) / CapePageSize}", 13, true));
            var next = FeatureButton("Next", () => { capePage++; RenderCapeTiles(); return Task.CompletedTask; }); next.IsEnabled = (capePage + 1) * CapePageSize < entries.Length;
            pages.Children.Add(next); capeTiles.Children.Add(pages);
        }
    }

    private Button CapeAction(CapeDefinition cape)
    {
        if (!capeService.SignedIn) return FeatureButton("Sign in", () => { Nav_Click(ProfileNav, new RoutedEventArgs()); return Task.CompletedTask; });
        if (cape.Owned) return FeatureButton(cape.Id == capeService.SelectedId ? "Apply again" : "Wear cape", async () =>
        {
            await capeService.SelectAsync(cape.Id); capeFocusedId = cape.Id;
            capeStatus = pipeServer.IsConnected ? "Cape sent to Gorilla Tag." : "Cape selected for your next connection.";
            OpenFeaturePage("Capes");
        }, true);
        if (cape.UnlockType == "free") return FeatureButton("Claim free cape", async () =>
        {
            await capeService.ClaimFreeAsync(cape.Id); capeTab = 0; capeFocusedId = cape.Id;
            capeStatus = "Cape added to your dressing room.";
            OpenFeaturePage("Capes");
        }, true);
        if (cape.UnlockType == "key") return FeatureButton("Redeem a key", () => { capeTab = 2; OpenFeaturePage("Capes"); return Task.CompletedTask; }, true);
        return new Button { Content = cape.UnlockType == "admin" ? "Admin grant" : "Redeem with key", IsEnabled = false, Style = (Style)FindResource("Ghost"), Margin = new Thickness(0, 0, 6, 6) };
    }

    private static string CapeAccessLabel(CapeDefinition cape) => cape.Owned ? "In your collection" : cape.UnlockType switch { "free" => "Free to claim", "admin" => "Admin exclusive", _ => "Single-use key" };
    private Border CapeBadge(string text) => new()
    {
        Background = Brushes.Transparent, CornerRadius = new CornerRadius(0), Padding = new Thickness(0),
        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 6),
        Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("AccentLight") }
    };

    private Border CapeArt(CapeDefinition cape, double width, double height)
    {
        var grid = new Grid();
        var silhouette = new Polygon { Points = new PointCollection(new[] { new Point(28, 0), new Point(72, 0), new Point(100, 130), new Point(82, 142), new Point(50, 134), new Point(18, 142), new Point(0, 130) }), Width = 86, Height = 122, Stretch = Stretch.Fill, Fill = new LinearGradientBrush(Color.FromRgb(146, 100, 205), Color.FromRgb(52, 34, 76), 90), Opacity = cape.Owned ? 0.6 : 0.28 };
        grid.Children.Add(silhouette);
        var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(8), Visibility = Visibility.Collapsed };
        grid.Children.Add(image);
        if (!cape.Owned) grid.Children.Add(new TextBlock { Text = cape.UnlockType == "free" ? "CLAIM TO REVEAL" : "LOCKED", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) });
        var border = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromRgb(18, 13, 26)), ClipToBounds = true, Child = grid, Margin = new Thickness(0, 0, 0, 4) };
        if (cape.Owned) { silhouette.Fill = (Brush)FindResource("SkeletonSurface"); Skeleton.SetActive(silhouette, true); _ = LoadCapeArtAsync(cape, image, silhouette, capeGalleryGeneration); }
        return border;
    }

    private async Task LoadCapeArtAsync(CapeDefinition cape, Image image, Polygon silhouette, int generation)
    {
        var key = (orbitAccount?.Account?.Id ?? "") + "|" + cape.Id + "|" + cape.Image + "|" + cape.Revision;
        await capeArtworkSlots.WaitAsync();
        try
        {
            if (windowClosed || generation != capeGalleryGeneration) return;
            if (!capeArtwork.TryGetValue(key, out var bitmap))
            {
                var bytes = await capeService.PreviewBytesAsync(cape.Id);
                using var stream = new MemoryStream(bytes);
                var decoded = new BitmapImage(); decoded.BeginInit(); decoded.CacheOption = BitmapCacheOption.OnLoad;
                decoded.DecodePixelWidth = 256; decoded.StreamSource = stream; decoded.EndInit(); decoded.Freeze(); bitmap = decoded;
                if (capeArtwork.Count >= 32) capeArtwork.Remove(capeArtwork.Keys.First());
                capeArtwork[key] = bitmap;
            }
            if (generation != capeGalleryGeneration || windowClosed) return;
            image.Source = bitmap; image.Visibility = Visibility.Visible; silhouette.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException or UnauthorizedAccessException or NotSupportedException or FormatException)
        { image.ToolTip = "Preview unavailable. Refresh your dressing room to try again."; }
        finally { Skeleton.SetActive(silhouette, false); capeArtworkSlots.Release(); }
    }
}


