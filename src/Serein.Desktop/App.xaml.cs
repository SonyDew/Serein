using System.Windows;
using System.IO.Pipes;
using Serein.Core;

namespace Serein.Desktop;
public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private readonly CancellationTokenSource lifetime = new();
    private string ControlPipe => "serein-ui-" + System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value + "-" + System.Diagnostics.Process.GetCurrentProcess().SessionId;
    protected override async void OnStartup(StartupEventArgs e)
    {
        instance = new Mutex(true, "Local\\Serein-" + System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value, out bool first);
        if (!first)
        {
            // async startup returns to WPF before the pipe reply; suppress automatic second-window creation.
            try
            {
                using var pipe = new NamedPipeClientStream(".", ControlPipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await pipe.ConnectAsync(timeout.Token); await Wire.Write(pipe, e.Args.Contains("--shutdown") ? "Shutdown" : e.Args.Contains("--status") ? "Status" : "Show", timeout.Token);
                bool accepted = await Wire.Read<bool>(pipe, timeout.Token); Shutdown(accepted ? 0 : 1);
            }
            catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "Serein control"); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--status")) { Shutdown(3); return; }
        if (e.Args.Contains("--shutdown"))
        {
            var results = Recovery.Reconcile(System.IO.Path.Combine(AppPaths.Data, "sessions"));
            Shutdown(results.Any(r => !r.Contains("0 unresolved", StringComparison.Ordinal)) ? 1 : 0); return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            new Serein.Core.AuditLog(Serein.Core.AppPaths.Data).Write("UI error: " + args.Exception.Message);
            System.Windows.MessageBox.Show(args.Exception.Message, "Serein"); args.Handled = true;
        };
        bool trayOnly = e.Args.Contains("--tray");
        base.OnStartup(e);
        var window = new MainWindow(); MainWindow = window;
        _ = RunControlServer();
        if (trayOnly) await window.InitializeBackground();
        else window.Show();
    }
    private async Task RunControlServer()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(ControlPipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(lifetime.Token);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                string request = await Wire.Read<string>(pipe, timeout.Token);
                bool accepted = MainWindow is MainWindow window && await window.HandleControl(request);
                await Wire.Write(pipe, accepted, timeout.Token);
                if (accepted && request == "Shutdown") { Shutdown(); return; }
            }
            catch (OperationCanceledException) { if (lifetime.IsCancellationRequested) return; }
            catch (Exception ex) { new AuditLog(AppPaths.Data).Write("Control endpoint: " + ex.Message); }
        }
    }
    protected override void OnExit(ExitEventArgs e) { lifetime.Cancel(); instance?.Dispose(); lifetime.Dispose(); base.OnExit(e); }
}
