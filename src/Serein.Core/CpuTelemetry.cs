namespace Serein.Core;

public enum CpuSensorStatus { Starting, Available, SdkNotInstalled, DriverNotRunning, UnsupportedVersion, Unavailable, AccessRequired }
public sealed record CpuReading(string Name, double? TemperatureC, double? PeakClockMHz, double? PackagePowerW)
{
    public CpuReading Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 512) throw new InvalidDataException("Invalid CPU identity.");
        foreach (var (value, min, max) in new[] { (TemperatureC, -50d, 150d), (PeakClockMHz, 0d, 10000d), (PackagePowerW, 0d, 2000d) })
            if (value is double number && (!double.IsFinite(number) || number < min || number > max)) throw new InvalidDataException("Invalid CPU sensor reading.");
        return this;
    }
}
public sealed record CpuSnapshot(DateTimeOffset Time, CpuSensorStatus Status, CpuReading? Cpu, string Detail = "")
{
    public CpuSnapshot Validate()
    {
        if (!Enum.IsDefined(Status) || Detail is null || Detail.Length > 2048 || Time > DateTimeOffset.UtcNow.AddMinutes(1) || (Status == CpuSensorStatus.Available) != (Cpu is not null)) throw new InvalidDataException("Invalid CPU snapshot.");
        Cpu?.Validate(); return this;
    }
}
