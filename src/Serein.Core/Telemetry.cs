using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Serein.Core;

public record MetricSample(DateTimeOffset Time, double? CpuPercent, ulong TotalMemory, ulong AvailableMemory, double NetworkBytesPerSecond, double? DiskBytesPerSecond)
{
    public double MemoryPercent => TotalMemory == 0 ? 0 : (TotalMemory - AvailableMemory) * 100d / TotalMemory;
}
public sealed class TelemetrySampler : IDisposable
{
    private long idle, total; private bool warm;
    private DateTimeOffset last; private long network;
    private readonly PdhCounter disk = new(@"\PhysicalDisk(_Total)\Disk Bytes/sec");
    public MetricSample Sample()
    {
        if (!Native.GetSystemTimes(out var i, out var kernel, out var user)) throw Native.Error();
        long sum = kernel + user;
        double? cpu = warm && sum > total ? Math.Clamp((sum - total - (i - idle)) * 100d / (sum - total), 0, 100) : null;
        var memory = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
        if (!Native.GlobalMemoryStatusEx(ref memory)) throw Native.Error();
        long bytes = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        { var stats = adapter.GetIPv4Statistics(); bytes += stats.BytesReceived + stats.BytesSent; }
        var now = DateTimeOffset.UtcNow;
        double rate = warm ? Math.Max(0, bytes - network) / Math.Max(.001, (now - last).TotalSeconds) : 0;
        idle = i; total = sum; network = bytes; last = now; warm = true;
        return new(now, cpu, memory.Total, memory.Available, rate, disk.Read());
    }
    public void Dispose() => disk.Dispose();
}
public sealed class PdhCounter : IDisposable
{
    private nint query, counter;
    [StructLayout(LayoutKind.Explicit)] private struct Value { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Number; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, nint user, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(nint query, string path, nint user, out nint counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out Value value);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
    public PdhCounter(string path)
    {
        if (PdhOpenQuery(null, 0, out query) != 0) { query = 0; return; }
        if (PdhAddEnglishCounter(query, path, 0, out counter) != 0) { PdhCloseQuery(query); query = 0; counter = 0; }
        if (query != 0) PdhCollectQueryData(query);
    }
    public double? Read()
    {
        if (query == 0 || PdhCollectQueryData(query) != 0 || PdhGetFormattedCounterValue(counter, 0x200, out _, out var value) != 0 || value.Status > 1) return null;
        return double.IsFinite(value.Number) ? Math.Max(0, value.Number) : null;
    }
    public void Dispose() { if (query != 0) { PdhCloseQuery(query); query = 0; } }
}
