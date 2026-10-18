using Serein.Core;

namespace Serein.Measure;
internal static class CpuStream
{
    public static CpuSnapshot Failure(Exception ex) => new(DateTimeOffset.UtcNow, ex is CpuSensorException reason ? reason.Status : CpuSensorStatus.Unavailable, null, ex.Message[..Math.Min(ex.Message.Length, 2000)]);
    public static async Task<int> Run()
    {
        using var lifetime = new CancellationTokenSource();
        _ = Task.Run(async () => { try { while (await Console.In.ReadAsync(new char[1], lifetime.Token) > 0) { } } catch (OperationCanceledException) { } finally { if (!lifetime.IsCancellationRequested) lifetime.Cancel(); } });
        using var output = Console.OpenStandardOutput();
        try
        {
            using var sensors = new RyzenCpuSensors();
            while (!lifetime.IsCancellationRequested)
            {
                await Wire.Write(output, sensors.Read(), lifetime.Token);
                await Task.Delay(TimeSpan.FromSeconds(5), lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { try { await Wire.Write(output, Failure(ex), CancellationToken.None); } catch (IOException) { } }
        finally { lifetime.Cancel(); }
        return 0;
    }
}
