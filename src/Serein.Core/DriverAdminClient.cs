using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Serein.Core;
public static class DriverAdminClient
{
    public static bool IsRunning(string broker)
    {
        bool running = false;
        foreach (var process in Process.GetProcessesByName("Serein.Admin"))
        using (process)
        {
            try { if (!process.HasExited && (process.MainModule?.FileName is not string path || Path.GetFullPath(path).Equals(Path.GetFullPath(broker), StringComparison.OrdinalIgnoreCase))) running = true; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { running = true; }
        }
        return running;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    public static bool Peer(SafePipeHandle handle, int expectedPid, bool client) => (client ? GetNamedPipeClientProcessId(handle, out uint pid) : GetNamedPipeServerProcessId(handle, out pid)) && pid == expectedPid;
    public static Task<DriverInstallReply> Install(string broker, DriverInstallRequest request) { DriverInstaller.Validate(request); if (IsRunning(broker)) throw new InvalidOperationException("A driver broker is still running. Wait for its report before starting another installation."); return Send(broker, request, false); }
    public static Task<DriverInstallReply> Probe(string broker) => Send(broker, new("Probe"), true);
    private static async Task<DriverInstallReply> Send(string broker, DriverInstallRequest request, bool probe)
    {
        string pipeName = "serein-admin-" + Guid.NewGuid().ToString("N"); var owner = Native.Identity(Environment.ProcessId);
        using var pipe = SameUserPipe.CreateServer(pipeName);
        var launch = new ProcessStartInfo(Path.GetFullPath(broker)) { UseShellExecute = !probe, CreateNoWindow = probe, WindowStyle = ProcessWindowStyle.Hidden };
        if (!probe) launch.Verb = "runas"; else launch.ArgumentList.Add("--probe");
        foreach (string arg in new[] { "--pipe", pipeName, "--owner", owner.Pid.ToString(), "--born", owner.CreatedFileTime.ToString() }) launch.ArgumentList.Add(arg);
        using var process = Process.Start(launch) ?? throw new InvalidOperationException("Driver broker launch failed."); var identity = Native.Identity(process.Id);
        using var connection = new CancellationTokenSource(TimeSpan.FromSeconds(45)); await pipe.WaitForConnectionAsync(connection.Token);
        if (!Peer(pipe.SafePipeHandle, process.Id, true) || !Native.Matches(identity)) throw new UnauthorizedAccessException("Unexpected driver broker pipe peer.");
        using var write = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await Wire.Write(pipe, request, write.Token);
        // Do not cancel/kill a live driver commit when it takes time. Broker keeps its durable report if UI disconnects.
        using var completion = new CancellationTokenSource(TimeSpan.FromMinutes(60)); return await Wire.Read<DriverInstallReply>(pipe, completion.Token);
    }
}
