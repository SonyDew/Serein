using System.Windows;
using System.Windows.Controls;
using Serein.Core;

namespace Serein.Desktop;
public partial class MainWindow
{
    private TextBlock? dashboardSensors, monitoringSensors;
    private Button? cpuConnectButton, cpuDownloadButton;
    private void ConnectCpuSensors()
    {
        cpuTelemetry.Dispose(); cpuTelemetry = new(System.IO.Path.Combine(AppContext.BaseDirectory, "Serein.Measure.exe"), requestElevation: true); RefreshSensorLabels();
    }
    private string CpuSensorText(bool detailed)
    {
        var snapshot = cpuTelemetry.Current;
        string Value(double? number, string unit) => number is double n ? n.ToString("F0") + unit : "—";
        if (snapshot.Cpu is CpuReading cpu)
            return T("Процессор ", "Processor ") + Value(cpu.TemperatureC, " °C") + (detailed ? T($" · {Value(cpu.PeakClockMHz, " МГц")} · {Value(cpu.PackagePowerW, " Вт")}", $" · {Value(cpu.PeakClockMHz, " MHz")} · {Value(cpu.PackagePowerW, " W")}") : "");
        return snapshot.Status switch
        {
            CpuSensorStatus.Starting => T("Подключаем датчики процессора…", "Connecting processor sensors…"),
            CpuSensorStatus.SdkNotInstalled => detailed ? T("Для температуры процессора нужен компонент мониторинга AMD. Установите его с сайта AMD и повторите подключение.", "Processor temperature requires AMD's monitoring component. Install it from AMD and reconnect.") : T("Температура процессора —", "Processor temperature —"),
            CpuSensorStatus.DriverNotRunning => T("Компонент мониторинга AMD не запущен. Проверьте его установку.", "AMD's monitoring component is not running. Check its installation."),
            CpuSensorStatus.UnsupportedVersion => T("Эта версия компонента AMD пока не поддерживается.", "This version of AMD's monitoring component is not supported yet."),
            CpuSensorStatus.AccessRequired => detailed ? T("Для температуры процессора нажмите «Подключить датчик процессора» и разрешите доступ в окне Windows.", "For processor temperature, click Connect processor sensor and allow access in the Windows prompt.") : T("Температура процессора —", "Processor temperature —"),
            _ => T("Температура процессора недоступна. Проверьте компонент мониторинга AMD.", "Processor temperature is unavailable. Check AMD's monitoring component.")
        };
    }
    private string SensorSummary(bool detailed)
    {
        var snapshot = gpuTelemetry.Current;
        if (snapshot.Gpus.Count == 0)
        {
            return CpuSensorText(detailed) + "\n\n" + (snapshot.Status == "Starting" ? T("Подключаем датчики видеокарты…", "Connecting graphics sensors…") : T("Датчики видеокарты недоступны. Проверьте её драйвер или повторите подключение.", "Graphics sensors are unavailable. Check the graphics driver or reconnect."));
        }
        string Value(double? value, string unit, string format = "F0") => value.HasValue ? value.Value.ToString(format) + unit : "—";
        var rows = snapshot.Gpus.Select(gpu =>
            (detailed ? gpu.Name + "\n" : snapshot.Gpus.Count > 1 ? gpu.Name + "\n" : "") +
            T($"Видеокарта {Value(gpu.TemperatureC, " °C")}", $"Graphics {Value(gpu.TemperatureC, " °C")}") + (detailed ? T($" · горячая точка {Value(gpu.HotspotC, " °C")}", $" · hotspot {Value(gpu.HotspotC, " °C")}") : "") + "\n" +
            T($"Загрузка {Value(gpu.UsagePercent, "%")} · видеопамять {Value(gpu.UsedMemoryMb, "")} / {Value(gpu.TotalMemoryMb, " МБ")}", $"Usage {Value(gpu.UsagePercent, "%")} · video memory {Value(gpu.UsedMemoryMb, "")} / {Value(gpu.TotalMemoryMb, " MB")}") + "\n" +
            T($"Плата {Value(gpu.BoardPowerW, " Вт")} · {Value(gpu.CoreClockMHz, " МГц")} · {Value(gpu.FanRpm, " об/мин")}", $"Board {Value(gpu.BoardPowerW, " W")} · {Value(gpu.CoreClockMHz, " MHz")} · {Value(gpu.FanRpm, " RPM")}") +
            (detailed && gpu.ChipPowerW is not null ? "\n" + T($"Питание чипа {Value(gpu.ChipPowerW, " Вт")}", $"Chip power {Value(gpu.ChipPowerW, " W")}") : "") +
            (detailed && gpu.MemoryClockMHz is not null ? "\n" + T($"Частота видеопамяти {Value(gpu.MemoryClockMHz, " МГц")}", $"Video memory clock {Value(gpu.MemoryClockMHz, " MHz")}") : ""));
        return CpuSensorText(detailed) + "\n\n" + string.Join("\n\n", detailed ? rows : rows.Take(1));
    }
    private FrameworkElement SensorPanel()
    {
        var panel = new StackPanel(); panel.Children.Add(SectionTitle(T("Температура и питание", "Temperature and power"), T("Данные обновляются каждые 5 секунд. Прочерк означает, что датчик недоступен.", "Readings update every 5 seconds. A dash means the sensor is unavailable.")));
        monitoringSensors = Text(SensorSummary(true), 13, 12); panel.Children.Add(monitoringSensors);
        var actions = new WrapPanel();
        actions.Children.Add(Action(T("Повторить подключение", "Reconnect sensors"), () =>
        {
            gpuTelemetry.Dispose(); gpuTelemetry = new(System.IO.Path.Combine(AppContext.BaseDirectory, "Serein.Measure.exe"));
            if (cpuTelemetry.Current.Cpu is null) { cpuTelemetry.Dispose(); cpuTelemetry = new(System.IO.Path.Combine(AppContext.BaseDirectory, "Serein.Measure.exe")); }
            RefreshSensorLabels();
        }));
        cpuConnectButton = Action(T("Подключить датчик процессора", "Connect processor sensor"), ConnectCpuSensors);
        cpuConnectButton.ToolTip = T("Windows запросит разрешение. Доступ используется только для чтения датчиков до выхода из Serein.", "Windows will ask for permission. Access is used only to read sensors until Serein exits."); actions.Children.Add(cpuConnectButton);
        cpuDownloadButton = Action(T("Компонент мониторинга AMD", "AMD monitoring component"), () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.amd.com/en/developer/ryzen-master-monitoring-sdk.html") { UseShellExecute = true })); actions.Children.Add(cpuDownloadButton);
        actions.Children.Add(Action(T("Сведения о подключении", "Connection details"), () => CreateActionDialog(T("Сведения о датчиках", "Sensor details"), $"GPU: {gpuTelemetry.Current.Provider}\n{gpuTelemetry.Current.Status}\n\nCPU: AMD Ryzen Master Monitoring SDK 3.1.1.5478\n{cpuTelemetry.Current.Detail}", false).ShowDialog()));
        panel.Children.Add(actions);
        RefreshSensorLabels();
        return Card(panel, new(0, 0, 0, 16));
    }
    private void RefreshSensorLabels()
    {
        var sample = gpuTelemetry.Current;
        if (dashboardSensors is not null) { dashboardSensors.Text = SensorSummary(false); dashboardSensors.ToolTip = T("Обновлено ", "Updated ") + sample.Time.ToLocalTime().ToString("HH:mm:ss"); }
        if (monitoringSensors is not null) monitoringSensors.Text = SensorSummary(true);
        var cpu = cpuTelemetry.Current;
        if (cpuConnectButton is not null) cpuConnectButton.Visibility = cpu.Status is CpuSensorStatus.AccessRequired or CpuSensorStatus.Unavailable ? Visibility.Visible : Visibility.Collapsed;
        if (cpuDownloadButton is not null) cpuDownloadButton.Visibility = cpu.Status is CpuSensorStatus.SdkNotInstalled or CpuSensorStatus.UnsupportedVersion ? Visibility.Visible : Visibility.Collapsed;
    }
}
