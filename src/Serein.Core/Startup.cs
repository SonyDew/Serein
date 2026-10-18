using Microsoft.Win32;

namespace Serein.Core;

public record StartupRow(string Name, string Command, string Origin, bool Enabled, bool CanChange);
public sealed class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string BackupKey = @"Software\Serein\DisabledRun";
    public List<StartupRow> Read()
    {
        var list = new List<StartupRow>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(hive == Registry.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, view);
            using var key = root.OpenSubKey(RunKey);
            if (key is not null) foreach (string name in key.GetValueNames())
                list.Add(new(name, Convert.ToString(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)) ?? "", $"{(hive == Registry.CurrentUser ? "HKCU" : "HKLM")} Run {view}", true, hive == Registry.CurrentUser && view == RegistryView.Registry64));
        }
        using var backup = Registry.CurrentUser.OpenSubKey(BackupKey);
        if (backup is not null) foreach (string name in backup.GetValueNames()) list.Add(new(name, (string)backup.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames)!, "Serein disabled HKCU Run", false, true));
        foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
        {
            var directory = Environment.GetFolderPath(folder);
            if (Directory.Exists(directory)) foreach (var path in Directory.EnumerateFiles(directory)) list.Add(new(System.IO.Path.GetFileName(path), path, folder.ToString(), true, false));
        }
        return list;
    }
    public void Toggle(StartupRow row)
    {
        if (!row.CanChange || row.Name.Length is < 1 or > 256 || row.Command.Length > 32768) throw new InvalidOperationException("This origin is read-only.");
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        using var backup = Registry.CurrentUser.CreateSubKey(BackupKey);
        if (row.Enabled)
        {
            if (Convert.ToString(run.GetValue(row.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)) != row.Command) throw new InvalidOperationException("Startup entry changed; refresh.");
            if (backup.GetValue(row.Name) is not null) throw new InvalidOperationException("Existing backup requires reconciliation.");
            var kind = run.GetValueKind(row.Name);
            if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) throw new InvalidOperationException("Unsupported value kind.");
            backup.SetValue(row.Name, row.Command, kind); backup.Flush(); run.DeleteValue(row.Name); run.Flush();
        }
        else
        {
            if (run.GetValue(row.Name) is not null) throw new InvalidOperationException("Restore conflict: live entry exists.");
            if (Convert.ToString(backup.GetValue(row.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)) != row.Command) throw new InvalidOperationException("Backup changed; refresh.");
            run.SetValue(row.Name, row.Command, backup.GetValueKind(row.Name)); run.Flush(); backup.DeleteValue(row.Name); backup.Flush();
        }
    }
    public void SetAppStartup(bool enabled, string executable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue("Serein", $"\"{System.IO.Path.GetFullPath(executable)}\" --tray"); else key.DeleteValue("Serein", false);
    }
}
