using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Serein.Core;

namespace Serein.Measure;
internal static class CpuPipeStream
{
    public static async Task<int> Run(string[] args)
    {
        if (!CpuSensorPipe.TryParse(args, out string name, out int ownerPid, out long born)) return 2;
        using var lifetime = new CancellationTokenSource();
        try
        {
            var owner = Native.Identity(ownerPid);
            if (owner.CreatedFileTime != born) return 2;
            using (var process = Native.OpenProcess(Native.Query, false, ownerPid))
                if (Native.Owner(process) != WindowsIdentity.GetCurrent().User?.Value) return 3;
            using var pipe = SameUserPipe.OpenClient(name);
            using (var connect = new CancellationTokenSource(TimeSpan.FromSeconds(15))) await pipe.ConnectAsync(connect.Token);
            if (!SameUserPipe.ServerMatches(pipe, owner)) return 3;
            // Independent of a potentially stuck native Init/Read/UnInit. Exits only
            // this read-only helper after lost parent/heartbeat, never the watchdog.
            var watchdog = Task.Run(async () =>
            {
                var total = Stopwatch.StartNew();
                while (!lifetime.IsCancellationRequested)
                {
                    if (!Native.Matches(owner) || total.Elapsed > TimeSpan.FromHours(6)) { lifetime.Cancel(); break; }
                    await Task.Delay(1000);
                }
                await Task.Delay(5000); Environment.Exit(0);
            });
            var heartbeats = Task.Run(async () =>
            {
                try
                {
                    byte[] message = new byte[1];
                    while (!lifetime.IsCancellationRequested)
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
                        if (await pipe.ReadAsync(message, timeout.Token) != 1 || message[0] != CpuSensorPipe.Heartbeat) break;
                    }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
                finally { lifetime.Cancel(); }
            });
            try
            {
                using var sensors = new RyzenCpuSensors();
                while (!lifetime.IsCancellationRequested)
                {
                    using (var write = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                    { write.CancelAfter(TimeSpan.FromSeconds(5)); await Wire.Write(pipe, sensors.Read(), write.Token); }
                    await Task.Delay(TimeSpan.FromSeconds(5), lifetime.Token);
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await Wire.Write(pipe, CpuStream.Failure(ex), timeout.Token); } catch (Exception error) when (error is IOException or OperationCanceledException) { }
            }
            finally { lifetime.Cancel(); }
            await heartbeats; GC.KeepAlive(watchdog); return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Console.Error.WriteLine(ex.Message); return 1; }
        finally { lifetime.Cancel(); }
    }
}
