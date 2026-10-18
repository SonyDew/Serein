using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Serein_Gaming_WinUI;

internal static class Ui
{
    public static double DensityScale { get; set; } = 1;
    public static Brush Brush(string key, ElementTheme theme)
    {
        var resources = Application.Current.Resources;
        if (key == "AccentBrush") return (Brush)resources[key];
        string dictionary = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast ? "HighContrast" : theme == ElementTheme.Light ? "Light" : "Dark";
        return (Brush)((ResourceDictionary)resources.ThemeDictionaries[dictionary])[key];
    }
    public static StackPanel Stack(double gap = 16) => new() { Spacing = gap * DensityScale };
    public static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 * DensityScale };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    public static TextBlock Text(string text, double size, Brush ink, bool bold = false) => new()
    { Text = text, FontSize = size, Foreground = ink, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
    public static Border Group(UIElement content, Brush surface, Brush line, Thickness? padding = null) => new()
    { Child = content, Background = surface, BorderBrush = line, BorderThickness = new(1), CornerRadius = new(18), Padding = padding ?? new Thickness(22) };
    public static Grid Columns(FrameworkElement left, FrameworkElement right, double leftWeight = 2, double rightWeight = 1)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = new(leftWeight, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new(rightWeight, GridUnitType.Star) });
        grid.Children.Add(left); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }
    public static FontIcon Icon(string glyph, double size = 22) => new() { Glyph = glyph, FontSize = size };
}
