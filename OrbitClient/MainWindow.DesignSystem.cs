using OrbitClient.Scripts;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OrbitClient;

public partial class MainWindow
{
    private double DiscoverArtworkSize => Math.Clamp(discoverSettings.cardImageSize, 48, 64);

    private void SetHomeMetadataLoading(bool loading)
    {
        foreach (var name in new[] { "CurrentSteamAccount", "GameVersionText", "PlaytimeText" })
        {
            var value = (TextBlock)FindName(name);
            if (!loading && string.IsNullOrWhiteSpace(value.Text)) value.Text = name == "CurrentSteamAccount" ? "No account selected" : "Unavailable";
            value.Visibility = loading ? Visibility.Hidden : Visibility.Visible;
            ((Border)FindName(name + "Skeleton")).Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SetHomeNewsLoading(bool loading)
    {
        HomeNewsSkeleton.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        News1Title.Visibility = News1Desc.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowCatalogSkeletons(Panel target, int count = 3)
    {
        target.Children.Clear();
        for (var index = 0; index < count; index++)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DiscoverArtworkSize + 16) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var image = Skeleton.Bar(DiscoverArtworkSize, DiscoverArtworkSize);
            image.CornerRadius = new CornerRadius(0); // Card radius 12 − padding 14, clamped to zero.
            image.Margin = new Thickness(0);
            row.Children.Add(image);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Skeleton.Bar(180, 20));
            text.Children.Add(Skeleton.Bar(220));
            text.Children.Add(Skeleton.Bar(120, 12));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var card = new Border { Style = (Style)FindResource("Card"), MinHeight = 180, Child = row, Margin = new Thickness(0, 0, 0, 10), IsHitTestVisible = false };
            System.Windows.Automation.AutomationProperties.SetName(card, "Loading catalog item");
            target.Children.Add(card);
        }
    }

    private void RevealLoadedContent(UIElement target)
    {
        var start = target.HasAnimatedProperties ? target.Opacity : .8;
        target.BeginAnimation(OpacityProperty, null);
        target.Opacity = 1;
        if (!preferences.ReducedMotion)
            target.BeginAnimation(OpacityProperty, new DoubleAnimation(start, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop }, HandoffBehavior.SnapshotAndReplace);
    }

    private void RevealCatalogCards()
    {
        var index = 0;
        foreach (var card in DiscoverModsPanel.Children.OfType<UIElement>())
        {
            card.BeginAnimation(OpacityProperty, null); card.Opacity = 1;
            if (!preferences.ReducedMotion)
            {
                card.Opacity = .75;
                var reveal = new DoubleAnimation(.75, 1, TimeSpan.FromMilliseconds(160))
                { BeginTime = TimeSpan.FromMilliseconds(Math.Min(index, 5) * 24) };
                reveal.Completed += (_, _) => { card.BeginAnimation(OpacityProperty, null); card.Opacity = 1; };
                card.BeginAnimation(OpacityProperty, reveal, HandoffBehavior.SnapshotAndReplace);
            }
            index++;
        }
    }

    private async Task LoadDiscoverArtworkAsync(Image picture, Border holder, string source)
    {
        holder.SetResourceReference(Border.BackgroundProperty, "SkeletonSurface");
        Skeleton.SetActive(holder, true);
        try { await LoadRemoteImage(picture, source); }
        finally
        {
            Skeleton.SetActive(holder, false);
            holder.SetResourceReference(Border.BackgroundProperty, "ControlSurface");
            if (picture.Source is not null) RevealLoadedContent(picture);
            else
            {
                var fallback = MaterialSymbols.Icon("extension", 30);
                ((System.Windows.Shapes.Path)fallback).Fill = (Brush)FindResource("Muted");
                holder.Child = fallback;
                holder.ToolTip = "Mod artwork unavailable";
            }
        }
    }

    private void ClipRoundedSurface(Border surface)
    {
        void Update() => surface.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, surface.ActualWidth), Math.Max(0, surface.ActualHeight)), surface.CornerRadius.TopLeft, surface.CornerRadius.TopLeft);
        surface.SizeChanged += (_, _) => Update();
        Update();
    }
}
