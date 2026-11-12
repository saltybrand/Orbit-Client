using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OrbitClient.Scripts;

namespace OrbitClient;

public partial class MainWindow
{
    private int newsTransitionGeneration;
    private UIElement? newsTransitionTarget;

    private void CancelNewsTransition()
    {
        newsTransitionGeneration++;
        if (newsTransitionTarget is not null) newsTransitionTarget.Opacity = 1;
        newsTransitionTarget = null;
        SharedNewsLayer?.Children.Clear();
    }

    private void TransitionNewsArtwork(Image? source, Image destination)
    {
        CancelNewsTransition();
        if (preferences.ReducedMotion || source?.Source is null || source.ActualWidth <= 0 || source.Visibility != Visibility.Visible || News.Visibility != Visibility.Visible) return;
        NewsDetailsOverlay.UpdateLayout();
        if (destination.ActualWidth <= 0 || destination.ActualHeight <= 0) return;
        Rect from, to;
        try
        {
            from = source.TransformToVisual(SharedNewsLayer).TransformBounds(new Rect(source.RenderSize));
            to = destination.TransformToVisual(SharedNewsLayer).TransformBounds(new Rect(destination.RenderSize));
        }
        catch (InvalidOperationException) { return; }
        // A recycled/offscreen card has no meaningful origin. Keep the normal content reveal.
        if (!new Rect(SharedNewsLayer.RenderSize).IntersectsWith(from)) return;
        var moving = new Image { Source = source.Source, Stretch = Stretch.UniformToFill, Width = from.Width, Height = from.Height, IsHitTestVisible = false };
        Canvas.SetLeft(moving, from.X); Canvas.SetTop(moving, from.Y);
        SharedNewsLayer.Children.Add(moving);
        destination.Source = source.Source;
        destination.Opacity = 0;
        newsTransitionTarget = destination;
        var generation = newsTransitionGeneration;
        var duration = TimeSpan.FromMilliseconds(240);
        DoubleAnimation Motion(double start, double end) => new(start, end, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        moving.BeginAnimation(Canvas.LeftProperty, Motion(from.X, to.X), HandoffBehavior.SnapshotAndReplace);
        moving.BeginAnimation(Canvas.TopProperty, Motion(from.Y, to.Y), HandoffBehavior.SnapshotAndReplace);
        moving.BeginAnimation(HeightProperty, Motion(from.Height, to.Height), HandoffBehavior.SnapshotAndReplace);
        var width = Motion(from.Width, to.Width);
        width.Completed += (_, _) => { if (generation == newsTransitionGeneration) CancelNewsTransition(); };
        moving.BeginAnimation(WidthProperty, width, HandoffBehavior.SnapshotAndReplace);
    }
}
