using OrbitClient.Scripts;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace OrbitClient;

public partial class MainWindow
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private void InitializePolish()
    {
        SourceInitialized += (_, _) => UpdateWindowCorners();
        StateChanged += (_, _) => UpdateWindowCorners();
        WindowRoot.SizeChanged += (_, _) => { ClipWindowContent(); CancelNewsTransition(); };
        SidebarNavigation.SizeChanged += (_, _) => FitSidebar();
        ProfileNav.SizeChanged += (_, _) => FitSidebar();
        Loaded += (_, _) => { FitSidebar(); AnimateActivePage(); };
    }

    private void ClipWindowContent()
    {
        var radius = WindowState == WindowState.Maximized ? 0 : 16;
        WindowRoot.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, WindowRoot.ActualWidth), Math.Max(0, WindowRoot.ActualHeight)), radius, radius);
    }

    private void UpdateWindowCorners()
    {
        ClipWindowContent();
        WindowChrome.GetWindowChrome(this).CornerRadius = new CornerRadius(WindowState == WindowState.Maximized ? 0 : 16);
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        // Native rounded corners retain hardware rendering, resizing, and the desktop shadow.
        var preference = WindowState == WindowState.Maximized ? 1 : 2;
        _ = DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
    }

    private void FitSidebar()
    {
        if (SidebarNavigation.ActualHeight <= 0) return;
        ProfileNav.Padding = new Thickness(preferences.ShowNavLabels ? 8 : 0, 7, preferences.ShowNavLabels ? 8 : 0, 7);
        var labels = NavPanel.Children.OfType<TextBlock>().Where(label => label.Visibility == Visibility.Visible).ToArray();
        for (var i = 0; i < labels.Length; i++) labels[i].Margin = new Thickness(12, i == 0 ? 0 : 12, 0, 5);
        var labelSpace = labels.Sum(label => label.FontSize * 1.5 + label.Margin.Top + label.Margin.Bottom);
        var buttons = NavPanel.Children.OfType<Button>().ToArray();
        var height = Math.Clamp((SidebarNavigation.ActualHeight - labelSpace - 10 - buttons.Length * 2 - 24) / Math.Max(1, buttons.Length), 34, preferences.Compact ? 34 : 36);
        foreach (var button in buttons)
        {
            button.Height = height;
            button.Margin = new Thickness(0, 1, 0, 1);
            button.Padding = new Thickness(8, 0, 8, 0);
        }
    }

    private void AnimateActivePage()
    {
        HeroContent.BeginAnimation(OpacityProperty, null);
        HeroContent.Opacity = 1;
        HeroTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        HeroImageScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        HeroImageScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        HeroTranslate.Y = 0;
        foreach (var page in new[] { HomePage, Accounts, SteamAccountsPage, Discover, News, SettingsPage, FeaturePage })
        {
            page.BeginAnimation(OpacityProperty, null);
            page.Opacity = 1;
            page.RenderTransform = new TranslateTransform();
            if (page.Visibility != Visibility.Visible || preferences.ReducedMotion) continue;
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

        }
    }
}
