using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serein.Core;

namespace Serein_Gaming_WinUI;
public sealed partial class MainWindow
{
    private FrameworkElement Dashboard()
    {
        var page = Ui.Stack(16);
        var sessionPanel = Ui.Stack(16);
        var state = Text("", 13);
        var title = Ui.Stack(7); title.Children.Add(Text(UiStrings.Get("Text001"), 20, true)); title.Children.Add(state);
        var icon = Ui.Icon("\uE7FC", 40); icon.Foreground = Accent;
        var left = Ui.Stack(12); left.Children.Add(Ui.Row(icon, title)); left.Children.Add(Button(UiStrings.Get("Text002"), () => { Go("Gaming"); return Task.CompletedTask; }, true));
        var mode = Ui.Stack(10); mode.VerticalAlignment = VerticalAlignment.Center;
        mode.Children.Add(ModePicker());
        sessionPanel.Children.Add(Ui.Columns(left, mode, 1.35, 1));
        if (appearance.ShowSession) page.Children.Add(Group(sessionPanel, new Thickness(22, 16, 22, 16)));
        liveUpdates.Add(() => state.Text = session.Playing ? UiStrings.Get("Text003") + System.IO.Path.GetFileNameWithoutExtension(session.Profile.GamePath) : session.Armed ? UiStrings.Get("Text004") : UiStrings.Get("Text005"));
        if (appearance.ShowResources) page.Children.Add(Metrics());
        var activity = Ui.Stack(18);
        var header = new Grid(); header.Children.Add(Text(UiStrings.Get("Text006"), 19, true));
        var legend = Ui.Row();
        for (int i = 0; i < 3; i++) legend.Children.Add(LegendItem(new[] { "CPU", "GPU", "RAM" }[i], MetricBrush(i)));
        legend.HorizontalAlignment = HorizontalAlignment.Right; header.Children.Add(legend);
        activity.Children.Add(header);
        var chart = CreateChart(height: 125); activity.Children.Add(chart);
        var axis = new Grid(); axis.Children.Add(Hint(UiStrings.Format("Text007", appearance.HistorySeconds), 11)); var now = Hint(UiStrings.Get("Text008"), 11); now.HorizontalAlignment = HorizontalAlignment.Right; axis.Children.Add(now); activity.Children.Add(axis);
        if (appearance.ShowActivity && appearance.ShowHardware) page.Children.Add(Ui.Columns(Group(activity), Group(HardwareSummary()), 1.75, 1));
        else if (appearance.ShowActivity) page.Children.Add(Group(activity));
        else if (appearance.ShowHardware) page.Children.Add(Group(HardwareSummary()));
        var background = Ui.Columns(Heading(UiStrings.Get("Text009"), UiStrings.Get("Text010")), Link(UiStrings.Get("Text011"), () => Go("Gaming")), 2, 1);
        if (appearance.ShowBackgroundApps) page.Children.Add(Group(background));
        if (page.Children.Count == 0)
        {
            var empty = Heading(UiStrings.Get("Text012"), UiStrings.Get("Text013"));
            empty.Children.Add(Link(UiStrings.Get("Text014"), () => Go("Settings"))); page.Children.Add(Group(empty));
        }
        return page;
    }
    private FrameworkElement ModePicker()
    {
        var row = new Grid { ColumnSpacing = 4, MinWidth = 285 };
        var buttons = new List<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>();
        var modes = new[] { ProfileMode.Balanced, ProfileMode.Gaming, ProfileMode.ExtremeGaming };
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var b = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton { Content = new[] { UiStrings.Get("Text015"), UiStrings.Get("Text016"), UiStrings.Get("Text017") }[i], IsChecked = session.Profile.Mode == modes[i], IsEnabled = !session.Armed, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 * appearance.TextScale, Padding = new(10, 11, 10, 11), CornerRadius = new(ControlRadius) };
            foreach (string state in new[] { "Checked", "CheckedPointerOver", "CheckedPressed" })
            {
                b.Resources["ToggleButtonBackground" + state] = Accent;
                b.Resources["ToggleButtonForeground" + state] = AccentText;
            }
            if (b.IsChecked == true) { b.Background = Accent; b.Foreground = AccentText; }
            buttons.Add(b); Grid.SetColumn(b, i); row.Children.Add(b);
            b.Click += async (_, _) => await Guard(() =>
            {
                session.Save(session.Profile with { Mode = modes[index] });
                ShowPage(currentPage); return Task.CompletedTask;
            });
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, UiStrings.Get("Text018"));
        return row;
    }
    private FrameworkElement Metrics()
    {
        var row = new Grid { ColumnSpacing = 14 };
        for (int i = 0; i < 3; i++) row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (int i = 0; i < 3; i++)
        {
            int metric = i;
            var body = Ui.Stack(10);
            var top = new Grid(); top.Children.Add(Text(new[] { "CPU", "GPU", "RAM" }[i], 14, true));
            var value = Text("—", 25, true); value.HorizontalAlignment = HorizontalAlignment.Right; top.Children.Add(value); body.Children.Add(top);
            var chart = CreateChart(i, 30); body.Children.Add(chart);
            var detail = Hint("", 11); body.Children.Add(detail);
            liveUpdates.Add(() =>
            {
                var graphics = gpu.Current.Gpus.FirstOrDefault();
                value.Text = Percent(metric switch { 0 => sample?.CpuPercent, 1 => graphics?.UsagePercent, _ => sample?.MemoryPercent });
                detail.Text = metric switch
                {
                    0 => cpu.Current.Cpu?.TemperatureC is double t ? UiStrings.Format("Text019", t, Environment.ProcessorCount) : UiStrings.Format("Text020", Environment.ProcessorCount),
                    1 => graphics?.TemperatureC is double t ? UiStrings.Format("Text021", t, graphics.UsedMemoryMb / 1024) : UiStrings.Get("Text022"),
                    _ => sample is null ? UiStrings.Get("Text023") : UiStrings.Format("Text024", (sample.TotalMemory - sample.AvailableMemory) / 1073741824d, sample.TotalMemory / 1073741824d)
                };
            });
            var card = Group(body, new Thickness(20, 18, 20, 14)); Grid.SetColumn(card, i); row.Children.Add(card);
        }
        return row;
    }
    private StackPanel HardwareSummary()
    {
        var p = Ui.Stack(18); p.Children.Add(Text(UiStrings.Get("Text025"), 19, true));
        foreach (var (name, label) in new[] { (cpuName, UiStrings.Get("Text026")), (gpuName, UiStrings.Get("Text027")), (osName, UiStrings.Get("Text028")) })
        {
            var line = Ui.Stack(5); line.Children.Add(Text(name, 13)); line.Children.Add(Hint(label, 11)); p.Children.Add(line);
        }
        return p;
    }
    private FrameworkElement Monitoring()
    {
        var page = Ui.Stack(18); page.Children.Add(Metrics());
        var body = Ui.Stack(18); body.Children.Add(Text(UiStrings.Format("Text029", appearance.HistorySeconds), 20, true));
        var chart = CreateChart(height: 210); body.Children.Add(chart); body.Children.Add(Hint("CPU · GPU · RAM     /     0–100%")); page.Children.Add(Group(body));
        var values = Text("", 14); values.LineHeight = 28;
        liveUpdates.Add(() =>
        {
            var g = gpu.Current.Gpus.FirstOrDefault(); var c = cpu.Current.Cpu;
            values.Text = UiStrings.Format("Text034", Number(c?.TemperatureC, "°C"), Number(c?.PeakClockMHz, UiStrings.Get("Text030")), Number(c?.PackagePowerW, UiStrings.Get("Text031")), Number(g?.TemperatureC, "°C"), Number(g?.CoreClockMHz, UiStrings.Get("Text030")), Number(g?.BoardPowerW ?? g?.ChipPowerW, UiStrings.Get("Text031")), Number(g?.UsedMemoryMb, UiStrings.Get("Text032")), Number(g?.TotalMemoryMb, UiStrings.Get("Text032")), Number(sample?.DiskBytesPerSecond / 1048576, UiStrings.Get("Text033")), Number(sample?.NetworkBytesPerSecond / 1048576, UiStrings.Get("Text033")));
        });
        var sensors = Ui.Stack(14); sensors.Children.Add(Text(UiStrings.Get("Text035"), 20, true)); sensors.Children.Add(values);
        var status = Hint(""); liveUpdates.Add(() => status.Text = cpu.Current.Status switch
        {
            CpuSensorStatus.Available => UiStrings.Get("Text036"),
            CpuSensorStatus.AccessRequired => UiStrings.Get("Text037"),
            CpuSensorStatus.SdkNotInstalled => UiStrings.Get("Text038"),
            CpuSensorStatus.Starting => UiStrings.Get("Text039"), _ => UiStrings.Get("Text040")
        }); sensors.Children.Add(status);
        sensors.Children.Add(Button(UiStrings.Get("Text041"), async () =>
        {
            if (!await Confirm(UiStrings.Get("Text042"), UiStrings.Get("Text043"), UiStrings.Get("Text044"))) return;
            cpu.Dispose(); cpu = new(MeasurePath, requestElevation: true); Toast(UiStrings.Get("Text045"));
        }));
        page.Children.Add(Group(sensors)); return page;
    }
}
