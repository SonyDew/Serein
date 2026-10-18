using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serein.Core;

namespace Serein_Gaming_WinUI;

public sealed partial class MainWindow
{
    private readonly List<(Microsoft.UI.Xaml.Shapes.Ellipse Dot, TextBlock Label)> legendItems = [];
    private FrameworkElement LegendItem(string name, Brush color)
    {
        var item = new Grid { ColumnSpacing = 8, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        item.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        item.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        // The Latin capitals sit one pixel below the center of Segoe's line box.
        var dot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 10, Height = 10, Fill = color, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 2, 0, 0) };
        var label = Hint(name); label.VerticalAlignment = VerticalAlignment.Center;
        item.Children.Add(dot); Grid.SetColumn(label, 1); item.Children.Add(label);
        legendItems.Add((dot, label)); return item;
    }
    private Brush MetricBrush(int metric)
    {
        if (metric == 0) return ChartAccent;
        string original = metric == 1 ? "#6D9DE5" : "#8B94A8";
        return new SolidColorBrush(Color(AppearanceColors.VisibleOn(original, Hex(Surface), Hex(Ink))));
    }
    private HistoryChart CreateChart(int? metric = null, double height = 190)
    {
        var chart = new HistoryChart(ChartAccent, metric, height, appearance.ChartGrid, appearance.HistorySeconds, MetricBrush(1), MetricBrush(2));
        charts.Add(chart); return chart;
    }
    private FrameworkElement Settings()
    {
        var page = Ui.Stack(18);
        page.Children.Add(LanguageSettings());
        var styles = Ui.Stack(16);
        styles.Children.Add(Heading(UiStrings.Get("Text279"), UiStrings.Get("Text280")));
        styles.Children.Add(Ui.Row(
            Link(UiStrings.Get("Text187"), () => SetStyle(SurfaceTone.Graphite, "#AE9CF7", PanelShape.Soft)),
            Link(UiStrings.Get("Text281"), () => SetStyle(SurfaceTone.Midnight, "#80BFEA", PanelShape.Neat)),
            Link(UiStrings.Get("Text282"), () => SetStyle(SurfaceTone.Warm, "#E8B397", PanelShape.Round))));
        page.Children.Add(Group(styles));
        page.Children.Add(BackgroundSettings());

        var color = Ui.Stack(18); color.Children.Add(Text(UiStrings.Get("Text283"), 20, true));
        color.Children.Add(Ui.Columns(
            Choice(UiStrings.Get("Text284"), [UiStrings.Get("Text174"), UiStrings.Get("Text175"), UiStrings.Get("Text285")], appearance.FollowSystemTheme ? 2 : preferences.LightTheme ? 1 : 0, value =>
            {
                appearance = appearance with { FollowSystemTheme = value == 2 };
                preferences = preferences with { LightTheme = value == 1 }; SaveAppearance();
            }),
            Choice(UiStrings.Get("Text286"), [UiStrings.Get("Text287"), UiStrings.Get("Text288"), UiStrings.Get("Text282")], (int)appearance.Tone, value => { appearance = appearance with { Tone = (SurfaceTone)value }; SaveAppearance(); }), 1, 1));
        color.Children.Add(Hint(UiStrings.Get("Text289"), 13));
        var swatches = Ui.Row();
        foreach (var (name, hex) in new[] { (UiStrings.Get("Text290"), "#AE9CF7"), (UiStrings.Get("Text291"), "#80BFEA"), (UiStrings.Get("Text292"), "#87CEBA"), (UiStrings.Get("Text293"), "#E8B397") })
        {
            var button = Link(name, () => { preferences = preferences with { AccentColor = hex }; SaveAppearance(); });
            if (preferences.AccentColor.Equals(hex, StringComparison.OrdinalIgnoreCase)) { button.BorderBrush = ChartAccent; button.BorderThickness = new(1.5); }
            var swatch = new Border { Width = 10, Height = 10, CornerRadius = new(5), Background = new SolidColorBrush(Color(hex)), VerticalAlignment = VerticalAlignment.Center };
            button.Content = Ui.Row(swatch, Text(name, 12)); swatches.Children.Add(button);
        }
        color.Children.Add(swatches);
        var picker = new ColorPicker { Color = Color(preferences.AccentColor), IsAlphaEnabled = false, IsMoreButtonVisible = false, IsColorChannelTextInputVisible = false, IsHexInputVisible = true, Width = 290 };
        var pickerBody = Ui.Stack(12); pickerBody.Children.Add(picker);
        var flyout = new Flyout { Content = pickerBody };
        pickerBody.Children.Add(Button(UiStrings.Get("Text294"), () =>
        {
            var c = picker.Color; preferences = preferences with { AccentColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}" };
            flyout.Hide(); SaveAppearance(); return Task.CompletedTask;
        }, true));
        var custom = new Button { Content = UiStrings.Get("Text295"), Flyout = flyout, FontSize = 14 * appearance.TextScale, CornerRadius = new(ControlRadius) };
        color.Children.Add(custom); page.Children.Add(Group(color));

        var layout = Ui.Stack(18); layout.Children.Add(Text(UiStrings.Get("Text296"), 20, true));
        layout.Children.Add(Ui.Columns(
            Choice(UiStrings.Get("Text297"), [UiStrings.Get("Text298"), UiStrings.Get("Text299"), UiStrings.Get("Text300")], (int)appearance.Density, value => { appearance = appearance with { Density = (LayoutDensity)value }; SaveAppearance(); }),
            Choice(UiStrings.Get("Text301"), [UiStrings.Get("Text302"), UiStrings.Get("Text303"), UiStrings.Get("Text304")], (int)appearance.Shape, value => { appearance = appearance with { Shape = (PanelShape)value }; SaveAppearance(); }), 1, 1));
        layout.Children.Add(Choice(UiStrings.Get("Text305"), [UiStrings.Get("Text015"), UiStrings.Get("Text306"), UiStrings.Get("Text307")], (int)Math.Round((appearance.TextScale - 1) * 10), value => { appearance = appearance with { TextScale = 1 + value / 10d }; SaveAppearance(); }));
        layout.Children.Add(Switch(UiStrings.Get("Text308"), appearance.CompactNavigation, value => { appearance = appearance with { CompactNavigation = value }; SaveAppearance(); }));
        layout.Children.Add(Switch(UiStrings.Get("Text309"), appearance.PanelOutlines, value => { appearance = appearance with { PanelOutlines = value }; SaveAppearance(); }));
        page.Children.Add(Group(layout));

        var home = Ui.Stack(16); home.Children.Add(Heading(UiStrings.Get("Text012"), UiStrings.Get("Text310")));
        home.Children.Add(Ui.Columns(
            Check(UiStrings.Get("Text001"), appearance.ShowSession, value => appearance with { ShowSession = value }),
            Check(UiStrings.Get("Text311"), appearance.ShowResources, value => appearance with { ShowResources = value }), 1, 1));
        home.Children.Add(Ui.Columns(
            Check(UiStrings.Get("Text312"), appearance.ShowActivity, value => appearance with { ShowActivity = value }),
            Check(UiStrings.Get("Text025"), appearance.ShowHardware, value => appearance with { ShowHardware = value }), 1, 1));
        home.Children.Add(Check(UiStrings.Get("Text009"), appearance.ShowBackgroundApps, value => appearance with { ShowBackgroundApps = value }));
        page.Children.Add(Group(home));

        var charts = Ui.Stack(18); charts.Children.Add(Text(UiStrings.Get("Text313"), 20, true));
        charts.Children.Add(Choice(UiStrings.Get("Text314"), [UiStrings.Get("Text315"), UiStrings.Get("Text316"), UiStrings.Get("Text317")], appearance.HistorySeconds == 30 ? 0 : appearance.HistorySeconds == 120 ? 2 : 1,
            value => { appearance = appearance with { HistorySeconds = new[] { 30, 60, 120 }[value] }; SaveAppearance(); }));
        charts.Children.Add(Switch(UiStrings.Get("Text318"), appearance.ChartGrid, value => { appearance = appearance with { ChartGrid = value }; SaveAppearance(); }));
        charts.Children.Add(Switch(UiStrings.Get("Text319"), preferences.ReducedMotion, value => { preferences = preferences with { ReducedMotion = value }; SaveAppearance(); }));
        page.Children.Add(Group(charts));
        page.Children.Add(Hint(UiStrings.Get("Text320")));
        page.Children.Add(Link(UiStrings.Get("Text321"), () =>
        {
            appearance = new(); preferences = preferences with { LightTheme = false, AccentColor = "#AE9CF7", ReducedMotion = false }; SaveAppearance();
        }));
        return page;
    }
    private void SetStyle(SurfaceTone tone, string accent, PanelShape shape)
    {
        appearance = appearance with { Tone = tone, Shape = shape, FollowSystemTheme = tone == SurfaceTone.Midnight ? false : appearance.FollowSystemTheme };
        preferences = preferences with { AccentColor = accent, LightTheme = tone == SurfaceTone.Midnight ? false : preferences.LightTheme }; SaveAppearance();
    }
    private StackPanel Choice(string name, string[] values, int selected, Action<int> changed)
    {
        var body = Ui.Stack(8); body.Children.Add(Hint(name, 13));
        var choice = new ComboBox { ItemsSource = values, SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 14 * appearance.TextScale };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choice, name);
        choice.SelectionChanged += (_, _) => { if (choice.SelectedIndex >= 0) changed(choice.SelectedIndex); };
        body.Children.Add(choice); return body;
    }
    private ToggleSwitch Switch(string name, bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { Header = name, IsOn = value, OnContent = UiStrings.Get("Text322"), OffContent = UiStrings.Get("Text323"), FontSize = 14 * appearance.TextScale };
        foreach (string state in new[] { "On", "OnPointerOver", "OnPressed" })
        {
            toggle.Resources["ToggleSwitchFill" + state] = Accent;
            toggle.Resources["ToggleSwitchKnobFill" + state] = AccentText;
        }
        toggle.Toggled += (_, _) => changed(toggle.IsOn); return toggle;
    }
    private CheckBox Check(string name, bool value, Func<bool, AppearanceOptions> changed)
    {
        var check = new CheckBox { Content = name, IsChecked = value, FontSize = 14 * appearance.TextScale };
        foreach (string state in new[] { "Checked", "CheckedPointerOver", "CheckedPressed" })
        {
            check.Resources["CheckBoxCheckBackgroundFill" + state] = Accent;
            check.Resources["CheckBoxCheckBackgroundStroke" + state] = Accent;
            check.Resources["CheckBoxCheckGlyphForeground" + state] = AccentText;
        }
        check.Checked += (_, _) => { appearance = changed(true); SaveAppearance(); };
        check.Unchecked += (_, _) => { appearance = changed(false); SaveAppearance(); }; return check;
    }
}
