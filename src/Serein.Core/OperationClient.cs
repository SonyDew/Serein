using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace Serein.Core;

public sealed class OperationClient(string hostPath, string dataDirectory) : IDisposable
{
    private Process? process;
    private string? pipe;
    private readonly SemaphoreSlim gate = new(1, 1);
    public int? HostPid => process is { HasExited: false } ? process.Id : null;
    public async Task<OperationReply> Send(OperationRequest request)
    {
        await gate.WaitAsync();
        try
        {
            if (process is null || process.HasExited)
            {
                var reconciliation = Recovery.Reconcile(dataDirectory);
                var recovered = Directory.Exists(dataDirectory) ? Directory.EnumerateFiles(dataDirectory, "*.json").Select(p => JsonFiles.Read<Journal>(p)).SelectMany(j => j.Changes).Where(c => c.Status != ChangeStatus.Reverted).ToList() : [];
                var power = PowerLeaseController.Pending(dataDirectory);
                if (request.Action is "Restore" or "Status" or "Exit") return new(recovered.Count + power.Count == 0, recovered.Count + power.Count == 0 ? "No outstanding changes." : string.Join("; ", reconciliation), recovered, power.FirstOrDefault());
                if (recovered.Count + power.Count > 0) throw new InvalidOperationException("Unresolved recovery records must be reviewed before new operations.");
                process?.Dispose();
                pipe = "serein-" + Guid.NewGuid().ToString("N");
                var info = new ProcessStartInfo(hostPath) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { "--pipe", pipe, "--owner", Environment.ProcessId.ToString(), "--born", Native.Identity(Environment.ProcessId).CreatedFileTime.ToString(), "--data", dataDirectory }) info.ArgumentList.Add(arg);
                process = Process.Start(info) ?? throw new InvalidOperationException("Watchdog launch failed.");
            }
            using var stream = new NamedPipeClientStream(".", pipe!, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await stream.ConnectAsync(timeout.Token);
            await Wire.Write(stream, request, timeout.Token);
            return await Wire.Read<OperationReply>(stream, timeout.Token);
        }
        finally { gate.Release(); }
    }
    public void Dispose() { process?.Dispose(); gate.Dispose(); }
}
public static class Wire
{
    public static async Task Write<T>(Stream stream, T value, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonFiles.Options);
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("IPC message too large.");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), ct); await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct);
    }
    public static async Task<T> Read<T>(Stream stream, CancellationToken ct)
    {
        var size = new byte[4]; await stream.ReadExactlyAsync(size, ct); int length = BitConverter.ToInt32(size);
        if (length is < 2 or > 1024 * 1024) throw new InvalidDataException("IPC length rejected.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, ct);
        return JsonSerializer.Deserialize<T>(bytes, JsonFiles.Options) ?? throw new InvalidDataException("Empty IPC message.");
    }
}
