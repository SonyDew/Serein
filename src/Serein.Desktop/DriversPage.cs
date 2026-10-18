using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Serein.Core;
using Orientation = System.Windows.Controls.Orientation;

namespace Serein.Desktop;
public partial class MainWindow
{
    private bool driverInstalling;
    private bool DriverOperationActive => driverInstalling || DriverAdminClient.IsRunning(Path.Combine(AppContext.BaseDirectory, "Serein.Admin.exe"));
    private List<DriverCandidate> driverCandidates = [];
    private InfPackage? inspectedPackage;
    private FrameworkElement Drivers()
    {
        var tabs = new TabControl { Margin = new(0, 8, 0, 0) };
        var deviceWork = Workspace(); var buttons = Row();
        var deviceGrid = Table((T("Устройство", "Device"), "Name", 230), ("Health", "Health", 130), ("Provider", "Provider", 130), ("Version", "Version", 125), ("INF", "Inf", 105), ("Signed metadata", "Signature", 100), ("Hardware IDs", "Hardware", 340)); deviceGrid.ItemsSource = devices;
        buttons.Children.Add(Action(T("Сканировать устройства", "Scan devices"), async () => { devices = await Task.Run(DeviceInventory.Read); deviceGrid.ItemsSource = devices; Report(T($"Устройств найдено: {devices.Count}", $"Devices found: {devices.Count}")); }));
        var sourceWork = Workspace(); var sourceTop = new StackPanel(); var sourceButtons = Row();
        var sourceGrid = Table((T("Источник", "Source"), "Name", 190), ("URL", "Url", 430), (T("Основание", "Evidence"), "Evidence", 410)); sourceGrid.ItemsSource = DriverSources.Catalog;
        buttons.Children.Add(Action(T("Источники для устройства", "Sources for device"), async () =>
        {
            if (deviceGrid.SelectedItem is not DeviceRow device) throw new InvalidOperationException(T("Выберите устройство.", "Choose a device."));
            var model = await Task.Run(ComputerModel.Read); sourceGrid.ItemsSource = DriverSources.For(device, model); tabs.SelectedIndex = 3;
            Report($"OEM: {model.Manufacturer} {model.Model}; board: {model.BoardManufacturer} {model.BoardModel}. No serial numbers sent.");
        }));
        buttons.Children.Add(Action(T("Экспорт копии", "Export backup"), async () =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = T("Выберите пустую папку для копии драйверов", "Choose an empty folder for driver backups") }; if (picker.ShowDialog(this) != true) return;
            DriverBackup.Prepare(picker.FolderName);
            if (!Confirm(T("Сохранить копии сторонних драйверов? Windows запросит права администратора. Такая копия не включает всю систему и не гарантирует откат.", "Back up third-party drivers? Windows will request administrator access. This is not a full system backup and does not guarantee rollback."))) return;
            await DriverBackup.Export(picker.FolderName); Report(T("Копия экспортирована.", "Backup exported."));
        }));
        DockPanel.SetDock(buttons, Dock.Top); deviceWork.Children.Add(buttons); deviceWork.Children.Add(Searchable(deviceGrid, o => o is DeviceRow d && $"{d.Name} {d.InstanceId} {d.Provider} {d.Health}".Contains(filter, StringComparison.OrdinalIgnoreCase)));
        tabs.Items.Add(new TabItem { Header = T("Устройства", "Devices"), Content = deviceWork });

        var wuWork = Workspace(); var wuTop = new StackPanel(); var wuButtons = Row();
        var wuGrid = Table(("Title", "Title", 360), ("Provider", "Provider", 140), ("Class", "DeviceClass", 110), ("Hardware ID", "HardwareId", 260), ("Revision", "Revision", 85), ("Match", "Match", 340)); wuGrid.ItemsSource = driverCandidates;
        wuButtons.Children.Add(Action(T("Найти обновления", "Find updates"), async () =>
        {
            if (DriverOperationActive) throw new InvalidOperationException(T("Установка драйвера уже выполняется.", "Driver installation is already running."));
            Report(T("Поиск в настроенном источнике Windows Update…", "Searching the configured Windows Update service…"));
            driverCandidates = await Task.Run(() => DriverRecommendations.SearchWindowsUpdate(devices)); wuGrid.ItemsSource = driverCandidates;
            Report(T($"Найдено {driverCandidates.Count}. Пустой результат не исключает драйверы у OEM.", $"Found {driverCandidates.Count}. Empty results do not rule out OEM drivers."));
        }));
        wuButtons.Children.Add(Action(T("Установить выбранное…", "Install selected…"), async () =>
        {
            if (wuGrid.SelectedItem is not DriverCandidate candidate) throw new InvalidOperationException(T("Выберите обновление.", "Choose an update."));
            DriverInstallPolicy.ValidateCandidate(candidate);
            string preview = $"{candidate.Title}\n{candidate.Provider} · {candidate.DeviceClass}\n{candidate.HardwareId}\nUpdateID {candidate.UpdateId} / revision {candidate.Revision}\n{candidate.Service}\n\n{candidate.Match}\n\n" + InstallNotice() + "\n\nEULA:\n" + candidate.Eula;
            if (!DriverConsent(preview)) return;
            await RunDriverInstall(new("InstallWindowsUpdate", true, true, candidate));
        }, true));
        wuButtons.Children.Add(Action("Windows Update", () => OpenFixed("ms-settings:windowsupdate")));
        wuTop.Children.Add(wuButtons); wuTop.Children.Add(Text(T("Windows проверит совместимость и загрузит выбранный драйвер. Перед установкой вы сможете просмотреть сведения и подтвердить действие. Обновления BIOS и прошивок не предлагаются.", "Windows checks compatibility and downloads the selected driver. Review the details and confirm before installation. BIOS and firmware updates are excluded."), 12));
        DockPanel.SetDock(wuTop, Dock.Top); wuWork.Children.Add(wuTop); wuWork.Children.Add(wuGrid); tabs.Items.Add(new TabItem { Header = "Windows Update", Content = wuWork });

        var infWork = Workspace(); var infTop = new StackPanel(); var infButtons = Row(); var packageNote = Text(T("Выберите распакованный пакет с INF и CAT. EXE/MSI и архивы здесь не выполняются.", "Choose an extracted package containing INF/CAT. EXE/MSI and archives are not executed here."), 13);
        var infGrid = Table(("Device", "DeviceName", 260), ("Instance", "InstanceId", 350), ("Provider", "Provider", 150), ("Version", "Version", 130), ("Windows rank", "Rank", 120)); infGrid.ItemsSource = inspectedPackage?.Matches;
        infButtons.Children.Add(Action(T("Проверить INF…", "Inspect INF…"), async () =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Driver INF|*.inf" }; if (picker.ShowDialog(this) != true) return;
            inspectedPackage = null; infGrid.ItemsSource = null;
            inspectedPackage = await Task.Run(() => InfPackages.Inspect(picker.FileName, devices.Count > 0 ? devices : DeviceInventory.Read()));
            infGrid.ItemsSource = inspectedPackage.Matches; packageNote.Text = $"{inspectedPackage.Inf}\nClass: {inspectedPackage.DeviceClass}; signer: {inspectedPackage.Signer}; catalog: {inspectedPackage.Catalog}\n{inspectedPackage.Files.Count} files / {inspectedPackage.Files.Sum(f => f.Length) / 1048576d:F1} MiB. Windows-compatible nodes; lower rank is better. Source: user-provided file, acquisition origin not verified.";
        }));
        infButtons.Children.Add(Action(T("Установить проверенный INF…", "Install inspected INF…"), async () =>
        {
            var package = inspectedPackage ?? throw new InvalidOperationException(T("Сначала проверьте INF.", "Inspect an INF first."));
            string preview = $"{package.Directory}\\{package.Inf}\nSigner: {package.Signer}\n{package.Files.Count} files / {package.Files.Sum(f => f.Length) / 1048576d:F1} MiB\n" + string.Join("\n", package.Matches.Select(m => $"{m.DeviceName} · {m.Version} · rank {m.Rank}")) + "\n\n" + InstallNotice() + "\n\nWindows may retain a better-ranked driver. No /force, /subdirs or /reboot. Local boot/storage/system/firmware classes are unsupported. Original download provenance is not proven by a signature.";
            if (!DriverConsent(preview)) return; await RunDriverInstall(new("InstallInf", true, false, Package: package));
        }, true));
        infTop.Children.Add(infButtons); infTop.Children.Add(packageNote); DockPanel.SetDock(infTop, Dock.Top); infWork.Children.Add(infTop); infWork.Children.Add(infGrid); tabs.Items.Add(new TabItem { Header = T("Локальный INF", "Local INF"), Content = infWork });

        sourceButtons.Children.Add(Action(T("Все источники", "All sources"), () => sourceGrid.ItemsSource = DriverSources.Catalog));
        sourceButtons.Children.Add(Action(T("Открыть официальный сайт", "Open official site"), () =>
        {
            if (sourceGrid.SelectedItem is not DriverSource source || !DriverSources.Trusted(source.Url)) throw new InvalidOperationException("Select an official source.");
            Process.Start(new ProcessStartInfo(source.Url.AbsoluteUri) { UseShellExecute = true });
        }));
        sourceTop.Children.Add(sourceButtons); sourceTop.Children.Add(Text(T("Сначала проверьте сайт производителя компьютера или платы, затем производителя компонента. Выбирайте драйвер для своей модели и версии Windows. Ссылка на сайт ещё не означает, что совместимый драйвер найден.", "Check your computer or motherboard manufacturer first, then the component manufacturer. Choose a driver for your exact model and Windows version. A support link alone does not establish compatibility."), 13));
        var download = new TextBox { ToolTip = T("Прямая HTTPS-ссылка официального CDN", "Direct official CDN HTTPS URL"), MinWidth = 320, Margin = new(0, 4, 12, 8) };
        var downloadRow = new DockPanel(); var fetch = Action(T("Загрузить файл…", "Download file…"), async () =>
        {
            if (!Uri.TryCreate(download.Text.Trim(), UriKind.Absolute, out var url) || !DriverSources.DownloadAllowed(url)) throw new InvalidDataException(T("Нужна прямая ссылка разрешённого официального CDN; при ограничениях используйте сайт производителя.", "Use a direct allowed official CDN URL; use the vendor site if restricted."));
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = T("Пустая папка загрузки", "Empty download folder") }; if (picker.ShowDialog(this) != true) return;
            var result = await DriverDownloads.Fetch(url, picker.FolderName);
            Report($"Downloaded {result.Bytes} bytes; SHA256 {result.Sha256}. No execution. HTTPS origin recorded; hash is not an official vendor checksum. Extract archives manually, then inspect INF. OEM installers remain a separate vendor-guided action.");
        }); DockPanel.SetDock(fetch, Dock.Right); downloadRow.Children.Add(fetch); downloadRow.Children.Add(download); sourceTop.Children.Add(downloadRow);
        DockPanel.SetDock(sourceTop, Dock.Top); sourceWork.Children.Add(sourceTop); sourceWork.Children.Add(sourceGrid); tabs.Items.Add(new TabItem { Header = T("Другие источники", "Other sources"), Content = sourceWork });
        return tabs;
    }
    private string InstallNotice() => T("Windows запросит UAC. Сначала экспортируются сторонние INF-пакеты; это не гарантирует откат. Установка может временно отключить экран, сеть или звук — закройте игры. Возможна перезагрузка; приложение не запускает её автоматически. Выполняется только выбранное обновление/INF, но Windows может применить его к нескольким подходящим устройствам. Журнал: ProgramData/Serein-DriverOperations.", "Windows will request UAC. Third-party INF packages are exported first; backup does not guarantee rollback. Display/network/audio can be interrupted; close games. Reboot may be required, never automatic. Only the selected update/INF is submitted, but Windows can apply it to multiple matching devices. Reports: ProgramData/Serein-DriverOperations.");
    private bool DriverConsent(string preview)
    {
        var dialog = new Window { Owner = this, Title = T("Проверка перед установкой драйвера", "Review driver installation"), Width = Math.Min(800, SystemParameters.WorkArea.Width - 40), Height = Math.Min(650, SystemParameters.WorkArea.Height - 40), ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (System.Windows.Media.Brush)FindResource("CanvasBrush"), Foreground = (System.Windows.Media.Brush)FindResource("TextBrush") };
        var panel = new DockPanel { Margin = new(20) }; var footer = new StackPanel(); var accept = new CheckBox { Content = T("Я принимаю условия лицензии и подтверждаю эту установку", "I accept the license terms and confirm this installation") }; var install = new System.Windows.Controls.Button { Content = T("Разрешить установку", "Allow installation"), IsEnabled = false, Margin = new(0, 12, 0, 0) };
        accept.Checked += (_, _) => install.IsEnabled = true; accept.Unchecked += (_, _) => install.IsEnabled = false; install.Click += (_, _) => dialog.DialogResult = true;
        footer.Children.Add(accept); footer.Children.Add(install); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        panel.Children.Add(new TextBox { Text = preview, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 0, 0, 16) }); dialog.Content = panel; return dialog.ShowDialog() == true;
    }
    private async Task RunDriverInstall(DriverInstallRequest request)
    {
        if (DriverOperationActive) throw new InvalidOperationException("Another driver operation is running.");
        if (armed || changes.Any(c => c.Status != ChangeStatus.Reverted) || powerLease is { Status: not ChangeStatus.Reverted } || PowerLeaseController.Pending(Path.Combine(data, "sessions")).Count > 0) throw new InvalidOperationException(T("Сначала остановите игровой режим и нажмите «Восстановить всё».", "Disarm gaming and Restore All before driver installation."));
        driverInstalling = true; Report(T("Проверка, резервная копия и установка… Не прерывайте операцию.", "Verifying, backing up and installing… Do not interrupt."));
        try
        {
            var reply = await DriverAdminClient.Install(Path.Combine(AppContext.BaseDirectory, "Serein.Admin.exe"), request);
            audit.Write($"Driver install {reply.State}; reboot={reply.RebootRequired}; report={reply.ReportPath}");
            System.Windows.MessageBox.Show($"{reply.State}\n{reply.Message}\nReboot required: {reply.RebootRequired}\n{reply.ReportPath}", "Serein · driver result");
            Report(reply.Success ? T("Операция завершена. Пересканируйте устройства; при необходимости перезагрузитесь самостоятельно.", "Operation completed. Rescan devices; restart manually if required.") : T("Операция не подтверждена как успешная. Проверьте журнал; частичные изменения возможны.", "Operation not confirmed successful. Review report; partial changes may exist."));
        }
        finally { driverInstalling = false; }
    }
}
