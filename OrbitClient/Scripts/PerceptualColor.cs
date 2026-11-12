using System.Windows.Media;

namespace OrbitClient.Scripts;

public static class PerceptualColor
{
    public readonly record struct Oklch(double L, double C, double H);
    private static double Linear(byte channel) { var c = channel / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
    public static Oklch ToOklch(Color color)
    {
        var r = Linear(color.R); var g = Linear(color.G); var b = Linear(color.B);
        var l = Math.Cbrt(.4122214708 * r + .5363325363 * g + .0514459929 * b);
        var m = Math.Cbrt(.2119034982 * r + .6806995451 * g + .1073969566 * b);
        var s = Math.Cbrt(.0883024619 * r + .2817188376 * g + .6299787005 * b);
        var a = 1.9779984951 * l - 2.4285922050 * m + .4505937099 * s;
        var bb = .0259040371 * l + .7827717662 * m - .8086757660 * s;
        return new(.2104542553 * l + .7936177850 * m - .0040720468 * s, Math.Sqrt(a * a + bb * bb), Math.Atan2(bb, a));
    }
    public static Color FromOklch(Oklch color)
    {
        static (double R, double G, double B) Rgb(double lightness, double chroma, double hue)
        {
            var a = chroma * Math.Cos(hue); var b = chroma * Math.Sin(hue);
            var l = Math.Pow(lightness + .3963377774 * a + .2158037573 * b, 3);
            var m = Math.Pow(lightness - .1055613458 * a - .0638541728 * b, 3);
            var s = Math.Pow(lightness - .0894841775 * a - 1.2914855480 * b, 3);
            return (4.0767416621 * l - 3.3077115913 * m + .2309699292 * s, -1.2684380046 * l + 2.6097574011 * m - .3413193965 * s, -.0041960863 * l - .7034186147 * m + 1.7076147010 * s);
        }
        static bool InGamut((double R, double G, double B) rgb) => rgb.R is >= 0 and <= 1 && rgb.G is >= 0 and <= 1 && rgb.B is >= 0 and <= 1;
        var lightness = Math.Clamp(color.L, 0, 1); var chroma = Math.Max(0, color.C);
        var rgb = Rgb(lightness, chroma, color.H);
        if (!InGamut(rgb))
        {
            double low = 0, high = chroma;
            for (var i = 0; i < 24; i++) { var mid = (low + high) / 2; if (InGamut(Rgb(lightness, mid, color.H))) low = mid; else high = mid; }
            rgb = Rgb(lightness, low, color.H);
        }
        static byte Srgb(double value) => (byte)Math.Round(Math.Clamp(value <= .0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1 / 2.4) - .055, 0, 1) * 255);
        return Color.FromRgb(Srgb(rgb.R), Srgb(rgb.G), Srgb(rgb.B));
    }
    public static Color Foreground(Color background)
    {
        var luminance = .2126 * Linear(background.R) + .7152 * Linear(background.G) + .0722 * Linear(background.B);
        return luminance > .179 ? Colors.Black : Colors.White;
    }
}
