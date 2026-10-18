using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Serein.Core;
using Forms = System.Windows.Forms;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;

namespace Serein.Desktop;

public partial class MainWindow : Window
{
    private static readonly string[] Pages = ["Dashboard", "Optimize", "Gaming", "Processes", "Services", "Startup", "Drivers", "Cleanup", "Monitoring", "Profiles", "Settings"];
    private static readonly string[] RussianPages = ["Обзор", "Оптимизация", "Игровая сессия", "Процессы", "Службы", "Автозагрузка", "Драйверы", "Очистка", "Мониторинг", "Профили", "Настройки"];
    private readonly string data;
    private readonly ProfileStore profileStore;
    private readonly AuditLog audit;
    private readonly OperationClient operations;
    private readonly TelemetrySampler telemetry = new();
    private GpuTelemetryClient gpuTelemetry;
    private CpuTelemetryClient cpuTelemetry;
    private readonly ProcessInventory processInventory = new();
    private readonly StartupManager startup = new();
    private readonly GameSessionTracker tracker = new();
    private readonly DispatcherTimer timer = new();
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon appIcon;
    private List<Profile> profiles;
    private Profile selectedProfile;
    private List<ProcessRow> processRows = [];
    private List<DeviceRow> devices = [];
    private List<Change> changes = [];
    private readonly List<MetricSample> samples = [];
    private MetricSample? latest;
    private bool polling, closing, armed, english, light, suppressUntilGameExit;
    private string page = "Dashboard", filter = "";
    private DataGrid? table;
    private TrendChart? chart;
    private TextBlock? metrics, sessionLabel, frozenLabel;
    private DateTimeOffset sessionStart;
    private readonly List<MetricSample> sessionSamples = [];
    private List<CleanupSelection> cleanupRows = [];
    private CleanupService? cleaner;
    private readonly List<string> recent = [];
    private int tick;
    private string hardwareSummary = "";
    private Preferences preferences = new();
    private bool startupReady;
    private int applicationExitCode;
    private readonly Func<Task> initializeInBackground;
    public Task InitializeBackground() => initializeInBackground();
    private string T(string ru, string en) => english ? en : ru;
    private string Label(string key) => english ? key : RussianPages[Array.IndexOf(Pages, key)];

    public MainWindow()
    {
        InitializeComponent();
        var args = Environment.GetCommandLineArgs(); int smoke = Array.IndexOf(args, "--smoke"), resourceCheck = Array.IndexOf(args, "--resource-check");
        if (args.Contains("--tray")) ShowActivated = false;
        int isolation = smoke >= 0 ? smoke : resourceCheck >= 0 ? resourceCheck : Array.IndexOf(args, "--test-data");
        data = isolation >= 0 && isolation + 1 < args.Length ? Path.Combine(Path.GetFullPath(args[isolation + 1]), "state") : AppPaths.Data;
        Directory.CreateDirectory(data); audit = new(data); profileStore = new(data);
        try { if (File.Exists(Path.Combine(data, "preferences.json"))) { preferences = JsonFiles.Read<Preferences>(Path.Combine(data, "preferences.json")); preferences.Validate(); } }
        catch (Exception ex) { preferences = new(); audit.Write("Preferences unavailable: " + ex.Message); }
        english = preferences.English; light = preferences.LightTheme; ApplyAppearance(); ConfigureChrome();
        try { profiles = profileStore.Load(); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
        { profiles = Profile.Presets; audit.Write("Profiles could not be loaded; original file retained: " + ex.Message); System.Windows.MessageBox.Show(ex.Message, "Serein profiles"); }
        selectedProfile = profiles[0];
        operations = new(Path.Combine(AppContext.BaseDirectory, "Serein.Host.exe"), Path.Combine(data, "sessions"));
        gpuTelemetry = new(Path.Combine(AppContext.BaseDirectory, "Serein.Measure.exe"));
        cpuTelemetry = new(Path.Combine(AppContext.BaseDirectory, "Serein.Measure.exe"));
        using (var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Serein.ico")).Stream) appIcon = new System.Drawing.Icon(iconStream);
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Serein.ico"));
        tray = new Forms.NotifyIcon { Text = "Serein · Balanced", Icon = appIcon, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);
        // Keep NotifyIcon's supported mouse/keyboard context-menu callback path.
        // Cancel its native surface and open the themed WPF menu instead.
        var trayTrigger = new Forms.ContextMenuStrip();
        trayTrigger.Opening += (_, e) => { e.Cancel = true; Dispatcher.BeginInvoke(new System.Action(OpenTrayMenu)); };
        tray.ContextMenuStrip = trayTrigger;
        BuildTray(); BuildNavigation();
        timer.Interval = TimeSpan.FromSeconds(2); timer.Tick += async (_, _) => await Poll();
        Task? startupTask = null;
        async Task Initialize()
        {
            var recovery = await Task.Run(() => Recovery.Reconcile(Path.Combine(data, "sessions")));
            foreach (var result in recovery) Report(result);
            var sessions = Path.Combine(data, "sessions");
            try { powerLease = PowerLeaseController.Pending(sessions).FirstOrDefault(); }
            catch (Exception ex) { Report("Power recovery journal requires review: " + ex.Message); }
            if (Directory.Exists(sessions)) foreach (var path in Directory.EnumerateFiles(sessions, "*.json"))
            {
                try { changes.AddRange(JsonFiles.Read<Journal>(path).Changes.Where(c => c.Status != ChangeStatus.Reverted)); }
                catch (Exception ex) { Report("Recovery journal requires review: " + ex.Message); }
            }
            Microsoft.Win32.SystemEvents.PowerModeChanged += PowerChanged;
            Microsoft.Win32.SystemEvents.SessionEnding += SessionEnding;
            await Poll();
            try
            {
                var hardware = await Task.Run(() => (Cpu: string.Join(", ", Wmi.Query("SELECT Name FROM Win32_Processor").Select(r => r.Text("Name"))), Gpu: string.Join(", ", Wmi.Query("SELECT Name FROM Win32_VideoController").Select(r => r.Text("Name")))));
                processorName = hardware.Cpu; graphicsName = hardware.Gpu; hardwareSummary = hardware.Cpu + " / " + hardware.Gpu;
                if (page == "Dashboard") ShowPage("Dashboard");
            }
            catch (Exception ex) { Report("Hardware summary unavailable: " + ex.Message); }
            if (args.Contains("--cpu-admin"))
            {
                new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle(); ConnectCpuSensors();
            }
            if (args.Contains("--tray")) Hide();
            startupReady = true;
            if (smoke >= 0 && smoke + 1 < args.Length) await Smoke(Path.GetFullPath(args[smoke + 1]));
            else { timer.Start(); if (resourceCheck >= 0 && resourceCheck + 1 < args.Length) await ResourceCheck(Path.GetFullPath(args[resourceCheck + 1])); }
        }
        Task StartOnce() => startupTask ??= Initialize();
        initializeInBackground = StartOnce;
        Loaded += async (_, _) => await StartOnce();
        StateChanged += (_, _) => { if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; if (WindowState == WindowState.Minimized) { Hide(); WindowState = WindowState.Normal; } };
        IsVisibleChanged += (_, _) => timer.Interval = TimeSpan.FromSeconds(IsVisible ? 2 : 5);
        Closing += OnClosing;
    }
    private void BuildNavigation()
    {
        var selected = page; Navigation.Items.Clear();
        string[] glyphs = ["\uE80F", "\uE945", "\uE7FC", "\uE9D9", "\uE713", "\uE7E8", "\uE964", "\uE74D", "\uE9D2", "\uE8A5", "\uE115"];
        for (int i = 0; i < Pages.Length; i++)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = glyphs[i], FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 16, Width = 29, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Transparent });
            ((TextBlock)content.Children[0]).SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) });
            content.Children.Add(new TextBlock { Text = Label(Pages[i]), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            ((TextBlock)content.Children[1]).SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) });
            Navigation.Items.Add(new ListBoxItem { Content = content, Tag = Pages[i], ToolTip = Label(Pages[i]) });
        }
        Navigation.SelectedIndex = Array.IndexOf(Pages, selected);
        RestoreButton.Content = T("Восстановить всё", "Restore all");
        ProfileCaption.Text = T("ТЕКУЩИЙ ПРОФИЛЬ", "CURRENT PROFILE");
        SidebarCaption.Text = T("ОПТИМИЗАЦИЯ И МОНИТОРИНГ", "PERFORMANCE WORKSPACE");
    }
    private void ShowWindow() { if (trayMenu is { IsOpen: true }) trayMenu.IsOpen = false; Show(); WindowState = WindowState.Normal; Activate(); }
    private void Navigate(object sender, SelectionChangedEventArgs e)
    {
        if (Navigation.SelectedItem is ListBoxItem item && item.Tag is string key) ShowPage(key);
    }
    private void ShowPage(string key)
    {
        page = key; table = null; chart = null; metrics = null; sessionLabel = null; frozenLabel = null; filter = ""; ResetDashboard();
        PageTitle.Text = Label(key);
        PageSubtitle.Text = key switch
        {
            "Dashboard" => T("Ресурсы компьютера и состояние игровой сессии.", "Computer resources and game session status."),
            "Optimize" => T("Управление фоновыми приложениями и питанием.", "Background apps and power settings."),
            "Gaming" => T("Автоматическая оптимизация на время игры и восстановление после выхода.", "Automatic optimization while playing, with restoration when the game closes."),
            "Processes" => T("Ручное управление приложениями. Автоматический режим настраивается в разделе «Игровая сессия».", "Manual app controls. Configure automatic management in Game session."),
            "Services" => T("Службы Windows и их состояние.", "Windows services and their status."),
            "Startup" => T("Приложения, запускаемые при входе в Windows.", "Apps that start when you sign in to Windows."),
            "Drivers" => T("Устройства, обновления и резервные копии драйверов.", "Devices, driver updates and backups."),
            "Cleanup" => T("Просмотрите временные файлы и выберите, что удалить.", "Review temporary files and choose what to remove."),
            "Monitoring" => T("Нагрузка, память и температура в реальном времени.", "Live usage, memory and temperature."),
            "Profiles" => T("Сохранённые настройки для работы и игр.", "Saved settings for work and gaming."),
            _ => T("Внешний вид и поведение приложения.", "Appearance and app behavior.")
        };
        PageBody.Content = key switch
        {
            "Dashboard" => Dashboard(), "Optimize" => Optimize(), "Gaming" => Gaming(), "Processes" => Processes(), "Services" => Services(), "Startup" => Startup(), "Drivers" => Drivers(), "Cleanup" => Cleanup(), "Monitoring" => Monitoring(), "Profiles" => Profiles(), _ => Settings()
        };
        if (!preferences.ReducedMotion && SystemParameters.ClientAreaAnimation) PageBody.BeginAnimation(OpacityProperty, new DoubleAnimation(.6, 1, TimeSpan.FromMilliseconds(150)));
        else PageBody.BeginAnimation(OpacityProperty, null);
        UpdateMetrics();
    }
    private static DockPanel Workspace() => new() { LastChildFill = true };
    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal };
    private StackPanel Notes(params string[] lines)
    {
        var panel = new StackPanel();
        foreach (var line in lines) panel.Children.Add(Text(line, 14, 12));
        return panel;
    }
    private TextBlock Text(string text, double size = 14, double bottom = 8) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, bottom) };
    private Button Action(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = text };
        if (primary) button.Style = (Style)FindResource("PrimaryButtonStyle");
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception ex) { Report(ex.Message); ShowNotice(ex.Message); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }
    private Button Action(string text, System.Action action) => Action(text, () => { action(); return Task.CompletedTask; });
    private DataGrid Table(params (string Header, string Binding, int Width)[] columns)
    {
        var grid = new DataGrid();
        foreach (var c in columns) grid.Columns.Add(new DataGridTextColumn { Header = DisplayTextConverter.Header(c.Header, english), Binding = new Binding(c.Binding) { Converter = new DisplayTextConverter(english), ConverterParameter = c.Binding }, Width = c.Width });
        table = grid; return grid;
    }
    private FrameworkElement Searchable(DataGrid grid, Func<object, bool> predicate)
    {
        var work = Workspace();
        var search = new TextBox { ToolTip = T("Поиск по списку", "Search this list"), MaxLength = 256 };
        search.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Search");
        search.TextChanged += (_, _) =>
        {
            filter = search.Text; var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
            if (view is not null) { view.Filter = o => predicate(o); view.Refresh(); }
        };
        DockPanel.SetDock(search, Dock.Top); work.Children.Add(search); work.Children.Add(grid); return work;
    }
    private void SelectPage(string key) { Navigation.SelectedIndex = Array.IndexOf(Pages, key); if (page != key) ShowPage(key); }
    private FrameworkElement Processes()
    {
        var work = Workspace(); var actions = Row();
        var grid = Table((T("Приложение", "App"), "Name", 160), ("ID", "Pid", 65), (T("Родитель", "Parent"), "ParentPid", 75), ("CPU", "Cpu", 75), (T("Память, МБ", "Memory, MB"), "WorkingSetMb", 95), (T("Доступность", "Availability"), "Classification", 180), (T("Приоритет", "Priority"), "Priority", 100), (T("Расположение", "Location"), "Path", 380));
        grid.ItemsSource = processRows;
        actions.Children.Add(Action(T("Обновить", "Refresh"), async () => { await ReadProcesses(); grid.ItemsSource = processRows; }));
        actions.Children.Add(Action(T("Разрешить управление", "Allow management"), () => ApproveSelected(grid)));
        actions.Children.Add(Action(T("Заморозить", "Freeze"), async () => await ApplySelected(grid, "Freeze"), true));
        actions.Children.Add(Action(T("Снизить приоритет", "Lower priority"), async () => await ApplySelected(grid, "Priority")));
        actions.Children.Add(Action(T("Возобновить", "Resume"), async () => { if (grid.SelectedItem is ProcessRow r) await Send(new("Restore", r.Identity)); }));
        actions.Children.Add(Action(T("Сведения", "Details"), async () =>
        {
            if (grid.SelectedItem is not ProcessRow p || string.IsNullOrWhiteSpace(p.Path)) throw new InvalidOperationException(T("Выберите доступный процесс.", "Choose an accessible process."));
            var signature = await Task.Run(() => SignatureVerifier.Read(p.Path));
            bool verified = signature.Status.StartsWith("Verified", StringComparison.Ordinal);
            CreateActionDialog(T("Сведения о приложении", "Application details"), T($"{p.Name}\n{p.Path}\n\nID процесса: {p.Pid}\nЗапущено: {DateTime.FromFileTime(p.Identity.CreatedFileTime):g}\nИспользует память: {p.WorkingSetMb:F1} МБ\nИздатель: {signature.Publisher}\nПодпись: {(verified ? "проверена" : "не подтверждена")}\n\n{(p.Classification is "CRITICAL" or "PROTECTED" or "GAME_DEPENDENCY" ? "Приложение защищено от изменений." : p.CanApprove ? "Можно разрешить управление этим приложением." : "Управление недоступно: не удалось проверить безопасность процесса.")}", $"{p.Name}\n{p.Path}\n\nProcess ID: {p.Pid}\nStarted: {DateTime.FromFileTime(p.Identity.CreatedFileTime):g}\nMemory: {p.WorkingSetMb:F1} MB\nPublisher: {signature.Publisher}\nSignature: {(verified ? "verified" : "not verified")}"), false).ShowDialog();
        }));
        DockPanel.SetDock(actions, Dock.Top); work.Children.Add(actions);
        work.Children.Add(Searchable(grid, o => o is ProcessRow r && $"{r.Name} {r.Pid} {r.Path} {r.Classification}".Contains(filter, StringComparison.OrdinalIgnoreCase)));
        return work;
    }
    private void ApproveSelected(DataGrid grid)
    {
        if (grid.SelectedItem is not ProcessRow row || row.Path.Length == 0) throw new InvalidOperationException(T("Выберите доступный процесс.", "Choose an accessible process."));
        if (row.Classification is "CRITICAL" or "PROTECTED" or "GAME_DEPENDENCY") throw new InvalidOperationException(T("Это приложение защищено: управление может нарушить работу Windows или игры.", "This app is protected because changing it could affect Windows or your game."));
        if (!Confirm(T($"Разрешить управление приложением {row.Name}?\n{row.Path}\n\nЗаморозка приостановит его работу, включая загрузки и сохранение файлов. Выбирайте только ненужные на время игры приложения. Игра и системные компоненты останутся защищены.", $"Allow management of {row.Name}?\n{row.Path}\n\nFreezing pauses its work, including downloads and file saves. Choose only apps you do not need while playing. Your game and system components stay protected."))) return;
        var approval = JsonFiles.Approve(row.Path);
        var allowed = selectedProfile.AllowedApps.Where(a => !a.Path.Equals(row.Path, StringComparison.OrdinalIgnoreCase)).Append(approval).ToList();
        ReplaceProfile(selectedProfile with { AllowedApps = allowed }); Report(T("Управление приложением разрешено для этого профиля.", "App management allowed for this profile."));
    }
    private async Task ApplySelected(DataGrid grid, string kind)
    {
        if (grid.SelectedItem is not ProcessRow row) throw new InvalidOperationException(T("Выберите процесс.", "Choose a process."));
        var approval = selectedProfile.AllowedApps.FirstOrDefault(a => a.Path.Equals(row.Path, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException(T("Сначала нажмите «Разрешить управление».", "Choose Allow management first."));
        string action = kind == "Freeze" ? T("Заморозить", "Freeze") : T("Снизить приоритет", "Lower priority");
        if (!Confirm(T($"{action}: {row.Name}?\nВосстановление не позднее чем через {selectedProfile.DeadlineSeconds} с.\n\nЗаморозка приостановит загрузки и сохранение файлов. Не выбирайте приложение, нужное для текущей работы или игры.", $"{action}: {row.Name}?\nRestore within {selectedProfile.DeadlineSeconds}s.\n\nFreezing pauses downloads and file saves. Do not choose an app needed for your current work or game."))) return;
        await Send(new(kind, row.Identity, approval, selectedProfile.DeadlineSeconds, tracker.Roots.ToArray()));
    }
    private string SessionText() => tracker.Active ? T("Игровая сессия активна", "Game session active") : armed ? T("Ожидаем запуск игры", "Waiting for game launch") : T("Игровая сессия не запущена", "No game session running");
    private void ChooseGame()
    {
        if (armed) throw new InvalidOperationException(T("Сначала остановите наблюдение.", "Disarm monitoring first."));
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Executable (*.exe)|*.exe" };
        if (picker.ShowDialog(this) == true) { ReplaceProfile(selectedProfile with { GamePath = Path.GetFullPath(picker.FileName) }); ShowPage("Gaming"); }
    }
    private async Task Arm()
    {
        if (DriverOperationActive) throw new InvalidOperationException(T("Дождитесь завершения установки драйвера.", "Wait for driver installation to finish."));
        if (selectedProfile.GamePath is null || !File.Exists(selectedProfile.GamePath)) throw new InvalidOperationException(T("Выберите файл запуска игры.", "Choose the game's launch file."));
        if (selectedProfile.Mode is not (ProfileMode.Gaming or ProfileMode.ExtremeGaming)) throw new InvalidOperationException(T("Для сессии выберите Gaming или ExtremeGaming.", "Choose Gaming or ExtremeGaming for a session."));
        string targets = string.Join("\n", selectedProfile.AllowedApps.Select(a => a.Path));
        if (targets.Length == 0) targets = T("Приложения не выбраны", "No apps selected");
        if (selectedProfile.PowerScheme is not null) targets += T("\nБудет применена сохранённая схема питания.", "\nThe saved power plan will be applied.");
        if (!Confirm(T($"Включить автоматический режим для {Path.GetFileNameWithoutExtension(selectedProfile.GamePath)}?\n\nПрофиль: {selectedProfile.Name}\nВыбранные приложения:\n{targets}\n\nИзменения начнутся при запуске игры. После выхода или не позднее чем через {selectedProfile.DeadlineSeconds} с приложения будут восстановлены. Заморозка приостанавливает загрузки и сохранение файлов.", $"Enable automatic management for {Path.GetFileNameWithoutExtension(selectedProfile.GamePath)}?\n\nProfile: {selectedProfile.Name}\nChosen apps:\n{targets}\n\nChanges begin when the game starts. Apps are restored when it closes or within {selectedProfile.DeadlineSeconds}s. Freezing pauses downloads and file saves."))) return;
        await RestoreAll(); tracker.Reset(); suppressUntilGameExit = false; armed = true; Report(T("Автоматический режим включён. Ожидаем игру.", "Automatic management enabled. Waiting for your game.")); ShowPage("Gaming");
    }
    private FrameworkElement Services()
    {
        var work = Workspace(); var row = Row();
        var grid = Table((T("Название", "Name"), "DisplayName", 250), ("Service", "Name", 180), ("State", "State", 95), ("Start", "StartMode", 95), ("PID", "Pid", 65), ("Account", "Account", 180));
        row.Children.Add(Action(T("Сканировать службы", "Scan services"), async () => { grid.ItemsSource = await Task.Run(ServiceInventory.Read); Report(T("Службы прочитаны.", "Services read.")); }));
        row.Children.Add(Action(T("Открыть службы Windows", "Open Windows Services"), () => OpenFixed("services.msc")));
        row.Children.Add(Action(T("Зависимости", "Dependencies"), async () =>
        {
            if (grid.SelectedItem is not ServiceRow service) throw new InvalidOperationException(T("Выберите службу.", "Choose a service."));
            var details = await Task.Run(() => ServiceInspector.Read(service.Name));
            CreateActionDialog(T("Сведения о службе", "Service details"), T($"{service.DisplayName}\nФайл: {details.BinaryPath}\nУчётная запись: {details.Account}\nТип запуска: {details.StartType}\nЗависит от: {string.Join(", ", details.Dependencies)}\nЗависят от неё: {string.Join(", ", details.Dependents)}", $"{service.DisplayName}\nFile: {details.BinaryPath}\nAccount: {details.Account}\nStart type: {details.StartType}\nDependencies: {string.Join(", ", details.Dependencies)}\nDependents: {string.Join(", ", details.Dependents)}"), false).ShowDialog();
        }));
        DockPanel.SetDock(row, Dock.Top); work.Children.Add(row); work.Children.Add(Searchable(grid, o => o is ServiceRow s && $"{s.Name} {s.DisplayName} {s.State}".Contains(filter, StringComparison.OrdinalIgnoreCase))); return work;
    }
    private FrameworkElement Startup()
    {
        var work = Workspace(); var row = Row();
        var grid = Table((T("Название", "Name"), "Name", 160), ("Enabled", "Enabled", 75), ("Origin", "Origin", 170), ("Command", "Command", 500));
        row.Children.Add(Action(T("Сканировать", "Scan"), () => grid.ItemsSource = startup.Read()));
        row.Children.Add(Action(T("Включить / отключить", "Enable / disable"), () =>
        {
            if (grid.SelectedItem is not StartupRow entry) throw new InvalidOperationException(T("Выберите запись.", "Choose an entry."));
            if (Confirm(T($"Изменить автозагрузку {entry.Name}?\n{entry.Command}\nИсходная запись сохранится для восстановления.", $"Change startup {entry.Name}?\n{entry.Command}\nThe original entry will be saved for restoration."))) { startup.Toggle(entry); grid.ItemsSource = startup.Read(); Report(T("Автозагрузка изменена.", "Startup entry changed.")); }
        }));
        DockPanel.SetDock(row, Dock.Top); work.Children.Add(row); work.Children.Add(Searchable(grid, o => o is StartupRow s && $"{s.Name} {s.Command} {s.Origin}".Contains(filter, StringComparison.OrdinalIgnoreCase))); return work;
    }
    private FrameworkElement Cleanup()
    {
        var work = Workspace(); var row = Row(); var grid = Table(("Path", "File.Path", 520), ("Bytes", "File.Length", 100), ("Last write UTC", "File.LastWriteUtc", 170));
        grid.IsReadOnly = false;
        grid.Columns.Insert(0, new DataGridCheckBoxColumn { Header = T("Выбор", "Select"), Binding = new Binding("Selected"), Width = 60 });
        foreach (var column in grid.Columns.Skip(1)) column.IsReadOnly = true;
        grid.ItemsSource = cleanupRows;
        row.Children.Add(Action(T("Крупные файлы · просмотр", "Large files · read only"), async () =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = T("Выберите папку для анализа без удаления", "Choose folder for read-only analysis") };
            if (picker.ShowDialog(this) != true) return;
            var scan = await Task.Run(() => DiskAnalyzer.Read(picker.FolderName));
            ShowDataWindow(T("Крупные файлы · только просмотр", "Large files · read only"), scan.Files, ("Path", "Path", 620), ("MiB", "MiB", 90), ("Modified UTC", "ModifiedUtc", 180));
            Report(T($"Найдено {scan.ObservedBytes / 1048576d:F1} МБ · {(scan.Complete ? "анализ завершён" : "анализ частичный")} · сообщений: {scan.Errors.Count}", $"Found {scan.ObservedBytes / 1048576d:F1} MB · {(scan.Complete ? "complete" : "partial")} · {scan.Errors.Count} messages"));
            foreach (var error in scan.Errors) audit.Write("Disk analyzer: " + error);
        }));
        row.Children.Add(Action(T("Найти временные файлы", "Find temporary files"), async () =>
        {
            cleaner = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
            var result = await Task.Run(() => cleaner.Preview(TimeSpan.FromHours(48)));
            cleanupRows = result.Files.Select(f => new CleanupSelection(f)).ToList(); grid.ItemsSource = cleanupRows;
            Report(T($"{result.Files.Count} файлов · {result.Files.Sum(f => f.Length) / 1048576d:F1} MB · {result.Errors.Count} сообщений", $"{result.Files.Count} files · {result.Files.Sum(f => f.Length) / 1048576d:F1} MB · {result.Errors.Count} messages"));
            foreach (var error in result.Errors) audit.Write("Cleanup preview: " + error);
        }));
        row.Children.Add(Action(T("Удалить выбранное", "Delete selected"), async () =>
        {
            grid.CommitEdit(); grid.CommitEdit(DataGridEditingUnit.Row, true);
            var selected = cleanupRows.Where(r => r.Selected).ToList();
            if (cleaner is null || selected.Count == 0) throw new InvalidOperationException(T("Сначала выберите файлы из предпросмотра.", "Select files from a preview first."));
            if (!Confirm(T($"Удалить {selected.Count} файлов ({selected.Sum(r => r.File.Length) / 1048576d:F1} MB)? Удаление необратимо.", $"Delete {selected.Count} files ({selected.Sum(r => r.File.Length) / 1048576d:F1} MB)? Deletion is irreversible."))) return;
            var results = await Task.Run(() => selected.Select(s => cleaner.Delete(s.File)).ToList());
            foreach (var result in results) audit.Write($"Cleanup: {result.Path} {result.Deleted} {result.Message}");
            cleanupRows.RemoveAll(r => results.Any(result => result.Deleted && result.Path == r.File.Path)); grid.ItemsSource = cleanupRows.ToList();
            Report(T($"Удалено {results.Count(r => r.Deleted)} · пропущено {results.Count(r => !r.Deleted)} · {results.Sum(r => r.Bytes)} байт", $"Deleted {results.Count(r => r.Deleted)} · skipped {results.Count(r => !r.Deleted)} · {results.Sum(r => r.Bytes)} bytes"));
        }, true));
        DockPanel.SetDock(row, Dock.Top); work.Children.Add(row); work.Children.Add(grid); return work;
    }
    private FrameworkElement Monitoring()
    {
        var work = new StackPanel(); metrics = Text("…", 22, 24); work.Children.Add(metrics);
        work.Children.Add(SensorPanel());
        chart = new TrendChart { Height = 260, Samples = samples }; work.Children.Add(chart);
        work.Children.Add(Muted(T("Загрузка процессора и памяти · 0–100%", "Processor and memory usage · 0–100%"), 12, 20));
        work.Children.Add(Action(T("Экспорт CSV", "Export CSV"), () => ExportSamples(samples)));
        work.Children.Add(Action(T("Открыть запись FPS", "Open FPS recording"), async () =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "PresentMon CSV|*.csv" };
            if (picker.ShowDialog(this) != true) return;
            var frames = await Task.Run(() => FrameAccumulator.Read(picker.FileName));
            ShowDataWindow(T("Результаты записи FPS", "FPS recording results"), new List<FrameStatistics> { frames }, ("PID", "Pid", 70), ("Frames", "Intervals", 90), ("Average FPS", "AverageFps", 130), ("1% low FPS", "Low1PercentFps", 130), ("p95 ms", "P95Ms", 110), ("p99 ms", "P99Ms", 110), ("Swapchain", "SwapChain", 240));
            audit.Write(frames.Method);
        }));
        work.Children.Add(Muted(T("Сеть: общий входящий и исходящий трафик. При включённом VPN он может учитываться дважды. Диск: общая скорость чтения и записи. На системах с более чем 64 ядрами загрузка процессора может учитываться частично.", "Network: combined incoming and outgoing traffic; VPN traffic may be counted twice. Disk: combined reads and writes. On systems with over 64 cores, processor usage may cover only part of the CPU."), 11));
        return Scroll(work);
    }
    private FrameworkElement Profiles()
    {
        var work = Workspace(); var top = new StackPanel(); var row = Row();
        var choice = new ComboBox { ItemsSource = profiles, DisplayMemberPath = "Name", SelectedItem = selectedProfile, Width = 260, HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !armed };
        choice.SelectionChanged += (_, _) => { if (!armed && choice.SelectedItem is Profile p) { selectedProfile = p; ShowPage("Profiles"); } }; top.Children.Add(choice);
        var lease = new TextBox { Text = selectedProfile.DeadlineSeconds.ToString(), Width = 110, HorizontalAlignment = HorizontalAlignment.Left }; top.Children.Add(Text(T("Срок автоматического восстановления (секунды)", "Automatic restore deadline (seconds)"), 12)); top.Children.Add(lease);
        row.Children.Add(Action(T("Сохранить срок", "Save deadline"), () =>
        {
            if (armed) throw new InvalidOperationException(T("Сначала выключите автоматический режим.", "Turn off automatic management first."));
            if (!int.TryParse(lease.Text, out int seconds) || seconds is < 5 or > 1800) throw new InvalidDataException(T("Введите срок от 5 до 1800 секунд.", "Enter a deadline between 5 and 1800 seconds.")); ReplaceProfile(selectedProfile with { DeadlineSeconds = seconds }); Report(T("Профиль сохранён.", "Profile saved."));
        }));
        row.Children.Add(Action(T("Импорт JSON", "Import JSON"), () =>
        {
            if (armed) throw new InvalidOperationException(T("Сначала выключите автоматический режим.", "Turn off automatic management first."));
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json" };
            if (picker.ShowDialog(this) == true) { var p = profileStore.Import(picker.FileName); var next = profiles.Append(p).ToList(); profileStore.Save(next); profiles = next; selectedProfile = p; ShowPage("Profiles"); }
        }));
        row.Children.Add(Action(T("Экспорт JSON", "Export JSON"), () => { var picker = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = "serein-profile.json" }; if (picker.ShowDialog(this) == true) JsonFiles.Write(picker.FileName, selectedProfile); })); top.Children.Add(row);
        var grid = Table((T("Разрешённые приложения", "Allowed applications"), "Path", 750)); grid.ItemsSource = selectedProfile.AllowedApps;
        top.Children.Add(Action(T("Убрать разрешение", "Remove approval"), () => { if (armed) throw new InvalidOperationException(T("Сначала выключите автоматический режим.", "Turn off automatic management first.")); if (grid.SelectedItem is Approval a) { ReplaceProfile(selectedProfile with { AllowedApps = selectedProfile.AllowedApps.Where(x => x != a).ToList() }); grid.ItemsSource = selectedProfile.AllowedApps; } }));
        top.Children.Add(Muted(T("Gaming снижает приоритет выбранных фоновых приложений при запуске игры. Extreme Gaming временно приостанавливает их. Остальные профили используются для мониторинга и ручной настройки.", "Gaming lowers selected background apps' priority when your game starts. Extreme Gaming temporarily pauses them. Other profiles provide monitoring and manual controls."), 12, 16));
        DockPanel.SetDock(top, Dock.Top); work.Children.Add(top); work.Children.Add(grid); return work;
    }
    private void ReplaceProfile(Profile replacement)
    {
        if (armed) throw new InvalidOperationException(T("Сначала остановите наблюдение.", "Disarm before editing."));
        replacement.Validate(); int i = profiles.IndexOf(selectedProfile); var next = profiles.ToList(); next[i] = replacement; profileStore.Save(next); profiles = next; selectedProfile = replacement;
    }
    private void SavePreferences() { preferences = preferences with { English = english, LightTheme = light }; preferences.Validate(); JsonFiles.Write(Path.Combine(data, "preferences.json"), preferences); }
    private void OpenFixed(string target)
    {
        if (target is not ("services.msc" or "ms-settings:windowsupdate")) throw new InvalidOperationException("Unsupported system handoff.");
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
    private void ShowDataWindow<TItem>(string title, List<TItem> items, params (string Header, string Binding, int Width)[] columns)
    {
        var previous = table; var grid = Table(columns); table = previous; grid.ItemsSource = items;
        new Window { Title = title, Width = Math.Min(1000, SystemParameters.WorkArea.Width - 40), Height = Math.Min(500, SystemParameters.WorkArea.Height - 40), Owner = this, Content = grid, WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show();
    }
    private async Task ReadProcesses()
    {
        var approvals = selectedProfile.AllowedApps.ToArray(); var roots = tracker.Roots;
        processRows = (await Task.Run(() => processInventory.Read(approvals, roots))).ToList();
    }
    private async Task Poll()
    {
        if (polling || closing) return; polling = true;
        try
        {
            latest = await Task.Run(telemetry.Sample); samples.Add(latest); if (samples.Count > 300) samples.RemoveAt(0);
            if (tracker.Active && sessionSamples.Count < 3600) sessionSamples.Add(latest);
            if (armed || page is "Processes" or "Gaming" || tick++ % 3 == 0)
            {
                await ReadProcesses();
                if (page == "Processes" && table is not null)
                {
                    var sorts = CollectionViewSource.GetDefaultView(table.ItemsSource)?.SortDescriptions.ToArray() ?? [];
                    int? selected = (table.SelectedItem as ProcessRow)?.Pid; table.ItemsSource = processRows;
                    var view = CollectionViewSource.GetDefaultView(table.ItemsSource);
                    view.Filter = o => o is ProcessRow r && $"{r.Name} {r.Path} {r.Pid} {r.Classification}".Contains(filter, StringComparison.OrdinalIgnoreCase);
                    foreach (var sort in sorts) view.SortDescriptions.Add(sort);
                    if (selected is int pid) table.SelectedItem = processRows.FirstOrDefault(r => r.Pid == pid);
                }
            }
            if (armed)
            {
                var transition = tracker.Update(selectedProfile.GamePath, processRows);
                if (transition.Started)
                {
                    sessionStart = DateTimeOffset.UtcNow; sessionSamples.Clear(); sessionSamples.Add(latest);
                    if (!suppressUntilGameExit)
                    {
                        await ApplyGameProfile();
                        Notify(T("Игра обнаружена; разрешённые операции обработаны.", "Game detected; approved operations processed."));
                    }
                }
                if (transition.Ended)
                {
                    await RestoreAll(false); SaveSession(); suppressUntilGameExit = false; Notify(T("Игра закрыта; восстановление выполнено.", "Game closed; restoration attempted."));
                }
            }
            if (operations.HostPid is not null)
            {
                var result = await operations.Send(new("Status")); changes = result.Changes; powerLease = result.Power;
                if (page == "Gaming" && table is not null) table.ItemsSource = SessionChanges();
                if (!result.Success) Report(result.Message);
            }
            UpdateMetrics();
            if (sessionLabel is not null) sessionLabel.Text = SessionText();
            if (recent.Count == 0) StatusLine.Text = T($"Обновлено {latest.Time.ToLocalTime():HH:mm:ss} · приложений и процессов: {processRows.Count}", $"Updated {latest.Time.ToLocalTime():HH:mm:ss} · {processRows.Count} apps and processes");
        }
        catch (Exception ex) { Report(ex.Message); }
        finally { polling = false; }
    }
    private void UpdateMetrics()
    {
        if (latest is not null && metrics is not null)
            metrics.Text = $"{T("Процессор", "CPU")}  {(latest.CpuPercent is double c ? c.ToString("F1") + "%" : "—")}      {T("Память", "RAM")}  {latest.MemoryPercent:F1}%\n{T("Доступно", "Available")} {latest.AvailableMemory / 1073741824d:F1} / {latest.TotalMemory / 1073741824d:F1} {T("ГБ", "GB")}     {T("Сеть", "Network")}  {latest.NetworkBytesPerSecond / 1024:F1} {T("КБ/с", "KB/s")}     {T("Диск", "Disk")}  {(latest.DiskBytesPerSecond is double d ? (d / 1048576).ToString("F2") + T(" МБ/с", " MB/s") : "—")}";
        chart?.InvalidateVisual();
        int frozen = changes.Where(c => c.Kind == "Freeze" && c.Status == ChangeStatus.Applied).Select(c => c.Target).Distinct().Count();
        int attention = changes.Count(c => c.Status == ChangeStatus.Attention || c.Error is not null && c.Status != ChangeStatus.Reverted) + (powerLease is { Status: ChangeStatus.Attention } or { Status: ChangeStatus.Applied, Error: not null } ? 1 : 0);
        string powerState = powerLease is { Status: not ChangeStatus.Reverted } lease ? T($"временная схема · до {lease.DeadlineUtc.ToLocalTime():HH:mm:ss} · {lease.Status}", $"temporary policy · until {lease.DeadlineUtc.ToLocalTime():HH:mm:ss} · {lease.Status}") : T("нет временной", "no temporary policy");
        if (frozenLabel is not null) frozenLabel.Text = T($"Заморожено приложений: {frozen} · требуют внимания: {attention}\nПитание: {powerState}", $"Frozen apps: {frozen} · require attention: {attention}\nPower: {powerState}");
        SideMode.Text = selectedProfile.Name;
        SideSession.Text = tracker.Active ? T("Игра запущена", "Game running") : armed ? T("Ожидание игры", "Waiting for game") : T("Наблюдение выключено", "Monitoring off");
        RefreshDashboard();
        string trayText = "Serein · " + selectedProfile.Name + T($" · заморожено: {frozen}", $" · {frozen} frozen"); tray.Text = trayText[..Math.Min(63, trayText.Length)];
        if (trayMenu?.IsOpen == true) RefreshTrayState();
    }
    private async Task<OperationReply> Send(OperationRequest request)
    {
        if (request.Action is "Freeze" or "Priority" or "Power" && DriverOperationActive) throw new InvalidOperationException(T("Дождитесь завершения установки драйвера.", "Wait for driver installation to finish."));
        var result = await operations.Send(request); changes = result.Changes; powerLease = result.Power;
        Report($"{request.Action}: {result.Message}"); UpdateMetrics();
        if (page == "Gaming" && table is not null) table.ItemsSource = SessionChanges();
        if (!result.Success) throw new InvalidOperationException(result.Message);
        return result;
    }
    private async Task RestoreAll(bool userAction = true)
    {
        if (userAction) suppressUntilGameExit = true;
        var result = await Send(new("Restore"));
        if (!result.Success) Notify(result.Message);
    }
    private async void RestoreClick(object sender, RoutedEventArgs e)
    {
        RestoreButton.IsEnabled = false;
        try { await RestoreAll(); } catch (Exception ex) { Report(ex.Message); }
        finally { RestoreButton.IsEnabled = true; }
    }
    private void Report(string message)
    {
        StatusLine.Text = message; recent.Add($"{DateTimeOffset.Now:HH:mm:ss}  {message}"); if (recent.Count > 50) recent.RemoveAt(0); audit.Write(message);
    }
    private void Notify(string message) { Report(message); if (preferences.Notifications) tray.ShowBalloonTip(4000, "Serein", message, Forms.ToolTipIcon.Info); }
    private void ExportSamples(IReadOnlyList<MetricSample> source)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV|*.csv", FileName = "serein-metrics.csv" };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, Csv(source), Encoding.UTF8);
    }
    private static string Csv(IReadOnlyList<MetricSample> source)
    {
        var sb = new StringBuilder("utc,cpu_percent,total_physical_bytes,available_physical_bytes,network_bytes_sec,disk_bytes_sec\n");
        foreach (var s in source) sb.AppendLine(FormattableString.Invariant($"{s.Time:O},{s.CpuPercent},{s.TotalMemory},{s.AvailableMemory},{s.NetworkBytesPerSecond},{s.DiskBytesPerSecond}"));
        return sb.ToString();
    }
    private void SaveSession()
    {
        var directory = Path.Combine(data, "reports"); Directory.CreateDirectory(directory);
        var id = sessionStart.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        JsonFiles.Write(Path.Combine(directory, id + ".json"), new { Profile = selectedProfile.Name, StartedUtc = sessionStart, EndedUtc = DateTimeOffset.UtcNow, OS = Environment.OSVersion.Version.ToString(), Actions = changes, Power = powerLease, Samples = sessionSamples, FrameMetrics = "Unavailable: no frame capture provider" });
        File.WriteAllText(Path.Combine(directory, id + ".csv"), Csv(sessionSamples));
        foreach (var file in Directory.GetFiles(directory).Select(p => new FileInfo(p)).OrderByDescending(f => f.LastWriteTimeUtc).Skip(100)) file.Delete();
        sessionSamples.Clear();
    }
    private async Task HandleEvent(Func<Task> action)
    { try { await action(); } catch (Exception ex) { Notify(T("Операция требует внимания: ", "Operation requires attention: ") + ex.Message); } }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode == PowerModes.Suspend) Dispatcher.Invoke(async () => await HandleEvent(() => RestoreAll())); }
    private void SessionEnding(object sender, SessionEndingEventArgs e)
    {
        // Logoff is time-limited by Windows; independent watchdog also observes controller exit.
        try { operations.Send(new("Restore")).Wait(TimeSpan.FromSeconds(2)); } catch (Exception ex) { audit.Write("Logoff restore: " + ex.Message); }
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!closing)
        {
            e.Cancel = true;
            if (preferences.CloseToTray) Hide();
            else try { await ExitApp(); } catch (Exception ex) { Report(ex.Message); }
        }
    }
    private async Task ExitApp()
    {
        if (await PrepareExit()) System.Windows.Application.Current.Shutdown(applicationExitCode);
    }
    private async Task<bool> PrepareExit()
    {
        if (DriverOperationActive) { Report(T("Дождитесь результата установки драйвера. Операция и журнал могут продолжаться при аварийном закрытии.", "Wait for the driver result. Operation/report may continue if the UI is forcibly closed.")); return false; }
        if (closing) return true;
        timer.Stop(); armed = false;
        var reply = await operations.Send(new("Exit"));
        if (!reply.Success && !Confirm(T("Восстановление требует внимания. Завершить с сохранением журнала?", "Recovery requires attention. Exit with the journal retained?"))) { timer.Start(); return false; }
        closing = true; Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerChanged; Microsoft.Win32.SystemEvents.SessionEnding -= SessionEnding;
        if (trayMenu is not null) trayMenu.IsOpen = false;
        tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); appIcon.Dispose(); gpuTelemetry.Dispose(); cpuTelemetry.Dispose(); telemetry.Dispose(); operations.Dispose(); return true;
    }
    public async Task<bool> HandleControl(string request)
    {
        switch (request)
        {
            case "Status": return startupReady && !closing;
            case "Show": ShowWindow(); return true;
            case "Restore": await RestoreAll(); return true;
            case "Shutdown": return await PrepareExit();
            default: return false;
        }
    }
    private async Task Smoke(string directory)
    {
        Directory.CreateDirectory(directory); var results = new List<string>();
        bool background = Environment.GetCommandLineArgs().Contains("--tray");
        void Layout()
        {
            UpdateLayout();
            if (background) { RootViewport.Measure(new Size(Width, Height)); RootViewport.Arrange(new Rect(0, 0, Width, Height)); }
            RootViewport.UpdateLayout(); RootSurface.UpdateLayout();
            if (RootViewport.ActualWidth < Width * .9 || RootViewport.ActualHeight < Height * .9) throw new InvalidOperationException("UI smoke render surface is smaller than the intended window; refuse empty/minimized screenshots.");
        }
        void Capture(string name)
        {
            Layout(); var image = new RenderTargetBitmap((int)RootViewport.ActualWidth, (int)RootViewport.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(RootViewport);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(Path.Combine(directory, name + ".png")); png.Save(file);
        }
        try
        {
            if (Environment.GetCommandLineArgs().Contains("--cpu-admin"))
                for (int i = 0; i < 225 && cpuTelemetry.Current.Status == CpuSensorStatus.Starting; i++) await Task.Delay(200);
            if (light) ToggleTheme(); english = false; BuildNavigation();
            await Task.Delay(1200); await Poll(); await ReadProcesses(); devices = await Task.Run(DeviceInventory.Read); await Task.Delay(1100); await Poll();
            foreach (var key in Pages)
            {
                SelectPage(key); Layout(); await Task.Delay(220);
                var bitmap = new RenderTargetBitmap((int)RootViewport.ActualWidth, (int)RootViewport.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(RootViewport);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, key + ".png")); encoder.Save(file);
                results.Add($"{key}: built and rendered");
                if (key == "Drivers" && PageBody.Content is TabControl tabs)
                    for (int tab = 1; tab < tabs.Items.Count; tab++) { tabs.SelectedIndex = tab; Layout(); await Task.Delay(100); Capture("Drivers-tab-" + tab); results.Add("Driver tab " + tab + ": rendered, no scan/install/download invoked"); }
            }
            SelectPage("Processes"); var view = CollectionViewSource.GetDefaultView(table!.ItemsSource); view.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
            view.Filter = o => o is ProcessRow r && r.Pid == Environment.ProcessId;
            if (view.Cast<object>().Count() != 1) throw new InvalidOperationException("Process filter did not find own exact PID.");
            results.Add("Actual process view filter and sorting exercised; protected own PID found.");
            VerifyAppearanceInteractions(results);
            VerifyBackgroundSelector(directory, results);
            var cpuSample = cpuTelemetry.Current; cpuSample.Validate();
            JsonFiles.Write(Path.Combine(directory, "cpu-startup.json"), new { Arguments = Environment.GetCommandLineArgs(), HelperPid = cpuTelemetry.HelperPid, Status = cpuSample.Status, Detail = cpuSample.Detail });
            JsonFiles.Write(Path.Combine(directory, "cpu-sensors.json"), cpuSample);
            if (cpuSample.Status == CpuSensorStatus.Starting) throw new InvalidOperationException("CPU helper never returned a capability result.");
            results.Add("CPU helper returned a validated capability/reading result: " + cpuSample.Status + "; no driver installation invoked.");
            if (Environment.GetCommandLineArgs().Contains("--require-cpu") && cpuSample.Cpu?.TemperatureC is null) throw new InvalidOperationException("A real CPU temperature sample was required: " + cpuSample.Status);
            if (Environment.GetCommandLineArgs().Contains("--require-cpu"))
            {
                await Task.Delay(5200); var nextCpu = cpuTelemetry.Current;
                if (nextCpu.Cpu?.TemperatureC is null || nextCpu.Time <= cpuSample.Time) throw new InvalidOperationException("CPU provider did not deliver a second fresh sample.");
                JsonFiles.Write(Path.Combine(directory, "cpu-sensors-next.json"), nextCpu);
                JsonFiles.Write(Path.Combine(directory, "cpu-helper-boundary.json"), new { ParentPid = Environment.ProcessId, ParentElevated = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator), HelperPid = cpuTelemetry.HelperPid });
                results.Add("Actual AMD Ryzen temperature and a second fresh 5-second frame verified through the optional read-only elevated pipe; desktop remains standard-user.");
            }
            var gpuSample = gpuTelemetry.Current;
            if (Environment.GetCommandLineArgs().Contains("--require-gpu"))
            {
                if (gpuSample.Gpus.Count == 0 || !gpuSample.Gpus.Any(g => g.TemperatureC.HasValue)) throw new InvalidOperationException("A real GPU temperature sample was required: " + gpuSample.Status);
                for (int wait = 0; wait < 35 && gpuTelemetry.Current.Time <= gpuSample.Time; wait++) await Task.Delay(200);
                if (gpuTelemetry.Current.Time <= gpuSample.Time || gpuTelemetry.Current.Gpus.Count == 0) throw new InvalidOperationException("GPU provider did not deliver a fresh second sample.");
                results.Add("Actual AMD ADLX temperature sample and a second fresh 5-second frame verified; helper/native driver reads only.");
            }
            JsonFiles.Write(Path.Combine(directory, "gpu-sensors.json"), gpuTelemetry.Current);
            await VerifyTrayAppearance(directory, results, Environment.GetCommandLineArgs().Contains("--tray-popup-check"));
            SelectPage("Dashboard"); Layout(); await Task.Delay(220); Capture("Dashboard-accent");
            var previewDialog = CreateActionDialog(T("Подтвердите действие", "Confirm action"), T("Изменение затронет только выбранное приложение. Его исходное состояние будет записано перед применением. Продолжить?", "The change affects only the selected app. Its original state will be recorded before applying. Continue?"), true);
            var previewContent = (UIElement)previewDialog.Content; previewDialog.Content = null;
            var previewRoot = new Border { Background = (Brush)FindResource("CanvasBrush"), Child = previewContent };
            previewRoot.Measure(new Size(590, double.PositiveInfinity)); previewRoot.Arrange(new Rect(new Point(), previewRoot.DesiredSize)); previewRoot.UpdateLayout();
            if (!Descendants(previewRoot).OfType<Button>().Any(b => b.IsCancel && b.IsDefault) || previewDialog.ResizeMode != ResizeMode.NoResize) throw new InvalidOperationException("Confirmation dialog must default to cancellation and be fixed size.");
            var previewBitmap = new RenderTargetBitmap((int)previewRoot.ActualWidth, (int)previewRoot.ActualHeight, 96, 96, PixelFormats.Pbgra32); previewBitmap.Render(previewRoot);
            var previewPng = new PngBitmapEncoder(); previewPng.Frames.Add(BitmapFrame.Create(previewBitmap)); using (var f = File.Create(Path.Combine(directory, "Confirmation-preview.png"))) previewPng.Save(f);
            results.Add("Themed confirmation content rendered; cancellation is the default, dialog resizing disabled; no operation invoked.");
            Width = 960; Height = 960 * 820d / 1240; if (!light) ToggleTheme(); SelectPage("Dashboard"); Layout(); await Task.Delay(120);
            var resized = new RenderTargetBitmap((int)RootViewport.ActualWidth, (int)RootViewport.ActualHeight, 96, 96, PixelFormats.Pbgra32); resized.Render(RootViewport);
            var image = new PngBitmapEncoder(); image.Frames.Add(BitmapFrame.Create(resized)); using (var f = File.Create(Path.Combine(directory, "Light-resized.png"))) image.Save(f);
            if (!background) { Hide(); await Task.Delay(150); ShowWindow(); }
            await RestoreAll(); results.Add(background && !Environment.GetCommandLineArgs().Contains("--tray-popup-check") ? "Background render only; no activation/show/hide; Restore All with no active changes." : "Own UI hide/show, Restore All with no changes, theme/compact layout exercised programmatically; actual shell tray clicks require manual QA.");
            await Task.Delay(2100); await Poll();
            JsonFiles.Write(Path.Combine(directory, "smoke.json"), new { Success = true, Screens = results, Processes = processRows.Count, Devices = devices.Count, Sample = latest });
        }
        catch (Exception ex) { applicationExitCode = 1; JsonFiles.Write(Path.Combine(directory, "smoke.json"), new { Success = false, Error = ex.ToString(), Screens = results }); }
        await ExitApp();
    }
    private async Task ResourceCheck(string directory)
    {
        await Task.Delay(5000); // exclude startup/JIT
        async Task<object> Measure(string mode)
        {
            using var process = Process.GetCurrentProcess(); process.Refresh(); var startCpu = process.TotalProcessorTime; var clock = Stopwatch.StartNew();
            await Task.Delay(20000); process.Refresh();
            return new { Mode = mode, Seconds = clock.Elapsed.TotalSeconds, CpuPercentOfMachine = (process.TotalProcessorTime - startCpu).TotalSeconds / clock.Elapsed.TotalSeconds / Environment.ProcessorCount * 100, WorkingSetBytes = process.WorkingSet64, PrivateBytes = process.PrivateMemorySize64 };
        }
        var visible = await Measure("Dashboard visible / 2s telemetry / 6s inventory"); Hide(); var hidden = await Measure("Tray / 5s telemetry / 15s inventory");
        JsonFiles.Write(Path.Combine(directory, "resources.json"), new { OS = Environment.OSVersion.Version.ToString(), LogicalCPUs = Environment.ProcessorCount, Visible = visible, Tray = hidden, Method = "20s CPU-time delta normalized by logical CPUs, memory snapshots; startup/JIT excluded for 5s; one host observation, not a benchmark guarantee" });
        await ExitApp();
    }
}
public sealed class CleanupSelection(CleanupFile file)
{
    public CleanupFile File { get; } = file;
    public bool Selected { get; set; }
}
