using System.Diagnostics;

namespace Serein.Core;
public static class DriverBackup
{
    public static ProcessStartInfo Prepare(string directory)
    {
        directory = System.IO.Path.GetFullPath(directory);
        if (!Directory.Exists(directory) || Directory.EnumerateFileSystemEntries(directory).Any() || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Choose an empty, non-redirected backup folder.");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)) throw new NotSupportedException("PnPUtil export requires Windows 10 1607+. ");
        var info = new ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "pnputil.exe")) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        info.ArgumentList.Add("/export-driver"); info.ArgumentList.Add("*"); info.ArgumentList.Add(directory); return info;
    }
    public static async Task Export(string directory)
    {
        using var process = Process.Start(Prepare(directory)) ?? throw new InvalidOperationException("PnPUtil did not start.");
        await process.WaitForExitAsync(); if (process.ExitCode != 0) throw new InvalidOperationException($"PnPUtil export failed (exit {process.ExitCode}). Check folder permissions/UAC. Partial backups may remain.");
        if (!Directory.EnumerateFiles(directory, "*.inf", SearchOption.AllDirectories).Any()) throw new InvalidOperationException("Export returned no INF files; backup coverage cannot be confirmed.");
    }
}
