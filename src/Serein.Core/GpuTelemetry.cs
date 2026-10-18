namespace Serein.Core;

public sealed record GpuReading(string Name, string DeviceKey, double? TemperatureC, double? HotspotC,
    double? UsagePercent, double? UsedMemoryMb, double? TotalMemoryMb, double? ChipPowerW,
    double? BoardPowerW, double? CoreClockMHz, double? MemoryClockMHz, double? FanRpm)
{
    public GpuReading Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 256 || string.IsNullOrWhiteSpace(DeviceKey) || DeviceKey.Length > 1024) throw new InvalidDataException("Invalid GPU identity.");
        static void Range(double? value, double min, double max) { if (value.HasValue && (!double.IsFinite(value.Value) || value < min || value > max)) throw new InvalidDataException("Invalid GPU sensor reading."); }
        Range(TemperatureC, -50, 150); Range(HotspotC, -50, 150); Range(UsagePercent, 0, 100);
        Range(UsedMemoryMb, 0, 1048576); Range(TotalMemoryMb, 1, 1048576);
        if (UsedMemoryMb > TotalMemoryMb) throw new InvalidDataException("GPU memory usage exceeds capacity.");
        Range(ChipPowerW, 0, 2000); Range(BoardPowerW, 0, 2000); Range(CoreClockMHz, 0, 20000); Range(MemoryClockMHz, 0, 30000); Range(FanRpm, 0, 30000);
        return this;
    }
}
public sealed record GpuSnapshot(DateTimeOffset Time, string Provider, string Status, IReadOnlyList<GpuReading> Gpus)
{
    public GpuSnapshot Validate()
    {
        if (Provider != "AMD ADLX" || string.IsNullOrWhiteSpace(Status) || Status.Length > 2048 || Gpus is null || Gpus.Count > 8 || Time > DateTimeOffset.UtcNow.AddSeconds(10)) throw new InvalidDataException("Invalid GPU provider response.");
        foreach (var gpu in Gpus) gpu.Validate(); return this;
    }
}
