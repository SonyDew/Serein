using System.Diagnostics;

namespace Serein.Core;

// The UI reads immutable snapshots. Vendor code stays in a disposable, unprivileged
// child; closing stdin is its lifetime signal, including after a UI crash.
public sealed class GpuTelemetryClient : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task reader;
    private Process? process;
    private bool disposed;
    private GpuSnapshot current = new(DateTimeOffset.UtcNow, "AMD ADLX", "Starting", []);
    public GpuSnapshot Current
    {
        get
        {
            var sample = Volatile.Read(ref current);
            return sample.Gpus.Count > 0 && DateTimeOffset.UtcNow - sample.Time > TimeSpan.FromSeconds(12)
                ? new(sample.Time, sample.Provider, "Sensor data stale", []) : sample;
        }
    }
    public GpuTelemetryClient(string helperPath)
    {
        string path = Path.GetFullPath(helperPath);
        reader = Task.Run(async () =>
        {
            try
            {
                var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add("--gpu-stream");
                using var child = new Process { StartInfo = start };
                lock (gate) { if (disposed) return; child.Start(); process = child; }
                // Drain diagnostics without retaining unbounded output.
                var errors = Task.Run(async () => { var buffer = new char[1024]; try { while (await child.StandardError.ReadAsync(buffer, lifetime.Token).ConfigureAwait(false) > 0) { } } catch (OperationCanceledException) { } });
                try
                {
                    while (!lifetime.IsCancellationRequested)
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
                        var sample = await Wire.Read<GpuSnapshot>(child.StandardOutput.BaseStream, timeout.Token).ConfigureAwait(false);
                        sample.Validate(); Volatile.Write(ref current, sample);
                        if (sample.Gpus.Count == 0) return;
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
                if (!lifetime.IsCancellationRequested) Volatile.Write(ref current, new(DateTimeOffset.UtcNow, "AMD ADLX", "Unavailable: " + ex.Message[..Math.Min(ex.Message.Length, 1800)], []));
            }
        });
    }
    private static void Stop(Process child)
    {
        try { try { child.StandardInput.Close(); } catch (IOException) { } if (!child.HasExited && !child.WaitForExit(500)) child.Kill(); }
        catch (InvalidOperationException) { } // Already exited/disposed by its owner.
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; lifetime.Cancel(); if (process is not null) Stop(process); }
        _ = reader.ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
    }
}
