using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml.Linq;

namespace OrbitClient.Scripts;

internal static class MaterialSymbols
{
    private static readonly Dictionary<string, Geometry> Cache = new();

    public static FrameworkElement Icon(string name, double size = 20)
    {
        if (!Cache.TryGetValue(name, out var geometry))
        {
            var uri = new Uri($"pack://application:,,,/OrbitClient;component/Assets/Icons/{name}.svg");
            using var stream = Application.GetResourceStream(uri)!.Stream;
            var svg = XDocument.Load(stream);
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };
            foreach (var path in svg.Descendants().Where(x => x.Name.LocalName == "path"))
                group.Children.Add(Geometry.Parse((string?)path.Attribute("d") ?? ""));
            group.Freeze();
            Cache[name] = geometry = group;
        }
        var icon = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        icon.SetBinding(System.Windows.Shapes.Shape.FillProperty, new Binding("Foreground")
        { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1), FallbackValue = Brushes.White });
        return icon;
    }

    public static void Label(Button button, string text, string icon, bool showLabel = true)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Icon(icon, 18));
        if (showLabel) row.Children.Add(new TextBlock { Text = text, Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        button.Content = row;
        button.ToolTip = text;
        System.Windows.Automation.AutomationProperties.SetName(button, text);
    }
}
