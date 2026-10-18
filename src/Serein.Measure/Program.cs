using System.Diagnostics;
using Serein.Core;

try { return await Run(args); }
catch (Exception ex) { Console.Error.WriteLine("Measurement failed: " + ex.Message); return 1; }
static async Task<int> Run(string[] args)
{
if (args.FirstOrDefault() == "--cpu-pipe") return await Serein.Measure.CpuPipeStream.Run(args);
if (args.Length == 1 && args[0] == "--cpu-stream") return await Serein.Measure.CpuStream.Run();
if (args.Length is 1 or 2 && args[0] == "--cpu-snapshot")
{
    CpuSnapshot sample;
    try { using var cpu = new Serein.Measure.RyzenCpuSensors(args.Length == 2 ? args[1] : null); sample = cpu.Read(); }
    catch (Exception ex) { sample = Serein.Measure.CpuStream.Failure(ex); }
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(sample, JsonFiles.Options)); return sample.Cpu is null ? 4 : 0;
}
if (args.Length == 1 && args[0] == "--gpu-stream") return await Serein.Measure.GpuStream.Run();
if (args.Length == 1 && args[0] == "--gpu-snapshot")
{
    using var gpu = new Serein.Measure.AmdGpuSensors();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(gpu.Read(), JsonFiles.Options)); return 0;
}
if (args.Length == 2 && args[0] == "--analyze")
{
    var stats = FrameAccumulator.Read(args[1]); Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(stats, JsonFiles.Options)); return 0;
}
if (args.Length is not (4 or 6) || args[0] != "--cs2" || !int.TryParse(args[3], out int duration) || duration is < 15 or > 1800 || args.Length == 6 && args[4] is not ("--profile" or "--tray-app"))
{ Console.WriteLine("Serein.Measure --cs2 <official PresentMon-2.6.0-x64.exe> <output-directory> <seconds> [--profile <explicit-approval.json> | --tray-app <Serein.exe>]\nSerein.Measure --analyze <PresentMon-v1.csv>"); return 2; }
string tool = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
const string sha256 = "B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF";
if (!JsonFiles.Approve(tool).Sha256.Equals(sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Only the verified official PresentMon 2.6.0 x64 release is permitted.");
Directory.CreateDirectory(output);
Console.WriteLine("Waiting for cs2.exe (up to 10 minutes). Actions are determined by the explicit comparison option.");
ProcessIdentity? target = null;
var waiting = Stopwatch.StartNew();
while (waiting.Elapsed < TimeSpan.FromMinutes(10) && target is null)
{
    foreach (var process in Process.GetProcessesByName("cs2")) using (process) { target = Native.Identity(process.Id); break; }
    if (target is null) await Task.Delay(1000);
}
if (target is null) { Console.WriteLine("CS2 was not started."); return 3; }
string capture = Path.Combine(output, "frames.csv"), session = "Serein-CS2-" + Guid.NewGuid().ToString("N");
if (File.Exists(capture)) throw new InvalidOperationException("Choose a fresh recording folder.");
using var scenario = new MeasurementScenario(output, target, args.Length == 6 && args[4] == "--profile" ? args[5] : null, args.Length == 6 && args[4] == "--tray-app" ? args[5] : null);
var start = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
foreach (string argument in new[] { "--process_id", target.Pid.ToString(), "--output_stdout", "--v1_metrics", "--qpc_time", "--timed", duration.ToString(), "--terminate_after_timed", "--terminate_on_proc_exit", "--session_name", session, "--no_console_stats", "--no_track_input" }) start.ArgumentList.Add(argument);
Console.WriteLine($"Recording CS2 PID {target.Pid} for up to {duration}s. Capture includes menus/loading; mark match start for analysis.");
JsonFiles.Write(Path.Combine(output, "metadata.json"), new { Target = target, StartedUtc = DateTimeOffset.UtcNow, Provider = "PresentMon 2.6.0 standalone / ETW / v1 metrics / QPC", ProviderSHA256 = sha256, Session = session, DurationLimitSeconds = duration, Optimization = scenario.Description, QpcFrequency = Stopwatch.Frequency, OS = Environment.OSVersion.Version.ToString() });
using var provider = Process.Start(start) ?? throw new InvalidOperationException("PresentMon launch failed.");
var standardError = provider.StandardError.ReadToEndAsync();
using var telemetry = new TelemetrySampler(); var samples = new List<MetricSample>();
var monitoring = Stopwatch.StartNew(); FrameAccumulator? accumulator = null; var frameGate = new object();
var focus = new List<FocusObservation>(); using var focusLifetime = new CancellationTokenSource();
var focusTask = Task.Run(async () =>
{
    try { while (!focusLifetime.IsCancellationRequested) { bool? active = Native.IsForeground(target.Pid); if (focus.Count == 0 || focus[^1].GameForeground != active) focus.Add(new(Stopwatch.GetTimestamp(), active)); await Task.Delay(250, focusLifetime.Token); } }
    catch (OperationCanceledException) when (focusLifetime.IsCancellationRequested) { }
});
var csvTask = Task.Run(async () =>
{
    using var file = new FileStream(capture, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    using var writer = new StreamWriter(file); int written = 0;
    while (await provider.StandardOutput.ReadLineAsync() is string line)
    {
        await writer.WriteLineAsync(line);
        if (string.IsNullOrWhiteSpace(line)) continue;
        lock (frameGate) { if (accumulator is null) accumulator = new(line, target.Pid); else accumulator.Add(line); }
        if (++written % 1000 == 0) await writer.FlushAsync();
    }
});
try
{
    do
    {
        await Task.Delay(3000);
        if (csvTask.IsFaulted) await csvTask;
        var sample = telemetry.Sample(); samples.Add(sample);
        FrameStatistics? frames; lock (frameGate) frames = accumulator?.Snapshot();
        bool alive = Native.Matches(target);
        if (alive) await scenario.Advance(monitoring.Elapsed.TotalSeconds, duration);
        JsonFiles.Write(Path.Combine(output, "live.json"), new { ElapsedSeconds = monitoring.Elapsed.TotalSeconds, Phase = scenario.Current, Frames = frames, System = sample, GameForeground = Native.IsForeground(target.Pid), ProcessIdentityStillMatches = alive, Status = !alive ? "Target exited" : provider.HasExited ? "Capture ended" : "Recording" });
        Console.WriteLine(frames is null ? $"{DateTimeOffset.Now:HH:mm:ss} waiting for frame events; RAM {sample.MemoryPercent:F1}%" : $"{DateTimeOffset.Now:HH:mm:ss} {scenario.Current} · cumulative FPS {frames.AverageFps:F1} · 1% low {frames.Low1PercentFps:F1} · p99 {frames.P99Ms:F2}ms · CPU {sample.CpuPercent:F1}% · RAM {sample.MemoryPercent:F1}% · frames {frames.Intervals}");
    } while (!provider.HasExited && Native.Matches(target) && monitoring.Elapsed < TimeSpan.FromSeconds(duration + 20));
    if (!provider.HasExited)
    {
        var stop = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true };
        stop.ArgumentList.Add("--session_name"); stop.ArgumentList.Add(session); stop.ArgumentList.Add("--terminate_existing_session");
        using var cleanup = Process.Start(stop); if (cleanup is not null) await cleanup.WaitForExitAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await provider.WaitForExitAsync(timeout.Token);
    }
    await csvTask;
    scenario.Complete();
    await scenario.Restore();
    focusLifetime.Cancel(); await focusTask; focus.Add(new(Stopwatch.GetTimestamp(), null));
    JsonFiles.Write(Path.Combine(output, "foreground.json"), focus);
    JsonFiles.Write(Path.Combine(output, "comparison.json"), FrameWindows.Analyze(capture, target.Pid, Stopwatch.Frequency, scenario.Phases, focus));
    File.WriteAllText(Path.Combine(output, "provider.log"), await standardError);
    if (provider.ExitCode != 0) throw new InvalidOperationException($"PresentMon exit {provider.ExitCode}. See provider.log; access denied requires Performance Log Users or explicit elevation, never automatic group/policy edits.");
    JsonFiles.Write(Path.Combine(output, "summary.json"), new { Frames = accumulator?.Snapshot(), SystemSamples = samples, EndedUtc = DateTimeOffset.UtcNow, TargetStillRunning = Native.Matches(target), EndReason = Native.Matches(target) ? "Time limit" : "Target exited or lifetime changed" });
    Console.WriteLine("Capture saved: " + output);
    return accumulator?.Snapshot() is null ? 4 : 0;
}
finally
{
    focusLifetime.Cancel(); await focusTask;
    JsonFiles.Write(Path.Combine(output, "foreground.json"), focus);
    try { await scenario.Restore(); }
    finally
    {
      if (!provider.HasExited)
      {
        var stop = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true };
        stop.ArgumentList.Add("--session_name"); stop.ArgumentList.Add(session); stop.ArgumentList.Add("--terminate_existing_session");
        using var cleanup = Process.Start(stop); if (cleanup is not null) await cleanup.WaitForExitAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await provider.WaitForExitAsync(timeout.Token);
      }
    }
}
}
