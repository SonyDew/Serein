using System.Diagnostics;
using Serein.Core;

if (CpuSensorPipe.TryParse(args, out string cpuPipe, out int cpuOwnerPid, out long cpuBorn))
{
    if (Native.Identity(cpuOwnerPid).CreatedFileTime != cpuBorn) return;
    using var pipe = SameUserPipe.OpenClient(cpuPipe);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await pipe.ConnectAsync(timeout.Token);
    if (!SameUserPipe.ServerMatches(pipe, Native.Identity(cpuOwnerPid))) return;
    await Wire.Write(pipe, new CpuSnapshot(DateTimeOffset.UtcNow, CpuSensorStatus.Available, new("Fixture pipe CPU", 52, 4200, 0)), timeout.Token);
    var message = new byte[1]; try { while (await pipe.ReadAsync(message, timeout.Token) == 1 && message[0] == CpuSensorPipe.Heartbeat) { } } catch (IOException) { }
    return;
}

if (args.Length == 1 && args[0] == "--cpu-stream")
{
    using var output = Console.OpenStandardOutput();
    await Wire.Write(output, new CpuSnapshot(DateTimeOffset.UtcNow, CpuSensorStatus.Available, new("Fixture CPU", 52, 4200, 0)), CancellationToken.None);
    await Console.In.ReadToEndAsync(); return;
}

if (args.Length == 1 && args[0] == "--gpu-stream")
{
    using var output = Console.OpenStandardOutput();
    var sample = new GpuReading("Fixture GPU", "fixture-only", 42, 54, 0, 128, 4096, null, 25, 0, 0, 0);
    await Wire.Write(output, new GpuSnapshot(DateTimeOffset.UtcNow, "AMD ADLX", "Available", [sample]), CancellationToken.None);
    // Same lifetime contract as the real provider, no vendor DLL/hardware reads.
    await Console.In.ReadToEndAsync(); return;
}

if (args.Length == 3 && args[0] == "--spawn")
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    start.ArgumentList.Add("--heartbeat"); start.ArgumentList.Add(args[1]);
    var child = Process.Start(start) ?? throw new InvalidOperationException("Fixture launch failed.");
    File.WriteAllText(args[2], child.Id.ToString()); child.Dispose(); return;
}
if (args.Length == 2 && args[0] == "--heartbeat")
{
    // Deliberately tiny test-owned workload; no busy looping or host stress.
    while (true) { File.AppendAllText(args[1], DateTime.UtcNow.Ticks + "\n"); Thread.Sleep(60); }
}
throw new ArgumentException("Fixture-only arguments required.");
