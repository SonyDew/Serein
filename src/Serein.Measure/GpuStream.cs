using Serein.Core;

namespace Serein.Measure;
internal static class GpuStream
{
    public static async Task<int> Run()
    {
        using var lifetime = new CancellationTokenSource();
        // No commands: stdin closing means the parent exited/crashed. Never orphan a sampler.
        _ = Task.Run(async () => { try { while (await Console.In.ReadAsync(new char[1], lifetime.Token) > 0) { } } catch (OperationCanceledException) { } finally { if (!lifetime.IsCancellationRequested) lifetime.Cancel(); } });
        using var output = Console.OpenStandardOutput();
        try
        {
            using var sensors = new AmdGpuSensors();
            while (!lifetime.IsCancellationRequested)
            {
                await Wire.Write(output, sensors.Read(), lifetime.Token);
                await Task.Delay(TimeSpan.FromSeconds(5), lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            try { await Wire.Write(output, new GpuSnapshot(DateTimeOffset.UtcNow, "AMD ADLX", "Unavailable: " + ex.Message[..Math.Min(ex.Message.Length, 1800)], []), CancellationToken.None); }
            catch (IOException) { }
        }
        finally { lifetime.Cancel(); }
        return 0;
    }
}
