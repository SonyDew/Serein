using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Serein.Core;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace Serein_Gaming_WinUI;

public sealed partial class MainWindow : Window
{
    private readonly string dataDirectory;
    private readonly string? smokeDirectory;
    private readonly OptimizerSession session;
    private readonly TelemetrySampler sampler = new();
    private readonly ProcessInventory inventory = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly List<HistoryPoint> history = [];
    private readonly List<HistoryChart> charts = [];
    private readonly List<Action> liveUpdates = [];
    private readonly GpuTelemetryClient gpu;
    private CpuTelemetryClient cpu;
    private Preferences preferences;
    private AppearanceOptions appearance = new();
    private readonly Windows.UI.ViewManagement.UISettings systemTheme = new();
    private readonly Windows.UI.ViewManagement.AccessibilitySettings accessibility = new();
    private MetricSample? sample;
    private IReadOnlyList<ProcessRow> processes = [];
    private List<DeviceRow>? devices;
    private string cpuName = UiStrings.Get("Text196"), gpuName = UiStrings.Get("Text196"), osName = "Windows";
    private string currentPage = "Overview";
    private bool polling, closing, allowClose, busy, lastHighContrast;
    private int ticks;
    private ListView? processList;
    private string processSearch = "";
    private string processSort = "Memory";
    private TextBlock? processCount;
    private ContentDialog? activeDialog;
    private Brush Ink => Ui.Brush("InkBrush", Root.RequestedTheme);
    private Brush Muted => Ui.Brush("MutedBrush", Root.RequestedTheme);
    private Brush Surface => Ui.Brush("SurfaceBrush", Root.RequestedTheme);
    private Brush Line => Ui.Brush("LineBrush", Root.RequestedTheme);
    private Brush Accent => Ui.Brush("AccentBrush", Root.RequestedTheme);
    private string MeasurePath => Path.Combine(AppContext.BaseDirectory, "Serein.Measure.exe");
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);

    public MainWindow()
    {
        InitializeComponent();
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "--smoke");
        smokeDirectory = at >= 0 && at + 1 < args.Length ? Path.GetFullPath(args[at + 1]) : null;
        dataDirectory = smokeDirectory is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Serein.Gaming.WinUI")
            : Path.Combine(smokeDirectory, "state");
        Directory.CreateDirectory(dataDirectory);
        UiStrings.SetLanguage("en");
        string languagePath = Path.Combine(dataDirectory, "language.json");
        if (File.Exists(languagePath))
        {
            try { var languageOptions = JsonFiles.Read<LanguageOptions>(languagePath); languageOptions.Validate(); UiStrings.SetLanguage(languageOptions.Language); }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException) { new AuditLog(dataDirectory).Write("Language preference ignored: " + ex.Message); }
        }
        preferences = File.Exists(Path.Combine(dataDirectory, "appearance.json")) ? JsonFiles.Read<Preferences>(Path.Combine(dataDirectory, "appearance.json")) : new(AccentColor: "#AE9CF7");
        preferences.Validate();
        if (File.Exists(Path.Combine(dataDirectory, "layout.json"))) appearance = JsonFiles.Read<AppearanceOptions>(Path.Combine(dataDirectory, "layout.json"));
        appearance.Validate();
        session = new(dataDirectory);
        gpu = new(MeasurePath); cpu = new(MeasurePath);
        ApplyShellLanguage(); ApplyAppearance();
        ExtendsContentIntoTitleBar = true; SetTitleBar(DragRegion);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        { presenter.IsResizable = false; presenter.IsMaximizable = false; }
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
        int width = Math.Min((int)(1240 * scale), display.WorkArea.Width - 32);
        int height = Math.Min((int)(860 * scale), display.WorkArea.Height - 32);
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(display.WorkArea.X + (display.WorkArea.Width - width) / 2, display.WorkArea.Y + (display.WorkArea.Height - height) / 2));
        AppWindow.Closing += Closing;
        Root.Loaded += async (_, _) => await Guard(Initialize);
        timer.Tick += async (_, _) => await Guard(Poll);
        ThemeButton.PointerEntered += (_, _) => SetThemeIconColor(ChartAccent);
        ThemeButton.PointerExited += (_, _) => SetThemeIconColor(Ink);
        systemTheme.ColorValuesChanged += SystemThemeChanged;
        Activated += (_, e) => { windowActive = e.WindowActivationState != WindowActivationState.Deactivated; RefreshPatternMotion(); };
        Navigation.SelectedIndex = 0;
    }
    private async Task Initialize()
    {
        await session.Restore();
        var hardware = await Task.Run(() => new[]
        {
            Wmi.Query("SELECT Name FROM Win32_Processor").FirstOrDefault()?.Text("Name") ?? UiStrings.Get("Text026"),
            Wmi.Query("SELECT Name FROM Win32_VideoController").FirstOrDefault()?.Text("Name") ?? UiStrings.Get("Text027"),
            Wmi.Query("SELECT Caption FROM Win32_OperatingSystem").FirstOrDefault()?.Text("Caption") ?? "Windows"
        });
        cpuName = hardware[0]; gpuName = hardware[1]; osName = hardware[2].Replace("Майкрософт ", "").Replace("Microsoft ", "");
        await Poll(); ShowPage(currentPage); timer.Start();
        if (smokeDirectory is not null)
        {
            if (Environment.GetCommandLineArgs().Contains("--driver-discovery")) await DriverDiscoverySmoke(smokeDirectory);
            else await Smoke(smokeDirectory);
        }
    }
    private async Task Poll()
    {
        if (polling || closing) return;
        polling = true;
        try
        {
            if (lastHighContrast != accessibility.HighContrast) { ApplyAppearance(); ShowPage(currentPage, true); }
            sample = await Task.Run(sampler.Sample);
            var g = gpu.Current.Gpus.FirstOrDefault();
            history.Add(new(sample.Time, sample.CpuPercent, g?.UsagePercent, sample.MemoryPercent));
            history.RemoveAll(p => p.Time < sample.Time.AddSeconds(-120));
            if (ticks++ % 3 == 0)
            {
                var gameRoots = processes.Where(p => p.Path.Equals(session.Profile.GamePath, StringComparison.OrdinalIgnoreCase)).Select(p => p.Pid).ToHashSet();
                processes = await Task.Run(() => inventory.Read(session.Profile.AllowedApps, gameRoots));
                processes = ExperimentalProcessProtection.Apply(processes, session.Profile.GamePath);
                await session.Poll(processes);
                if (processList is not null) FilterProcesses();
            }
            RestoreButton.Visibility = session.Armed || session.ActiveChanges > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var update in liveUpdates) update();
            foreach (var chart in charts) chart.Update(history);
            RefreshPatternMotion();
        }
        finally { polling = false; }
    }
    private async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            new AuditLog(dataDirectory).Write(e.ToString());
            MessageBar.Title = UiStrings.Get("Text199");
            MessageBar.Message = e is InvalidOperationException or InvalidDataException && UiStrings.IsUserMessage(e.Message) ? e.Message : UiStrings.Get("Text200");
            MessageBar.Severity = InfoBarSeverity.Error; MessageBar.IsOpen = true;
        }
    }
    private Button Button(string label, Func<Task> action, bool primary = false)
    {
        var b = new Button { Content = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 * appearance.TextScale, CornerRadius = new(ControlRadius), Padding = new(18 * appearance.Spacing, 11 * appearance.Spacing, 18 * appearance.Spacing, 11 * appearance.Spacing) };
        if (primary) { b.Background = Accent; b.Foreground = AccentText; b.BorderThickness = new(0); }
        b.Click += async (_, _) =>
        {
            if (busy || closing) return;
            busy = true; b.IsEnabled = false;
            try { await Guard(action); }
            finally { busy = false; b.IsEnabled = true; }
        };
        return b;
    }
    private Button Link(string label, Action action) => Button(label, () => { action(); return Task.CompletedTask; });
    private TextBlock Text(string text, double size = 14, bool bold = false) => Ui.Text(text, size * appearance.TextScale, Ink, bold);
    private TextBlock Hint(string text, double size = 12) => Ui.Text(text, size * appearance.TextScale, Muted);
    private Border Group(UIElement content, Thickness? padding = null)
    {
        var p = padding ?? new Thickness(22);
        var group = Ui.Group(content, PanelBrush(Surface), GlassEnabled ? GlassEdge() : Line, new Thickness(p.Left * appearance.Spacing, p.Top * appearance.Spacing, p.Right * appearance.Spacing, p.Bottom * appearance.Spacing));
        group.CornerRadius = new(appearance.Radius); group.BorderThickness = new(appearance.PanelOutlines ? 1 : 0); return group;
    }
    private StackPanel Heading(string title, string description)
    { var p = Ui.Stack(8); p.Children.Add(Text(title, 20, true)); p.Children.Add(Hint(description, 13)); return p; }
    private void Navigate(object sender, SelectionChangedEventArgs e)
    { if (Navigation.SelectedItem is ListViewItem item) ShowPage((string)item.Tag); }
    private void Go(string page)
    { Navigation.SelectedItem = Navigation.Items.Cast<ListViewItem>().First(i => (string)i.Tag == page); }
    private void ShowPage(string page, bool refreshAppearance = false)
    {
        double offset = refreshAppearance ? PageScroll.VerticalOffset : 0;
        currentPage = page; charts.Clear(); liveUpdates.Clear(); legendItems.Clear(); processList = null;
        var title = page switch { "Gaming" => UiStrings.Get("Text201"), "Processes" => UiStrings.Get("Text202"), "Monitoring" => UiStrings.Get("Text203"), "Device" => UiStrings.Get("Text025"), "Settings" => UiStrings.Get("Text204"), _ => UiStrings.Get("Text205") };
        PageTitle.Text = title; Breadcrumb.Text = page == "Overview" ? UiStrings.Get("Text206") : title;
        PageContent.ContentTransitions = preferences.ReducedMotion || refreshAppearance ? new TransitionCollection() : new TransitionCollection { new EntranceThemeTransition { IsStaggeringEnabled = false } };
        PageContent.Content = page switch { "Gaming" => Gaming(), "Processes" => Processes(), "Monitoring" => Monitoring(), "Device" => Device(), "Settings" => Settings(), _ => Dashboard() };
        Root.UpdateLayout(); PageScroll.ChangeView(null, offset, null, true);
        foreach (var update in liveUpdates) update();
        foreach (var chart in charts) chart.Update(history);
    }
    private async void RestoreClicked(object sender, RoutedEventArgs e) => await Guard(async () => { await session.Restore(); ShowPage(currentPage); Toast(UiStrings.Get("Text207")); });
    private void Toast(string text)
    { MessageBar.Title = text; MessageBar.Message = ""; MessageBar.Severity = InfoBarSeverity.Success; MessageBar.IsOpen = true; }
    private async Task<bool> Confirm(string title, string text, string action)
    {
        var dialog = new ContentDialog { Title = title, Content = new ScrollViewer { Content = Text(text, 14), MaxHeight = 400 }, PrimaryButtonText = action, CloseButtonText = UiStrings.Get("Text208"), DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme };
        return await ShowDialog(dialog) == ContentDialogResult.Primary;
    }
    private async Task<ContentDialogResult> ShowDialog(ContentDialog dialog)
    {
        if (closing) return ContentDialogResult.None;
        activeDialog = dialog;
        try { return await dialog.ShowAsync(); }
        finally { activeDialog = null; }
    }
    private async void Closing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (closing) return;
        closing = true; timer.Stop();
        PatternBackground.SetMotion(BackdropCanvas, false);
        activeDialog?.Hide();
        // Finish an in-flight game transition before restoration and teardown.
        while (polling || busy) await Task.Delay(50);
        try
        {
            await session.Shutdown(); systemTheme.ColorValuesChanged -= SystemThemeChanged; gpu.Dispose(); cpu.Dispose(); sampler.Dispose(); session.Dispose();
            allowClose = true; Close();
        }
        catch (Exception ex)
        { closing = false; timer.Start(); await Guard(() => Task.FromException(ex)); }
    }
    private static string Percent(double? value) => value is double n ? $"{n:F0}%" : "—";
    private static string Number(double? value, string unit) => value is double n ? $"{n:F0} {unit}" : "—";
}
