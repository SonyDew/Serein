using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serein.Core;

namespace Serein_Gaming_WinUI;

public sealed partial class MainWindow
{
    private double ControlRadius => Math.Clamp(appearance.Radius * .55, 4, 14);
    private Brush AccentText => new SolidColorBrush(Color(AppearanceColors.TextOn(preferences.AccentColor)));
    private Brush ChartAccent => new SolidColorBrush(Color(AppearanceColors.VisibleOn(preferences.AccentColor, Hex(Surface), Hex(Ink))));
    private static Windows.UI.Color Color(string hex) => Windows.UI.Color.FromArgb(255, Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
    private static string Hex(Brush brush)
    { var c = ((SolidColorBrush)brush).Color; return $"#{c.R:X2}{c.G:X2}{c.B:X2}"; }
    private bool UseLightTheme
    {
        get
        {
            if (!appearance.FollowSystemTheme) return preferences.LightTheme;
            var color = systemTheme.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            return color.R + color.G + color.B > 384;
        }
    }
    private void SystemThemeChanged(Windows.UI.ViewManagement.UISettings sender, object args)
    {
        if (!appearance.FollowSystemTheme) return;
        DispatcherQueue.TryEnqueue(() => { if (!closing) { ApplyAppearance(); ShowPage(currentPage, true); } });
    }
    private void ApplyAppearance()
    {
        appearance.Validate();
        lastHighContrast = accessibility.HighContrast;
        Ui.DensityScale = appearance.Spacing;
        SetPalette();
        Root.RequestedTheme = UseLightTheme ? ElementTheme.Light : ElementTheme.Dark;
        var color = Color(preferences.AccentColor);
        ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color = color;
        Application.Current.Resources["SystemAccentColor"] = color;
        foreach (string key in new[] { "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush" }) Root.Resources[key] = Accent;
        Root.Resources["TextOnAccentFillColorPrimaryBrush"] = AccentText;
        Root.Resources["ControlCornerRadius"] = new CornerRadius(ControlRadius);
        foreach (var (key, opacity) in new[] { ("ListViewItemBackgroundSelected", .15), ("ListViewItemBackgroundSelectedPointerOver", .22), ("ListViewItemBackgroundSelectedPressed", .3) })
            Navigation.Resources[key] = new SolidColorBrush(((SolidColorBrush)ChartAccent).Color) { Opacity = opacity };
        var transparent = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        foreach (string key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" }) ThemeButton.Resources[key] = transparent;
        ThemeSun.Visibility = UseLightTheme ? Visibility.Collapsed : Visibility.Visible;
        ThemeMoon.Visibility = UseLightTheme ? Visibility.Visible : Visibility.Collapsed;
        SetThemeIconColor(Ink);
        PageTitle.FontSize = 30 * appearance.TextScale;
        RestoreButton.FontSize = 14 * appearance.TextScale;
        SidebarColumn.Width = new(appearance.CompactNavigation ? 76 : 210 + (appearance.TextScale - 1) * 160);
        Wordmark.Visibility = appearance.CompactNavigation ? Visibility.Collapsed : Visibility.Visible;
        foreach (ListViewItem item in Navigation.Items)
        {
            var row = (StackPanel)item.Content;
            var label = row.Children.OfType<TextBlock>().Single();
            label.FontSize = 14 * appearance.TextScale;
            label.Visibility = appearance.CompactNavigation ? Visibility.Collapsed : Visibility.Visible;
            row.Spacing = appearance.CompactNavigation ? 0 : 15;
            item.Padding = new(appearance.CompactNavigation ? 10 : 12, 14 * appearance.Spacing, appearance.CompactNavigation ? 10 : 12, 14 * appearance.Spacing);
            item.CornerRadius = new(ControlRadius);
            ToolTipService.SetToolTip(item, label.Text);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, label.Text);
        }
        foreach (var (key, size) in new[] { ("BodyTextSize", 14d), ("CaptionTextSize", 12d), ("SmallTextSize", 11d) }) Application.Current.Resources[key] = size * appearance.TextScale;
        var title = AppWindow.TitleBar;
        title.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        title.ButtonInactiveBackgroundColor = title.ButtonBackgroundColor;
        title.ButtonForegroundColor = ((SolidColorBrush)Ink).Color;
        title.ButtonInactiveForegroundColor = title.ButtonForegroundColor;
        ApplyBackdrop();
    }
    private void SetPalette()
    {
        var palettes = appearance.Tone switch
        {
            SurfaceTone.Midnight => new[] { new[] { "#0B1019", "#111925", "#151F2D", "#29384B", "#ECF2FF", "#9CACC4" }, new[] { "#F0F5FA", "#E6EDF5", "#FFFFFF", "#D2DDEB", "#172335", "#53677D" } },
            SurfaceTone.Warm => new[] { new[] { "#151311", "#1B1815", "#211E1A", "#39312B", "#F3EDE4", "#B3A79B" }, new[] { "#F8F4EE", "#EEE7DD", "#FFFCF7", "#DED4C7", "#2B251E", "#6C5E50" } },
            _ => new[] { new[] { "#101114", "#17191E", "#191C22", "#292D36", "#EEEFF7", "#A3A9BA" }, new[] { "#F3F4F8", "#EAECF2", "#FFFFFF", "#D8DBE5", "#1A1D2B", "#596174" } }
        };
        string[] keys = ["CanvasBrush", "RailBrush", "SurfaceBrush", "LineBrush", "InkBrush", "MutedBrush"];
        for (int theme = 0; theme < 2; theme++)
        {
            var resources = (ResourceDictionary)Application.Current.Resources.ThemeDictionaries[theme == 0 ? "Dark" : "Light"];
            for (int i = 0; i < keys.Length; i++) ((SolidColorBrush)resources[keys[i]]).Color = Color(palettes[theme][i]);
        }
    }
    private void SetThemeIconColor(Brush color)
    { SunCenter.Stroke = color; SunRays.Stroke = color; ThemeMoon.Stroke = color; }
    private void SaveAppearance()
    {
        preferences.Validate(); appearance.Validate();
        JsonFiles.Write(Path.Combine(dataDirectory, "appearance.json"), preferences);
        JsonFiles.Write(Path.Combine(dataDirectory, "layout.json"), appearance);
        ApplyAppearance(); ShowPage(currentPage, true);
    }
    private void ThemeClicked(object sender, RoutedEventArgs e)
    {
        bool light = !UseLightTheme;
        appearance = appearance with { FollowSystemTheme = false };
        preferences = preferences with { LightTheme = light };
        SaveAppearance();
    }
}
