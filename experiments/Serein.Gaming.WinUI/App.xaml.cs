using System.IO;
using Microsoft.UI.Xaml;

namespace Serein_Gaming_WinUI;
public partial class App : Application
{
    private Window? window;
    private Mutex? singleton;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Serein.Gaming.WinUI");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "last-error.txt"), e.Message + "\n" + e.Exception + "\n" + string.Join("\n", e.Exception.Data.Keys.Cast<object>().Select(key => key + ": " + e.Exception.Data[key])));
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string identity = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var command = Environment.GetCommandLineArgs();
        int smoke = Array.IndexOf(command, "--smoke");
        string scope = smoke >= 0 && smoke + 1 < command.Length ? Path.GetFullPath(command[smoke + 1]) : "normal";
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity + scope)));
        singleton = new Mutex(true, "Local\\Serein.Gaming.WinUI." + hash, out bool created);
        if (!created) { singleton.Dispose(); singleton = null; Exit(); return; }
        window = new MainWindow();
        window.Activate();
    }
}
