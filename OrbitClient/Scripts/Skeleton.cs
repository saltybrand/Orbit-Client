using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace OrbitClient.Scripts;

/// <summary>Content-sized placeholders. Hidden/unloaded placeholders never animate.</summary>
public static class Skeleton
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached(
        "Active", typeof(bool), typeof(Skeleton), new PropertyMetadata(false, ActiveChanged));
    public static bool GetActive(DependencyObject target) => (bool)target.GetValue(ActiveProperty);
    public static void SetActive(DependencyObject target, bool value) => target.SetValue(ActiveProperty, value);

    private static void ActiveChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        if ((bool)args.NewValue)
        {
            element.Loaded += Loaded;
            element.Unloaded += Unloaded;
            element.IsVisibleChanged += VisibilityChanged;
        }
        else
        {
            element.Loaded -= Loaded;
            element.Unloaded -= Unloaded;
            element.IsVisibleChanged -= VisibilityChanged;
        }
        Update(element);
    }
    private static void Loaded(object sender, RoutedEventArgs args) => Update((FrameworkElement)sender);
    private static void Unloaded(object sender, RoutedEventArgs args) => Stop((FrameworkElement)sender);
    private static void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => Update((FrameworkElement)sender);
    public static void Update(FrameworkElement element)
    {
        Stop(element);
        if (!GetActive(element) || !element.IsVisible || !element.IsLoaded || InteractionMotion.GetReduced(element)) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.45, .85, TimeSpan.FromMilliseconds(900))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }
    private static void Stop(FrameworkElement element) { element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = 1; }
    public static Border Bar(double width, double height = 14)
    {
        var bar = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12), IsHitTestVisible = false };
        bar.SetResourceReference(Border.BackgroundProperty, "SkeletonSurface");
        SetActive(bar, true);
        System.Windows.Automation.AutomationProperties.SetName(bar, "Loading content");
        return bar;
    }
}
