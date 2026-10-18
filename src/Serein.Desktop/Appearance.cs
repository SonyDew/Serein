using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Serein.Core;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Serein.Desktop;

public partial class MainWindow
{
    private void ApplyAppearance()
    {
        preferences.Validate();
        // WPF's runtime ThemeMode setter is experimental in the pinned .NET 10 API;
        // isolated here. All data/engine operations remain independent of theme APIs.
#pragma warning disable WPF0001
        System.Windows.Application.Current.ThemeMode = light ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
        var resources = System.Windows.Application.Current.Resources;
        string[] keys = ["CanvasBrush", "SidebarBrush", "CardBrush", "SurfaceBrush", "ElevatedBrush", "TextBrush", "MutedBrush", "LineBrush", "SuccessBrush", "ChartSecondaryBrush"];
        string[] colors = light
            ? ["#F3F5FA", "#EAEEF6", "#FFFFFF", "#EFF2F8", "#E2E8F3", "#172239", "#59677F", "#DCE2ED", "#187558", "#687D99"]
            : ["#0E1119", "#10131C", "#161B27", "#1B2130", "#222A3B", "#F1F4FC", "#9AA6BC", "#293144", "#72D4AF", "#9FAFC3"];
        for (int i = 0; i < keys.Length; i++) resources[keys[i]] = Brush(colors[i]);
        resources["TraySurfaceBrush"] = Brush(light ? "#FCFCFE" : "#202127");
        resources["TrayHoverBrush"] = Brush(light ? "#EDF0F6" : "#2D2F39");
        resources["TrayLineBrush"] = Brush(light ? "#E1E4EC" : "#353741");
        var accent = (Color)ColorConverter.ConvertFromString(preferences.AccentColor);
        var ink = Contrast(accent, Colors.White) >= Contrast(accent, Colors.Black) ? Colors.White : Colors.Black;
        var backdrop = ((SolidColorBrush)resources["CanvasBrush"]).Color;
        Color label = accent;
        for (int i = 0; i < 24 && Contrast(label, backdrop) < 4.5; i++) label = Mix(label, light ? Colors.Black : Colors.White, .12);
        resources["AccentBrush"] = new SolidColorBrush(accent); resources["AccentInkBrush"] = new SolidColorBrush(ink);
        resources["AccentTextBrush"] = new SolidColorBrush(label); resources["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(light ? (byte)25 : (byte)35, label.R, label.G, label.B));
        var trayBackdrop = ((SolidColorBrush)resources["TraySurfaceBrush"]).Color;
        var trayHover = ((SolidColorBrush)resources["TrayHoverBrush"]).Color;
        Color trayLabel = accent;
        for (int i = 0; i < 32 && (Contrast(trayLabel, Mix(trayBackdrop, trayLabel, (light ? 20d : 28d) / 255)) < 4.5 || Contrast(trayLabel, trayHover) < 4.5); i++) trayLabel = Mix(trayLabel, light ? Colors.Black : Colors.White, .12);
        resources["TrayAccentTextBrush"] = new SolidColorBrush(trayLabel);
        resources["TrayAccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(light ? (byte)20 : (byte)28, trayLabel.R, trayLabel.G, trayLabel.B));
        resources[SystemColors.AccentColorBrushKey] = new SolidColorBrush(label);
        if (SystemParameters.HighContrast)
        {
            foreach (string key in new[] { "CanvasBrush", "SidebarBrush", "CardBrush", "SurfaceBrush", "ElevatedBrush" }) resources[key] = SystemColors.WindowBrush;
            foreach (string key in new[] { "TextBrush", "MutedBrush", "ChartSecondaryBrush", "LineBrush" }) resources[key] = SystemColors.WindowTextBrush;
            resources["AccentBrush"] = SystemColors.HighlightBrush; resources["AccentSoftBrush"] = SystemColors.HighlightBrush; resources["AccentTextBrush"] = SystemColors.WindowTextBrush; resources["AccentInkBrush"] = SystemColors.HighlightTextBrush;
            resources["TraySurfaceBrush"] = SystemColors.MenuBrush; resources["TrayHoverBrush"] = SystemColors.HighlightBrush; resources["TrayLineBrush"] = SystemColors.MenuTextBrush;
            resources["TrayAccentTextBrush"] = SystemColors.MenuTextBrush; resources["TrayAccentSoftBrush"] = SystemColors.MenuBrush;
        }
        chart?.InvalidateVisual();
        // Closed popups are detached from the application's resource notification tree.
        // Rebuild their lightweight visuals so the next open uses the new palette.
        if (trayMenu is not null) BuildTray();
    }
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static Color Mix(Color a, Color b, double amount) => Color.FromRgb((byte)(a.R + (b.R - a.R) * amount), (byte)(a.G + (b.G - a.G) * amount), (byte)(a.B + (b.B - a.B) * amount));
    private static double Contrast(Color a, Color b)
    {
        static double L(Color c) { static double C(byte v) { double x = v / 255d; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); } return .2126 * C(c.R) + .7152 * C(c.G) + .0722 * C(c.B); }
        double first = L(a), second = L(b); return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }
    private void ToggleTheme() { light = !light; ApplyAppearance(); SavePreferences(); ShowPage(page); }
    private void SetAccent(string value)
    {
        var next = preferences with { AccentColor = value.Trim().ToUpperInvariant() }; next.Validate(); preferences = next;
        ApplyAppearance(); SavePreferences(); ShowPage(page);
    }
    private void ThemeClick(object sender, RoutedEventArgs e) => ToggleTheme();
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void FitWorkingArea()
    {
        var work = SystemParameters.WorkArea;
        double scale = Math.Min(1, Math.Min(Math.Max(1, work.Width - 32) / 1240, Math.Max(1, work.Height - 32) / 820));
        Width = 1240 * scale; Height = 820 * scale;
        System.Windows.Shell.WindowChrome.GetWindowChrome(this).CaptionHeight = 46 * scale;
        Left = work.Left + (work.Width - Width) / 2; Top = work.Top + (work.Height - Height) / 2;
    }
    private void ConfigureChrome()
    {
        FitWorkingArea();
        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) ?? throw new InvalidOperationException("Window message source unavailable.");
            source.AddHook((nint window, int message, nint wParam, nint lParam, ref bool handled) =>
            {
                if (message == 0x112 && ((wParam.ToInt64() & 0xFFF0) is 0xF030 or 0xF000)) handled = true; // SC_MAXIMIZE / SC_SIZE
                return 0;
            });
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) { int round = 2; DwmSetWindowAttribute(source.Handle, 33, ref round, 4); }
        };
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, uint attribute, ref int value, uint size);
    private FrameworkElement Settings()
    {
        var work = new StackPanel();
        var appearance = new StackPanel(); appearance.Children.Add(SectionTitle(T("Внешний вид", "Appearance"), T("Цвет, тема и движение. Настройки применяются только к Serein.", "Color, theme and motion. These settings affect Serein only.")));
        var themes = Row();
        var dark = Action(T("Тёмная", "Dark"), () => { if (light) ToggleTheme(); }); if (!light) dark.Style = (Style)FindResource("PrimaryButtonStyle"); themes.Children.Add(dark);
        var bright = Action(T("Светлая", "Light"), () => { if (!light) ToggleTheme(); }); if (light) bright.Style = (Style)FindResource("PrimaryButtonStyle"); themes.Children.Add(bright); appearance.Children.Add(themes);
        appearance.Children.Add(Muted(T("Акцентный цвет", "Accent color"), 12, 10));
        var swatches = Row();
        foreach (var (name, hex) in new[] { (T("Лавандовый", "Lavender"), "#8B9FFF"), (T("Синий", "Blue"), "#70B6FF"), (T("Мятный", "Mint"), "#6ED4B4"), (T("Фиолетовый", "Violet"), "#BA9BFF"), (T("Розовый", "Rose"), "#F29BC1"), (T("Янтарный", "Amber"), "#F0BF70") })
        {
            var swatch = new Button { Content = new Border { Width = 22, Height = 22, CornerRadius = new(11), Background = Brush(hex) }, ToolTip = name, Width = 48, Height = 44, Padding = new(8), BorderThickness = new(preferences.AccentColor.Equals(hex, StringComparison.OrdinalIgnoreCase) ? 2 : 1) };
            swatch.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, name + " " + hex);
            if (preferences.AccentColor.Equals(hex, StringComparison.OrdinalIgnoreCase)) swatch.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
            swatch.Click += (_, _) => SetAccent(hex); swatches.Children.Add(swatch);
        }
        appearance.Children.Add(swatches);
        var custom = Row(); var hexInput = new TextBox { Text = preferences.AccentColor, MaxLength = 7, Width = 116, ToolTip = T("Код цвета, например #8B9FFF", "Color code, e.g. #8B9FFF") }; custom.Children.Add(hexInput); custom.Children.Add(Action(T("Выбрать цвет", "Use color"), () => SetAccent(hexInput.Text))); appearance.Children.Add(custom);
        var motion = new CheckBox { Content = T("Уменьшить анимации", "Reduce motion"), IsChecked = preferences.ReducedMotion };
        motion.Click += (_, _) => { preferences = preferences with { ReducedMotion = motion.IsChecked == true }; SavePreferences(); }; appearance.Children.Add(motion);
        work.Children.Add(Card(appearance, new(0, 0, 0, 16)));
        var behavior = new StackPanel(); behavior.Children.Add(SectionTitle(T("Поведение приложения", "Application behavior")));
        var closeToTray = new CheckBox { Content = T("При закрытии оставлять приложение в трее", "Keep the application in tray when closing"), IsChecked = preferences.CloseToTray };
        closeToTray.Click += (_, _) => { preferences = preferences with { CloseToTray = closeToTray.IsChecked == true }; SavePreferences(); }; behavior.Children.Add(closeToTray);
        var notifications = new CheckBox { Content = T("Уведомлять о сессиях и ошибках", "Notify about sessions and errors"), IsChecked = preferences.Notifications };
        notifications.Click += (_, _) => { preferences = preferences with { Notifications = notifications.IsChecked == true }; SavePreferences(); }; behavior.Children.Add(notifications);
        var autostart = new CheckBox { Content = T("Запускать в трее при входе в Windows", "Start in tray at Windows sign-in"), IsChecked = startup.Read().Exists(s => s.Name == "Serein" && s.Enabled) };
        autostart.Click += (_, _) => { try { startup.SetAppStartup(autostart.IsChecked == true, Environment.ProcessPath!); Report(T("Автозагрузка изменена.", "Startup changed.")); } catch (Exception ex) { Report(ex.Message); } }; behavior.Children.Add(autostart);
        behavior.Children.Add(Action("Русский / English", () => { english = !english; SavePreferences(); BuildTray(); BuildNavigation(); ShowPage("Settings"); })); work.Children.Add(Card(behavior, new(0, 0, 0, 16)));
        var recovery = new StackPanel(); recovery.Children.Add(SectionTitle(T("Журнал и восстановление", "Logs and recovery"), T("Подтверждённые изменения восстанавливаются автоматически. Неоднозначные записи требуют проверки.", "Confirmed changes recover automatically. Ambiguous records require review.")));
        var actions = Row(); actions.Children.Add(Action(T("Открыть журналы", "Open logs"), () => Process.Start(new ProcessStartInfo(data) { UseShellExecute = true }))); actions.Children.Add(Action(T("Проверить восстановление", "Check recovery"), async () => { foreach (string result in await System.Threading.Tasks.Task.Run(() => Recovery.Reconcile(Path.Combine(data, "sessions")))) Report(result); })); actions.Children.Add(Action(T("Выход и восстановление", "Exit and restore"), ExitApp)); recovery.Children.Add(actions); work.Children.Add(Card(recovery));
        return Scroll(work);
    }
    private TextBlock Muted(string text, double size = 12, double bottom = 8) { var label = Text(text, size, bottom); label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); return label; }
    private StackPanel SectionTitle(string title, string? description = null)
    {
        var content = new StackPanel(); var heading = Text(title, 17, description is null ? 16 : 6); heading.FontWeight = FontWeights.SemiBold; heading.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); content.Children.Add(heading);
        if (description is not null) content.Children.Add(Muted(description, 12, 16)); return content;
    }
    private Border Card(UIElement content, Thickness? margin = null) => new() { Style = (Style)FindResource("CardStyle"), Child = content, Margin = margin ?? new Thickness(0) };
    private static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 2, 0) };
}
