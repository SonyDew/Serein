using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Serein.Core;
using Orientation = System.Windows.Controls.Orientation;

namespace Serein.Desktop;

public partial class MainWindow
{
    private string processorName = "", graphicsName = "";
    private readonly Dictionary<string, MetricTile> dashboardTiles = [];
    private TextBlock? dashboardGame, dashboardState, dashboardChanges, dashboardFrozen, dashboardPower, chartWindow, dashboardActivity;
    private void ResetDashboard()
    {
        dashboardTiles.Clear(); dashboardGame = dashboardState = dashboardChanges = dashboardFrozen = dashboardPower = chartWindow = dashboardActivity = null;
        dashboardSensors = monitoringSensors = null;
    }
    private FrameworkElement Dashboard()
    {
        var work = new StackPanel(); var tiles = new UniformGrid { Columns = 4, Margin = new(0, 0, -12, 16) };
        foreach (var (key, title, glyph) in new[] { ("CPU", T("Процессор", "Processor"), "\uE950"), ("RAM", T("Память", "Memory"), "\uE964"), ("DISK", T("Диск", "Disk"), "\uEDA2"), ("NET", T("Сеть", "Network"), "\uE968") })
        {
            var tile = new MetricTile(title, glyph) { Margin = new(0, 0, 12, 0) }; dashboardTiles[key] = tile; tiles.Children.Add(tile);
        }
        work.Children.Add(tiles);

        var sessionRow = new Grid { Margin = new(0, 0, 0, 16) }; sessionRow.ColumnDefinitions.Add(new() { Width = new(1.9, GridUnitType.Star) }); sessionRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var session = new Grid();
        session.Children.Add(new TextBlock { Text = "\uE7FC", FontFamily = new("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 116, Opacity = .08, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0), IsHitTestVisible = false });
        var sessionText = new StackPanel(); var caption = Muted(T("ИГРОВАЯ СЕССИЯ", "GAME SESSION"), 10, 10); sessionText.Children.Add(caption);
        dashboardGame = Text("", 23, 6); dashboardGame.FontWeight = FontWeights.SemiBold; dashboardGame.MaxWidth = 490; dashboardGame.HorizontalAlignment = HorizontalAlignment.Left; sessionText.Children.Add(dashboardGame);
        dashboardState = Muted("", 12, 18); sessionText.Children.Add(dashboardState);
        var actions = Row(); actions.Children.Add(Action(T("Настроить сессию", "Configure session"), () => { SelectPage("Gaming"); return System.Threading.Tasks.Task.CompletedTask; }, true)); actions.Children.Add(Action(T("Профили", "Profiles"), () => SelectPage("Profiles"))); sessionText.Children.Add(actions); session.Children.Add(sessionText);
        var sessionCard = Card(session, new(0, 0, 16, 0)); sessionCard.MinHeight = 178; sessionRow.Children.Add(sessionCard);
        var owned = new StackPanel(); owned.Children.Add(Muted(T("АКТИВНЫЕ ОПТИМИЗАЦИИ", "ACTIVE OPTIMIZATIONS"), 10, 12));
        dashboardChanges = Text("0", 34, 2); dashboardChanges.FontWeight = FontWeights.SemiBold; owned.Children.Add(dashboardChanges);
        dashboardFrozen = Muted("", 12, 10); owned.Children.Add(dashboardFrozen); dashboardPower = Muted("", 11, 0); owned.Children.Add(dashboardPower);
        var changeCard = Card(owned); Grid.SetColumn(changeCard, 1); sessionRow.Children.Add(changeCard); work.Children.Add(sessionRow);

        var lower = new Grid(); lower.ColumnDefinitions.Add(new() { Width = new(1.9, GridUnitType.Star) }); lower.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var chartContent = new StackPanel(); var chartHeading = new DockPanel();
        var legend = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cpuKey = Text("● CPU", 11, 0); cpuKey.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush"); legend.Children.Add(cpuKey); var ramKey = Muted("  ─ RAM", 11, 0); legend.Children.Add(ramKey); DockPanel.SetDock(legend, Dock.Right); chartHeading.Children.Add(legend); chartHeading.Children.Add(Text(T("Нагрузка системы", "System load"), 16, 0)); chartContent.Children.Add(chartHeading);
        chartWindow = Muted("", 10, 12); chartWindow.Margin = new(0, 7, 0, 12); chartContent.Children.Add(chartWindow);
        chart = new TrendChart { Height = 137, MinHeight = 100, Samples = samples }; chartContent.Children.Add(chart);
        var chartCard = Card(chartContent, new(0, 0, 16, 0)); lower.Children.Add(chartCard);
        var hardware = new StackPanel(); hardware.Children.Add(SectionTitle(T("Ваше устройство", "Your device")));
        hardware.Children.Add(HardwareLine(T("Процессор", "Processor"), processorName.Length > 0 ? processorName : T("Определяется…", "Detecting…")));
        hardware.Children.Add(HardwareLine(T("Видеокарта", "Graphics"), graphicsName.Length > 0 ? graphicsName : T("Не определена", "Unavailable")));
        string windows = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? "Windows 11" : "Windows 10";
        hardware.Children.Add(Muted(T($"{windows} · {Environment.ProcessorCount} потоков", $"{windows} · {Environment.ProcessorCount} threads"), 10, 9));
        dashboardSensors = Text(SensorSummary(false), 10, 6); hardware.Children.Add(dashboardSensors);
        var hardwareCard = Card(hardware); Grid.SetColumn(hardwareCard, 1); lower.Children.Add(hardwareCard); work.Children.Add(lower);
        dashboardActivity = Muted("", 10, 0); dashboardActivity.Margin = new(2, 12, 0, 0); dashboardActivity.TextTrimming = TextTrimming.CharacterEllipsis; dashboardActivity.TextWrapping = TextWrapping.NoWrap; work.Children.Add(dashboardActivity);
        return Scroll(work);
    }
    private FrameworkElement HardwareLine(string name, string value)
    {
        var panel = new StackPanel { Margin = new(0, 0, 0, 12) }; panel.Children.Add(Muted(name, 10, 3));
        var label = Text(value, 12, 0); label.TextTrimming = TextTrimming.CharacterEllipsis; label.TextWrapping = TextWrapping.NoWrap; label.ToolTip = value; panel.Children.Add(label); return panel;
    }
    private void RefreshDashboard()
    {
        RefreshSensorLabels();
        if (latest is MetricSample sample && dashboardTiles.Count > 0)
        {
            dashboardTiles["CPU"].Update(sample.CpuPercent is double cpu ? $"{cpu:F1}%" : "—", T($"{Environment.ProcessorCount} логических процессоров", $"{Environment.ProcessorCount} logical processors"), sample.CpuPercent);
            dashboardTiles["RAM"].Update($"{sample.MemoryPercent:F1}%", $"{(sample.TotalMemory - sample.AvailableMemory) / 1073741824d:F1} / {sample.TotalMemory / 1073741824d:F1} GB", sample.MemoryPercent);
            dashboardTiles["DISK"].Update(sample.DiskBytesPerSecond is double disk ? $"{disk / 1048576:F1}" : "—", T("МБ/с · чтение и запись", "MB/s · read and write"), null);
            dashboardTiles["NET"].Update($"{sample.NetworkBytesPerSecond / 1024:F0}", T("КБ/с · приём и передача", "KB/s · receive and send"), null);
        }
        int active = changes.Where(c => c.Status == ChangeStatus.Applied).Select(c => new { c.Target, c.Kind }).Distinct().Count() + (powerLease is { Status: ChangeStatus.Applied } ? 1 : 0);
        int frozen = changes.Where(c => c.Kind == "Freeze" && c.Status == ChangeStatus.Applied).Select(c => c.Target).Distinct().Count();
        int attention = changes.Count(c => c.Status == ChangeStatus.Attention || c.Status == ChangeStatus.Applied && c.Error is not null) + (powerLease is { Status: ChangeStatus.Attention } or { Status: ChangeStatus.Applied, Error: not null } ? 1 : 0);
        if (dashboardGame is not null) dashboardGame.Text = selectedProfile.GamePath is not null ? Path.GetFileNameWithoutExtension(selectedProfile.GamePath) : T("Подготовьтесь к игре", "Ready for your next game");
        if (dashboardState is not null) dashboardState.Text = tracker.Active ? T($"Игра запущена · {selectedProfile.Name}", $"Game running · {selectedProfile.Name}") : armed ? T("Наблюдение включено · ожидаем запуск игры", "Monitoring armed · waiting for game") : T("Выберите игру, профиль и фоновые приложения.", "Choose a game, profile and background apps.");
        if (dashboardChanges is not null) dashboardChanges.Text = active.ToString();
        if (dashboardFrozen is not null) dashboardFrozen.Text = attention > 0 ? T($"Требуют проверки: {attention} · заморожено: {frozen}", $"Need attention: {attention} · frozen apps: {frozen}") : T($"действий · заморожено приложений: {frozen}", $"actions · frozen apps: {frozen}");
        if (dashboardPower is not null) dashboardPower.Text = powerLease is { Status: not ChangeStatus.Reverted } ? T($"Питание: {powerLease.Status}", $"Power: {powerLease.Status}") : T("Временная схема питания не применена", "No temporary power policy");
        if (chartWindow is not null) chartWindow.Text = samples.Count > 1 ? T($"Последние {(samples[^1].Time - samples[0].Time).TotalSeconds:F0} с · шкала 0–100%", $"Last {(samples[^1].Time - samples[0].Time).TotalSeconds:F0}s · 0–100% scale") : T("Собираем первые отсчёты…", "Collecting initial samples…");
        if (dashboardActivity is not null) { dashboardActivity.Text = recent.Count > 0 ? recent[^1] : ""; dashboardActivity.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed; }
    }
}

public sealed class MetricTile : Border
{
    private readonly TextBlock value, detail;
    private readonly ProgressBar meter;
    public MetricTile(string title, string glyph)
    {
        SetResourceReference(StyleProperty, "CardStyle"); Padding = new(17, 15, 17, 13); MinHeight = 116;
        var content = new StackPanel(); var heading = new DockPanel();
        var icon = new TextBlock { Text = glyph, FontFamily = new("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Right }; icon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); DockPanel.SetDock(icon, Dock.Right); heading.Children.Add(icon);
        var name = new TextBlock { Text = title, FontSize = 11 }; name.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); heading.Children.Add(name); content.Children.Add(heading);
        value = new() { Text = "—", FontSize = 29, FontWeight = FontWeights.SemiBold, Margin = new(0, 9, 0, 1) }; content.Children.Add(value);
        detail = new() { FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis }; detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); content.Children.Add(detail);
        meter = new() { Minimum = 0, Maximum = 100, Height = 3, Margin = new(0, 11, 0, 0), IsHitTestVisible = false }; meter.SetResourceReference(Control.ForegroundProperty, "AccentTextBrush"); meter.SetResourceReference(Control.BackgroundProperty, "LineBrush"); content.Children.Add(meter); Child = content;
    }
    public void Update(string number, string note, double? percent)
    {
        value.Text = number; detail.Text = note; meter.Visibility = percent.HasValue ? Visibility.Visible : Visibility.Hidden;
        if (percent is double p) meter.Value = Math.Clamp(p, 0, 100);
    }
}
