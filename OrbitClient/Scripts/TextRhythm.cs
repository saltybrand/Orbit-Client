using System.Globalization;
using System.Windows.Data;

namespace OrbitClient.Scripts;

public static class TextRhythm
{
    // Keep a short final phrase together; do not alter names, numbers, or article body copy.
    public static string Heading(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('\n') || text.Contains('\u00a0')) return text;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 4 || words[^1].Length + words[^2].Length > 22) return text;
        var lastSpace = text.LastIndexOf(' ');
        return text[..lastSpace] + '\u00a0' + text[(lastSpace + 1)..];
    }
}

public sealed class LeadingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double size ? size * double.Parse(parameter?.ToString() ?? "1.45", CultureInfo.InvariantCulture) : 20d;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
