using System.IO;

namespace Serein_Gaming_WinUI;

internal enum SurfaceTone { Graphite, Midnight, Warm }
internal enum LayoutDensity { Compact, Comfortable, Airy }
internal enum PanelShape { Neat, Soft, Round }
internal enum SurfaceMaterial { Solid, LiquidGlass }
internal enum BackgroundKind { Patterns, Collection, Custom }
internal enum BackgroundPalette { Automatic, Dark, Light }
internal enum BackgroundPattern { Aurora, Silk, Orbits, Contours, Grid, Waves, Prism, Bubbles, Dunes, Starlight, Circuit, Hexagons, LightTrails, Particles }
internal enum PatternGlow { Off, Soft, Neon }
internal enum GlowPalette { Violet, Cyan, Ruby, Spectrum }
internal sealed record AppearanceOptions(
    int SchemaVersion = 1, SurfaceTone Tone = SurfaceTone.Graphite,
    LayoutDensity Density = LayoutDensity.Comfortable, PanelShape Shape = PanelShape.Soft,
    double TextScale = 1, bool CompactNavigation = false, bool FollowSystemTheme = false,
    bool ShowSession = true, bool ShowResources = true, bool ShowActivity = true,
    bool ShowHardware = true, bool ShowBackgroundApps = true,
    int HistorySeconds = 60, bool ChartGrid = true, bool PanelOutlines = true,
    SurfaceMaterial Material = SurfaceMaterial.Solid, BackgroundKind Background = BackgroundKind.Patterns,
    BackgroundPalette BackgroundPalette = BackgroundPalette.Automatic, BackgroundPattern Pattern = BackgroundPattern.Aurora,
    int CollectionIndex = 0, double GlassTint = .65, double BackgroundStrength = .65,
    PatternGlow Glow = PatternGlow.Neon, GlowPalette GlowPalette = GlowPalette.Violet, bool PatternMotion = false)
{
    public double Spacing => Density switch { LayoutDensity.Compact => .85, LayoutDensity.Airy => 1.12, _ => 1 };
    public double Radius => Shape switch { PanelShape.Neat => 8, PanelShape.Round => 26, _ => 18 };
    public bool ShouldAnimate(bool reducedMotion, bool systemMotion, bool active, bool visible, bool playing) =>
        Material == SurfaceMaterial.LiquidGlass && Background == BackgroundKind.Patterns && PatternMotion && !reducedMotion && systemMotion && active && visible && !playing;
    public void Validate()
    {
        if (SchemaVersion != 1 || !Enum.IsDefined(Tone) || !Enum.IsDefined(Density) || !Enum.IsDefined(Shape)
            || !double.IsFinite(TextScale) || TextScale is < 1 or > 1.2 || HistorySeconds is not (30 or 60 or 120)
            || !Enum.IsDefined(Material) || !Enum.IsDefined(Background) || !Enum.IsDefined(BackgroundPalette) || !Enum.IsDefined(Pattern)
            || CollectionIndex is < 0 or > 4 || !double.IsFinite(GlassTint) || GlassTint is < .4 or > .9
            || !double.IsFinite(BackgroundStrength) || BackgroundStrength is < .2 or > 1)
            throw new InvalidDataException("Invalid appearance options.");
        if (!Enum.IsDefined(Glow) || !Enum.IsDefined(GlowPalette)) throw new InvalidDataException("Invalid pattern effects.");
    }
}

internal static class AppearanceColors
{
    public static string TextOn(string hex) => Contrast(hex, "#000000") >= Contrast(hex, "#FFFFFF") ? "#000000" : "#FFFFFF";
    public static double Contrast(string a, string b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
    public static string VisibleOn(string hex, string background, string foreground)
    {
        var original = Rgb(hex); var target = Rgb(foreground);
        for (int step = 0; step <= 20; step++)
        {
            double mix = step / 20d;
            string result = $"#{(int)Math.Round(original.R + (target.R - original.R) * mix):X2}{(int)Math.Round(original.G + (target.G - original.G) * mix):X2}{(int)Math.Round(original.B + (target.B - original.B) * mix):X2}";
            if (Contrast(result, background) >= 3) return result;
        }
        return foreground;
    }
    private static (byte R, byte G, byte B) Rgb(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#' || !hex.AsSpan(1).ToString().All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid color.");
        return (Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
    }
    private static double Luminance(string hex)
    {
        var c = Rgb(hex);
        static double Linear(byte b) { double n = b / 255d; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
        return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
    }
}
