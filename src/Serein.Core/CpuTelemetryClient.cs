using System.Diagnostics;
using System.IO.Pipes;

namespace Serein.Core;

// The UI stays unprivileged. Optional RunAs is explicit and confined to a read-only
// helper with peer-validated IPC and a heartbeat; normal discovery uses stdout.
public sealed class CpuTelemetryClient : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task reader;
    private Process? process;
    private NamedPipeServerStream? connection;
    private readonly bool pipeMode;
    private bool disposed;
    private CpuSnapshot current = new(DateTimeOffset.UtcNow, CpuSensorStatus.Starting, null);
    public int? HelperPid { get { lock (gate) return process?.Id; } }
    public CpuSnapshot Current
    {
        get
        {
            var sample = Volatile.Read(ref current);
            return sample.Cpu is not null && DateTimeOffset.UtcNow - sample.Time > TimeSpan.FromSeconds(12)
                ? new(sample.Time, CpuSensorStatus.Unavailable, null, "Sensor data stale") : sample;
        }
    }
    public CpuTelemetryClient(string helperPath, bool requestElevation = false, bool probePipe = false)
    {
        string path = Path.GetFullPath(helperPath);
        pipeMode = requestElevation || probePipe;
        reader = Task.Run(async () =>
        {
            try
            {
                if (pipeMode) { await ReadPipe(path, requestElevation).ConfigureAwait(false); return; }
                var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add("--cpu-stream");
                using var child = new Process { StartInfo = start };
                lock (gate) { if (disposed) return; child.Start(); process = child; }
                // Drain diagnostics without retaining unbounded output.
                var errors = Task.Run(async () => { var buffer = new char[1024]; try { while (await child.StandardError.ReadAsync(buffer, lifetime.Token).ConfigureAwait(false) > 0) { } } catch (OperationCanceledException) { } });
                try
                {
                    while (!lifetime.IsCancellationRequested)
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
                        var sample = await Wire.Read<CpuSnapshot>(child.StandardOutput.BaseStream, timeout.Token).ConfigureAwait(false);
                        sample.Validate(); Volatile.Write(ref current, sample);
                        if (sample.Cpu is null) return;
                    }
                }
                finally
                {
                    Stop(child);
                    await errors.ConfigureAwait(false);
                    lock (gate) { process = null; }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (!lifetime.IsCancellationRequested) Volatile.Write(ref current, new(DateTimeOffset.UtcNow, ex is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 } ? CpuSensorStatus.AccessRequired : CpuSensorStatus.Unavailable, null, ex.Message[..Math.Min(ex.Message.Length, 1800)]));
            }
        });
    }
    private async Task ReadPipe(string path, bool elevate)
    {
        string name = CpuSensorPipe.NewName(); var owner = Native.Identity(Environment.ProcessId);
        using var pipe = SameUserPipe.CreateServer(name);
        lock (gate) { if (disposed) return; connection = pipe; }
        var start = new ProcessStartInfo(path) { UseShellExecute = elevate, CreateNoWindow = !elevate, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Environment.SystemDirectory };
        if (elevate) start.Verb = "runas";
        foreach (string argument in new[] { "--cpu-pipe", name, "--owner", owner.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture), "--born", owner.CreatedFileTime.ToString(System.Globalization.CultureInfo.InvariantCulture) }) start.ArgumentList.Add(argument);
        // RunAs may wait for consent. Do not hold the UI/disposal lock during it.
        using var child = Process.Start(start) ?? throw new InvalidOperationException("CPU helper launch failed.");
        var identity = Native.Identity(child.Id);
        lock (gate) { if (disposed) return; process = child; }
        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            { timeout.CancelAfter(TimeSpan.FromSeconds(30)); await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false); }
            if (!DriverAdminClient.Peer(pipe.SafePipeHandle, child.Id, true) || !Native.Matches(identity)) throw new UnauthorizedAccessException("Unexpected CPU helper pipe peer.");
            var heartbeat = Task.Run(async () =>
            {
                try
                {
                    byte[] message = [CpuSensorPipe.Heartbeat];
                    while (!lifetime.IsCancellationRequested)
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                        await pipe.WriteAsync(message, timeout.Token).ConfigureAwait(false); await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
                        await Task.Delay(2000, lifetime.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
            });
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    var sample = await Wire.Read<CpuSnapshot>(pipe, timeout.Token).ConfigureAwait(false);
                    sample.Validate(); Volatile.Write(ref current, sample);
                    if (sample.Cpu is null) return;
                }
            }
            finally { pipe.Dispose(); await heartbeat.ConfigureAwait(false); }
        }
        finally { lock (gate) { process = null; connection = null; } }
    }
    private static void Stop(Process child)
    {
        try { try { child.StandardInput.Close(); } catch (IOException) { } if (!child.HasExited && !child.WaitForExit(500)) child.Kill(); }
        catch (InvalidOperationException) { } // Already exited/disposed by its owner.
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; lifetime.Cancel(); connection?.Dispose(); if (!pipeMode && process is not null) Stop(process); }
        _ = reader.ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
    }
}
