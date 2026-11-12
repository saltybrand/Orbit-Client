using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OrbitClient.Scripts;

public static class InteractionMotion
{
    public static readonly DependencyProperty ReducedProperty = DependencyProperty.RegisterAttached("Reduced", typeof(bool), typeof(InteractionMotion), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, ReducedChanged));
    public static bool GetReduced(DependencyObject target) => (bool)target.GetValue(ReducedProperty);
    public static void SetReduced(DependencyObject target, bool value) => target.SetValue(ReducedProperty, value);
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(InteractionMotion), new PropertyMetadata(false, EnabledChanged));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static readonly DependencyProperty ToggleProperty = DependencyProperty.RegisterAttached("Toggle", typeof(bool), typeof(InteractionMotion), new PropertyMetadata(false, ToggleChanged));
    public static bool GetToggle(DependencyObject target) => (bool)target.GetValue(ToggleProperty);
    public static void SetToggle(DependencyObject target, bool value) => target.SetValue(ToggleProperty, value);

    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element) return;
        if ((bool)e.NewValue)
        {
            element.RenderTransformOrigin = new Point(.5, .5);
            element.RenderTransform = new ScaleTransform(1, 1);
            element.MouseEnter += Hover;
            element.MouseLeave += Leave;
            element.PreviewMouseLeftButtonDown += Press;
            element.PreviewMouseLeftButtonUp += Release;
            element.IsEnabledChanged += EnabledStateChanged;
        }
        else
        {
            element.MouseEnter -= Hover;
            element.MouseLeave -= Leave;
            element.PreviewMouseLeftButtonDown -= Press;
            element.PreviewMouseLeftButtonUp -= Release;
            element.IsEnabledChanged -= EnabledStateChanged;
            Scale(element, 1, false);
        }
    }
    private static void Hover(object sender, MouseEventArgs e) => Scale((FrameworkElement)sender, 1);
    private static void Leave(object sender, MouseEventArgs e) => Scale((FrameworkElement)sender, 1);
    private static void Press(object sender, MouseButtonEventArgs e)
    {
        var element = (FrameworkElement)sender;
        var point = e.GetPosition(element);
        element.RenderTransformOrigin = new Point(Math.Clamp(point.X / Math.Max(1, element.ActualWidth), 0, 1), Math.Clamp(point.Y / Math.Max(1, element.ActualHeight), 0, 1));
        Scale(element, .975);
    }
    private static void Release(object sender, MouseButtonEventArgs e) => Scale((FrameworkElement)sender, 1.018);
    private static void EnabledStateChanged(object sender, DependencyPropertyChangedEventArgs e) => Scale((FrameworkElement)sender, 1, false);
    private static void Scale(FrameworkElement element, double value, bool animate = true)
    {
        if (element.RenderTransform is not ScaleTransform scale) return;
        if (GetReduced(element) || !element.IsEnabled) { value = 1; animate = false; }
        var animation = new DoubleAnimation(scale.ScaleX, value, TimeSpan.FromMilliseconds(animate ? 140 : 0)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale.ScaleY, value, TimeSpan.FromMilliseconds(animate ? 140 : 0)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    private static void ToggleChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ToggleButton toggle) return;
        if ((bool)e.NewValue) { toggle.Checked += ToggleState; toggle.Unchecked += ToggleState; toggle.Loaded += ToggleLoaded; }
        else { toggle.Checked -= ToggleState; toggle.Unchecked -= ToggleState; toggle.Loaded -= ToggleLoaded; }
    }
    private static void ToggleState(object sender, RoutedEventArgs e) => MoveToggle((ToggleButton)sender, true);
    private static void ToggleLoaded(object sender, RoutedEventArgs e) => MoveToggle((ToggleButton)sender, false);
    private static void MoveToggle(ToggleButton toggle, bool animate)
    {
        if (toggle.Template?.FindName("Knob", toggle) is not FrameworkElement knob) return;
        if (knob.RenderTransform is not TranslateTransform) knob.RenderTransform = new TranslateTransform();
        ((TranslateTransform)knob.RenderTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(((TranslateTransform)knob.RenderTransform).X, toggle.IsChecked == true ? 21 : 0, TimeSpan.FromMilliseconds(animate && !GetReduced(toggle) ? 180 : 0)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    private static void ReducedChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is FrameworkElement element && GetEnabled(element)) Scale(element, 1, false);
        if (target is FrameworkElement placeholder && Skeleton.GetActive(placeholder)) Skeleton.Update(placeholder);
        if (target is ToggleButton toggle && GetToggle(toggle)) MoveToggle(toggle, false);
    }
}
