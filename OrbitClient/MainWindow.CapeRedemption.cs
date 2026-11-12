using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;

namespace OrbitClient;

public partial class MainWindow
{
    private void BuildCapeRedemption()
    {
        var content = new StackPanel();
        var formOwner = orbitAccount?.Account?.Id;
        content.Children.Add(FeatureText("One key. One cape. Yours.", 28));
        content.Children.Add(FeatureText("Paste the cape key you received. Once a player redeems a key, nobody can use that key again.", 15, true));
        content.Children.Add(FeatureText("Cape key", 13));
        var input = new PasswordBox
        {
            FontSize = 16, Padding = new Thickness(10, 8, 10, 8), MaxLength = 256,
            Background = (Brush)FindResource("ControlSurface"), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(62, 47, 74)), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 2, 0, 10), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 300
        };
        System.Windows.Automation.AutomationProperties.SetName(input, "Cape redemption key");
        content.Children.Add(input);
        var button = FeatureButton("Redeem cape", async () =>
        {
            if (!capeService.SignedIn) { Nav_Click(ProfileNav, new RoutedEventArgs()); return; }
            if (formOwner != orbitAccount?.Account?.Id) throw new IOException("Your account changed. Reopen Redeem key and paste the key again.");
            var id = await capeService.RedeemAsync(input.Password);
            input.Clear(); capeTab = 0; capeFocusedId = id;
            capeStatus = "Cape redeemed and added to your dressing room. This key is now used.";
            OpenFeaturePage("Capes");
        }, true);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(button);
        content.Children.Add(FeatureText("Keys are checked for your signed-in Orbit account. Already own the cape? The key stays unused so you can pass it on.", 12, true));
        FeatureContent.Children.Add(FeatureCard(content));
    }
}
