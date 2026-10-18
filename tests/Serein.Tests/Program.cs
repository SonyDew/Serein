using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Serein.Core;
using Serein.Tests;

string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string host = Path.Combine(repo, "src/Serein.Host/bin/Release/net10.0-windows/Serein.Host.exe");
string fixture = Path.GetFullPath(Path.Combine(repo, "tests/OptimizerFixture/bin/Release/net10.0-windows/OptimizerFixture.exe"));
if (args.Length == 2 && args[0] == "--probe-driver") { var probe = await DriverAdminClient.Probe(args[1]); Console.WriteLine(probe.Message); return probe.Success ? 0 : 1; }
if (args.Length == 3 && args[0] == "--cpu-live")
{
    using var client = new CpuTelemetryClient(args[1], requestElevation: true);
    for (int i = 0; i < 450 && client.Current.Status == CpuSensorStatus.Starting; i++) await Task.Delay(100);
    var first = client.Current; if (first.Cpu?.TemperatureC is null) throw new InvalidOperationException("No real CPU reading: " + first.Detail);
    int pid = client.HelperPid ?? throw new InvalidOperationException("No owned helper PID.");
    await Task.Delay(5200); var second = client.Current;
    if (second.Time <= first.Time || second.Cpu?.TemperatureC is null) throw new InvalidOperationException("No fresh CPU reading.");
    client.Dispose(); using var helper = Process.GetProcessById(pid);
    bool exited = helper.WaitForExit(7000);
    JsonFiles.Write(args[2], new { First = first, Second = second, HelperPid = pid, HelperExitedAfterDisconnect = exited, ParentElevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator) });
    return exited ? 0 : 1;
}
if (args.Length == 3 && args[0] is "--inspect-inf" or "--verify-inf-copy")
{
    try
    {
        var devices = DeviceInventory.Read(); var package = InfPackages.Inspect(args[1], devices); bool? copyVerified = null, unrelatedRejected = null;
        if (args[0] == "--verify-inf-copy")
        {
            string copy = Path.Combine(repo, "artifacts", "native-inf-copy-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(copy);
            string inf = PackageFiles.CopyVerified(package, copy); copyVerified = InfPackages.Inspect(inf, devices).Binding == package.Binding;
            try { InfPackages.Inspect(inf, devices.Where(d => d.Class != package.DeviceClass).ToList()); unrelatedRejected = false; } catch (InvalidOperationException) { unrelatedRejected = true; }
            if (copyVerified != true || unrelatedRejected != true) throw new InvalidOperationException("Native staged binding or unrelated-device rejection failed.");
        }
        JsonFiles.Write(args[2], new { Success = true, package.Inf, package.DeviceClass, package.Signer, package.Catalog, Files = package.Files.Count, Matches = package.Matches.Count, Ranks = package.Matches.Select(m => m.Rank), CopyVerified = copyVerified, UnrelatedDevicesRejected = unrelatedRejected }); Console.WriteLine($"Read-only native INF review: {package.Signer}; {package.Matches.Count} compatible nodes; copy verified {copyVerified}."); return 0;
    }
    catch (Exception ex) { JsonFiles.Write(args[2], new { Success = false, Error = ex.ToString() }); Console.Error.WriteLine(ex.Message); return 1; }
}
if (args.Length == 4 && args[0] == "--crash-owner")
{
    int pid = int.Parse(args[1]); var target = Native.Identity(pid);
    using var client = new OperationClient(host, args[2]);
    var reply = await client.Send(new("Freeze", target, JsonFiles.Approve(target.ImagePath), 120));
    JsonFiles.Write(args[3], new { reply.Success, HostPid = client.HostPid });
    await Task.Delay(Timeout.Infinite); return 0;
}
var directory = Path.Combine(repo, "artifacts", "tests", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
Directory.CreateDirectory(directory); int passed = 0, failed = 0;
var report = new List<string>();
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new InvalidOperationException(message); }
void Throws(System.Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException or Win32Exception) { return; } throw new InvalidOperationException("Expected rejection."); }
async Task Test(string name, Func<Task> test)
{
    try { await test(); passed++; report.Add("PASS " + name); Console.WriteLine(report[^1]); }
    catch (Exception ex) { failed++; report.Add("FAIL " + name + ": " + ex); Console.WriteLine(report[^1]); }
}
Task Sync(System.Action action) { action(); return Task.CompletedTask; }
OperationEngine Engine(FakePlatform platform, string name)
{
    return new(platform, new Journal { Host = new(991, 11, "fake-host.exe"), Owner = new(992, 12, "fake-owner.exe"), Pipe = "test" }, Path.Combine(directory, name + ".json"));
}
var fakeTarget = new ProcessIdentity(100, 123, "G:\\fixture.exe");
var fakeApproval = new Approval(fakeTarget.ImagePath, new string('A', 64));
await Test("CPU pipe accepts only bounded nonce, exact arguments and live-process identity shape", () => Sync(() =>
{
    string name = CpuSensorPipe.NewName();
    string[] valid = ["--cpu-pipe", name, "--owner", "1234", "--born", "1234567"];
    Check(CpuSensorPipe.TryParse(valid, out var pipe, out int pid, out long born) && pipe == name && pid == 1234 && born == 1234567);
    foreach (var invalid in new[] { valid.Append("install-driver").ToArray(), new[] { "--cpu-pipe", "../arbitrary", "--owner", "1234", "--born", "1234567" }, new[] { "--cpu-pipe", name, "--owner", "0", "--born", "1234567" }, new[] { "--cpu-pipe", name, "--owner", "1234", "--born", "-1" } })
        Check(!CpuSensorPipe.TryParse(invalid, out _, out _, out _));
}));
await Test("CPU/admin pipe DACL grants only current user and rejects a duplicate first instance", () => Sync(() =>
{
    string name = CpuSensorPipe.NewName(); using var server = SameUserPipe.CreateServer(name);
    var security = server.GetAccessControl(); using var identity = WindowsIdentity.GetCurrent();
    Check(security.AreAccessRulesProtected && security.GetOwner(typeof(SecurityIdentifier)) == identity.User);
    var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<System.IO.Pipes.PipeAccessRule>().ToList();
    Check(rules.Count == 1 && rules[0].IdentityReference == identity.User && rules[0].AccessControlType == System.Security.AccessControl.AccessControlType.Allow);
    bool rejected = false; try { using var duplicate = SameUserPipe.CreateServer(name); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { rejected = true; } Check(rejected);
}));
await Test("CPU peer-validated duplex pipe receives data and disconnect ends only the owned fixture", async () =>
{
    using var client = new CpuTelemetryClient(fixture, probePipe: true);
    for (int i = 0; i < 50 && client.Current.Cpu is null; i++) await Task.Delay(100);
    Check(client.Current.Cpu?.Name == "Fixture pipe CPU", client.Current.Detail);
    int pid = client.HelperPid ?? throw new InvalidOperationException("No CPU fixture PID.");
    using var child = Process.GetProcessById(pid); client.Dispose();
    Check(child.WaitForExit(5000), "CPU pipe helper survived closed parent connection.");
});
await Test("CPU snapshots reject impossible data/status and preserve absent versus zero readings", () => Sync(() =>
{
    var valid = new CpuReading("Fixture CPU", 52, 4200, 0);
    Check(new CpuSnapshot(DateTimeOffset.UtcNow, CpuSensorStatus.Available, valid).Validate().Cpu!.PackagePowerW == 0);
    Check((valid with { TemperatureC = null }).Validate().TemperatureC is null);
    foreach (var invalid in new[] { valid with { TemperatureC = double.NaN }, valid with { PeakClockMHz = -1 }, valid with { PackagePowerW = double.PositiveInfinity }, valid with { Name = null! } }) Throws(() => invalid.Validate());
    Throws(() => new CpuSnapshot(DateTimeOffset.UtcNow, CpuSensorStatus.Available, null).Validate());
    Throws(() => new CpuSnapshot(DateTimeOffset.UtcNow, CpuSensorStatus.SdkNotInstalled, valid).Validate());
    Throws(() => new CpuSnapshot(DateTimeOffset.UtcNow.AddDays(1), CpuSensorStatus.Available, valid).Validate());
}));
await Test("CPU sampler hides stale data and closes its own helper on disposal", async () =>
{
    using var client = new CpuTelemetryClient(fixture);
    for (int attempt = 0; attempt < 30 && client.Current.Cpu is null; attempt++) await Task.Delay(100);
    Check(client.Current.Cpu?.Name == "Fixture CPU");
    var children = Native.Parents().Where(p => p.Value == Environment.ProcessId).Select(p => p.Key).Where(pid => { try { return Native.Identity(pid).ImagePath.Equals(fixture, StringComparison.OrdinalIgnoreCase); } catch { return false; } }).ToList();
    Check(children.Count == 1);
    await Task.Delay(12500); Check(client.Current.Cpu is null && client.Current.Status == CpuSensorStatus.Unavailable);
    client.Dispose();
    for (int attempt = 0; attempt < 30 && children.Any(pid => { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } }); attempt++) await Task.Delay(100);
    Check(children.All(pid => { try { using var p = Process.GetProcessById(pid); return p.HasExited; } catch (ArgumentException) { return true; } }));
});
await Test("Missing CPU helper fails transparently without a reading", async () =>
{
    using var client = new CpuTelemetryClient(Path.Combine(directory, "missing-cpu-helper.exe"));
    for (int i = 0; i < 30 && client.Current.Status == CpuSensorStatus.Starting; i++) await Task.Delay(50);
    Check(client.Current.Status == CpuSensorStatus.Unavailable && client.Current.Cpu is null);
});
await Test("Background selector excludes protected, unknown and other-user processes and preserves consent hashes", () => Sync(() =>
{
    string sid = WindowsIdentity.GetCurrent().User!.Value;
    var profile = Profile.Presets.First(p => p.Mode == ProfileMode.Gaming) with { AllowedApps = [fakeApproval], GamePath = @"G:\game.exe" };
    ProcessRow Row(string path, string owner, bool eligible) => new(new(100, 123, path), Path.GetFileNameWithoutExtension(path), 0, owner, eligible ? "UNKNOWN" : "PROTECTED", "", null, 0, 0, "Normal") { CanApprove = eligible };
    var choices = BackgroundAppSelection.Candidates([Row(fixture, sid, true), Row(fixture, sid, true), Row(@"G:\steam.exe", sid, false), Row(@"G:\other.exe", "other-user", true), Row(@"G:\game.exe", sid, true)], profile, sid);
    Check(choices.Count == 2 && choices.Any(c => c.Path == fixture) && !choices.Any(c => c.Name is "steam" or "game" or "other"));
    Check(BackgroundAppSelection.ApproveSelection(choices, [fakeTarget.ImagePath]).Single() == fakeApproval);
    var selected = BackgroundAppSelection.ApproveSelection(choices, [fixture]); Check(selected.Single() == JsonFiles.Approve(fixture));
    Check(BackgroundAppSelection.ApproveSelection(choices, []).Count == 0);
    Throws(() => BackgroundAppSelection.ApproveSelection(choices, [@"G:\not-offered.exe"]));
}));
await Test("GPU sensor readings reject invalid data and preserve unsupported versus idle zero", () => Sync(() =>
{
    var valid = new GpuReading("Fixture GPU", "fixture", 42, 50, 0, 128, 4096, null, 25, 0, 0, 0);
    Check(valid.Validate().ChipPowerW is null && valid.FanRpm == 0 && valid.UsagePercent == 0);
    foreach (var invalid in new[] { valid with { TemperatureC = double.NaN }, valid with { UsagePercent = 101 }, valid with { UsedMemoryMb = 5000 }, valid with { HotspotC = 1000 }, valid with { FanRpm = -1 }, valid with { Name = null! }, valid with { DeviceKey = "" } })
    { bool refused = false; try { invalid.Validate(); } catch (InvalidDataException) { refused = true; } Check(refused); }
    bool futureRefused = false; try { new GpuSnapshot(DateTimeOffset.UtcNow.AddDays(1), "AMD ADLX", "Available", [valid]).Validate(); } catch (InvalidDataException) { futureRefused = true; } Check(futureRefused);
}));
await Test("GPU child pipe lifetime closes only the owned sampler and stale samples are hidden", async () =>
{
    using var client = new GpuTelemetryClient(fixture);
    for (int attempt = 0; attempt < 30 && client.Current.Gpus.Count == 0; attempt++) await Task.Delay(100);
    Check(client.Current.Gpus.Single().Name == "Fixture GPU");
    var child = Process.GetProcessesByName("OptimizerFixture").Single(p => p.Id != Environment.ProcessId && p.StartTime.ToUniversalTime() > DateTime.UtcNow.AddSeconds(-10));
    using (child)
    {
        await Task.Delay(12500); Check(client.Current.Gpus.Count == 0 && client.Current.Status == "Sensor data stale");
        client.Dispose(); Check(child.WaitForExit(3000), "Owned GPU fixture did not exit on stdin EOF.");
    }
    using var absent = new GpuTelemetryClient(Path.Combine(directory, "missing-sensor-helper.exe"));
    for (int attempt = 0; attempt < 20 && absent.Current.Status == "Starting"; attempt++) await Task.Delay(50);
    Check(absent.Current.Gpus.Count == 0 && absent.Current.Status.StartsWith("Unavailable:", StringComparison.Ordinal));
});
await Test("profiles round trip and restart", () => Sync(() =>
{
    var store = new ProfileStore(Path.Combine(directory, "profiles")); var profiles = store.Load(); Check(profiles.Count == 6);
    profiles[3] = profiles[3] with { AllowedApps = [fakeApproval], GamePath = "G:\\game.exe", DeadlineSeconds = 60 };
    store.Save(profiles); var read = new ProfileStore(Path.Combine(directory, "profiles")).Load(); Check(read[3].AllowedApps[0] == fakeApproval && read[3].GamePath == "G:\\game.exe");
}));
await Test("QPC frame phases separate alt-tab and transition intervals", () => Sync(() =>
{
    var path = Path.Combine(directory, "windows.csv");
    File.WriteAllText(path, "ProcessID,SwapChainAddress,QPCTime,MsBetweenPresents\n100,chain,1000,10\n100,chain,2000,10\n100,chain,4900,10\n100,chain,6000,10\n100,chain,9000,10\n100,chain,11000,10\n100,chain,NA,10\n");
    CapturePhase[] phases = [new("A", 0, 6000, "None"), new("B", 6000, 12000, "Test")];
    FocusObservation[] focus = [new(0, true), new(5000, false), new(8000, true), new(12000, null)];
    var report = FrameWindows.Analyze(path, 100, 1000, phases, focus);
    Check(report[0].AllFrames?.Intervals == 4 && report[0].ForegroundFrames?.Intervals == 2 && report[1].AllFrames?.Intervals == 2 && report[1].ForegroundFrames?.Intervals == 2);
    Throws(() => FrameWindows.Analyze(path, 100, 1000, [phases[1], phases[0]], focus));
    Check(FrameWindows.Analyze(path, 100, 1000, phases, []).All(p => p.ForegroundFrames is null));
}));
await Test("invalid schema / lease / approval / duplicate rejected", () => Sync(() =>
{
    var p = Profile.Presets[0]; Throws(() => (p with { SchemaVersion = 9 }).Validate()); Throws(() => (p with { DeadlineSeconds = 3600 }).Validate());
    Throws(() => (p with { AllowedApps = [new("relative.exe", "junk")] }).Validate()); Throws(() => (p with { AllowedApps = [fakeApproval, fakeApproval] }).Validate());
}));
await Test("unknown JSON fields and oversized JSON rejected", () => Sync(() =>
{
    var path = Path.Combine(directory, "invalid-profile.json"); File.WriteAllText(path, "{\"SchemaVersion\":1,\"Unexpected\":true}"); Throws(() => JsonFiles.Read<Profile>(path));
    File.WriteAllText(path, new string('a', 1048577)); Throws(() => JsonFiles.Read<Profile>(path));
}));
await Test("atomic journal replace and schema validation", () => Sync(() =>
{
    var engine = Engine(new(), "atomic"); for (int i = 0; i < 10; i++) JsonFiles.Write(Path.Combine(directory, "atomic.json"), engine.Journal);
    JsonFiles.Read<Journal>(Path.Combine(directory, "atomic.json")).Validate(); Check(!Directory.GetFiles(directory, "*.tmp").Any());
}));
await Test("classification fails closed and consent cannot override protection", () => Sync(() =>
{
    string sid = WindowsIdentity.GetCurrent().User!.Value;
    Check(ProcessPolicy.Decide(fakeTarget, sid, false, false, false, false).Classification == "UNKNOWN");
    Check(ProcessPolicy.Decide(fakeTarget, "S-1-5-18", false, false, false, true).Classification == "UNKNOWN");
    Check(ProcessPolicy.Decide(fakeTarget, sid, true, false, false, true).Classification == "CRITICAL");
    Check(ProcessPolicy.Decide(fakeTarget, sid, false, true, false, true).Classification == "PROTECTED");
    Check(ProcessPolicy.Decide(fakeTarget with { ImagePath = "G:\\EasyAntiCheat.exe" }, sid, false, false, false, true).Classification == "PROTECTED");
    Check(ProcessPolicy.Decide(fakeTarget, sid, false, false, true, true).Classification == "GAME_DEPENDENCY");
    Check(ProcessPolicy.Decide(fakeTarget, sid, false, false, false, true).Classification == "ELIGIBLE_USER_APP");
    Check(ProcessPolicy.Decide(fakeTarget with { ImagePath = "G:\\ChatGPT.exe" }, sid, false, false, false, true).Classification == "PROTECTED");
    Check(ProcessPolicy.Decide(fakeTarget with { ImagePath = "G:\\AMDRSSrcExt.exe" }, sid, false, false, false, true).Classification == "PROTECTED");
}));
await Test("process trees protect ancestors and descendants without infinite cycles", () => Sync(() =>
{
    var parents = new Dictionary<int, int> { [10] = 2, [11] = 10, [12] = 11, [20] = 21, [21] = 20 };
    Check(ProcessPolicy.Related(12, [10], parents)); Check(ProcessPolicy.Related(10, [12], parents)); Check(!ProcessPolicy.Related(20, [10], parents));
}));
await Test("mock freeze resumes only owned increments and is idempotent", () => Sync(() =>
{
    var platform = new FakePlatform(); platform.Counts[1] = 1;
    var engine = Engine(platform, "counts"); engine.Apply("Freeze", fakeTarget, fakeApproval, 60, []);
    Check(platform.Counts[1] == 2 && platform.Counts[2] == 1); engine.Restore(); Check(platform.Counts[1] == 1 && platform.Counts[2] == 0);
    int calls = platform.ResumeCalls; engine.Restore(); Check(platform.ResumeCalls == calls);
}));
await Test("permission failure causes zero mutations", () => Sync(() =>
{
    var platform = new FakePlatform { Denied = true }; var engine = Engine(platform, "denied"); Throws(() => engine.Apply("Freeze", fakeTarget, fakeApproval, 60, [])); Check(platform.Counts.Values.All(c => c == 0) && engine.Journal.Changes.Count == 0);
}));
await Test("partial freeze failure rolls back confirmed increments", () => Sync(() =>
{
    var platform = new FakePlatform { FailSuspend = 2 }; var engine = Engine(platform, "partial"); Throws(() => engine.Apply("Freeze", fakeTarget, fakeApproval, 60, [])); Check(platform.Counts.Values.All(c => c == 0));
    Check(engine.Journal.Changes.All(c => c.Status == ChangeStatus.Reverted));
}));
await Test("resume failure retained and retry restores once", () => Sync(() =>
{
    var platform = new FakePlatform(); var engine = Engine(platform, "resume-error"); engine.Apply("Freeze", fakeTarget, fakeApproval, 60, []); platform.FailResume = 1; engine.Restore();
    Check(engine.Journal.Changes.Single(c => c.Thread?.Tid == 1).Status == ChangeStatus.Applied); platform.FailResume = -1; engine.Restore(); Check(platform.Counts.Values.All(c => c == 0));
}));
await Test("stale PID is never resumed", () => Sync(() =>
{
    var platform = new FakePlatform(); var engine = Engine(platform, "stale"); engine.Apply("Freeze", fakeTarget, fakeApproval, 60, []); platform.SameIdentity = false; engine.Restore(); Check(platform.ResumeCalls == 0);
}));
await Test("prepared/reverting crash records remain ambiguous", () => Sync(() =>
{
    var platform = new FakePlatform(); var engine = Engine(platform, "ambiguous");
    engine.Journal.Changes.Add(new() { Target = fakeTarget, Kind = "Freeze", Thread = new(1, 100), Status = ChangeStatus.Prepared });
    engine.Journal.Changes.Add(new() { Target = fakeTarget, Kind = "Freeze", Thread = new(2, 200), Status = ChangeStatus.Reverting });
    engine.Restore(); Check(platform.ResumeCalls == 0 && engine.Journal.Changes.All(c => c.Status == ChangeStatus.Attention));
}));
await Test("priority compare-before-restore preserves outside changes", () => Sync(() =>
{
    var platform = new FakePlatform(); var engine = Engine(platform, "priority"); engine.Apply("Priority", fakeTarget, fakeApproval, 60, []); Check(platform.CurrentPriority == 0x4000); engine.Restore(); Check(platform.CurrentPriority == 0x20);
    var e2 = Engine(platform, "priority-conflict"); e2.Apply("Priority", fakeTarget, fakeApproval, 60, []); platform.CurrentPriority = 0x8000; e2.Restore(); Check(platform.CurrentPriority == 0x8000);
}));
await Test("driver vendor exact match rejects substring false positives", () => Sync(() =>
{
    DeviceRow D(string id) => new("GPU", "id", "Display", "", [id], [], 0, "", "", "", "", "Unknown");
    Check(DriverRecommendations.For(D("PCI\\VEN_10DE&DEV_1234")).Provider == "NVIDIA");
    Check(DriverRecommendations.For(D("USB\\NOTPCI\\VEN_10DE&DEV_1234")).Provider == "Windows / OEM");
    Check(DriverRecommendations.For(D("PCI\\VEN_10DEF&DEV_1234")).Provider == "Windows / OEM");
    Check(DriverRecommendations.Match(D("PCI\\VEN_10DE&DEV_1234"), "PCI\\VEN_10DE&DEV_1235").StartsWith("Unmatched"));
    Check(!DriverRecommendations.Trusted(new Uri("https://www.nvidia.com.evil.test/"))); Check(!DriverRecommendations.Trusted(new Uri("http://www.nvidia.com/")));
}));
await Test("cleanup preview size, locked files and changed identity", () => Sync(() =>
{
    var root = Path.Combine(directory, "cleanup"); Directory.CreateDirectory(root); File.WriteAllBytes(Path.Combine(root, "a.tmp"), new byte[123]); File.WriteAllBytes(Path.Combine(root, "b.tmp"), new byte[456]);
    var cleaner = new CleanupService(root); var preview = cleaner.Preview(TimeSpan.Zero); Check(preview.Files.Count == 2 && preview.Files.Sum(f => f.Length) == 579);
    var a = preview.Files.Single(f => f.Path.EndsWith("a.tmp")); using (var locked = File.Open(a.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Check(!cleaner.Delete(a).Deleted);
    File.AppendAllText(a.Path, "changed"); Check(!cleaner.Delete(a).Deleted);
    var b = preview.Files.Single(f => f.Path.EndsWith("b.tmp")); var result = cleaner.Delete(b); Check(result.Deleted && result.Bytes == 456 && !File.Exists(b.Path), result.Message);
    Check(!cleaner.Delete(b with { Path = Path.Combine(directory, "atomic.json") }).Deleted);
}));
await Test("startup toggle restores isolated test-owned HKCU entry", () => Sync(() =>
{
    string name = "SereinFixture-" + Guid.NewGuid().ToString("N"); const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath); key.SetValue(name, "\"G:\\not-launched-fixture.exe\"");
    var manager = new StartupManager();
    try
    {
        var row = manager.Read().Single(r => r.Name == name && r.CanChange); manager.Toggle(row); Check(key.GetValue(name) is null);
        row = manager.Read().Single(r => r.Name == name); Check(!row.Enabled); manager.Toggle(row); Check((string?)key.GetValue(name) == "\"G:\\not-launched-fixture.exe\"");
    }
    finally { key.DeleteValue(name, false); using var backup = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Serein\DisabledRun", true); backup?.DeleteValue(name, false); }
}));
await Test("bounded IPC refuses oversized frames", async () =>
{
    using var stream = new MemoryStream(BitConverter.GetBytes(1048577));
    try { await Wire.Read<OperationRequest>(stream, CancellationToken.None); throw new InvalidOperationException("Bad frame accepted."); } catch (InvalidDataException) { }
});
await Test("read-only disk analyzer exact fixture totals and cap", () => Sync(() =>
{
    string root = Path.Combine(directory, "analyzer"); Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root, "child"));
    File.WriteAllBytes(Path.Combine(root, "small"), new byte[12]); File.WriteAllBytes(Path.Combine(root, "child", "large"), new byte[1234]);
    var scan = DiskAnalyzer.Read(root); Check(scan.Complete && scan.ObservedBytes == 1246 && scan.Files[0].Bytes == 1234); Check(!DiskAnalyzer.Read(root, 1).Complete);
    Check(File.Exists(Path.Combine(root, "child", "large")));
}));
await Test("driver backup has fixed export-only argument vector and rejects nonempty destination", () => Sync(() =>
{
    string root = Path.Combine(directory, "backup-fixture"); Directory.CreateDirectory(root);
    var request = DriverBackup.Prepare(root); Check(request.FileName.EndsWith("\\pnputil.exe", StringComparison.OrdinalIgnoreCase));
    Check(request.ArgumentList.SequenceEqual(new[] { "/export-driver", "*", root }) && request.Verb == "runas" && request.UseShellExecute);
    File.WriteAllText(Path.Combine(root, "existing.inf"), "fixture"); Throws(() => DriverBackup.Prepare(root));
    // Never execute the actual export/UAC during the developer suite.
}));
await Test("preferences persistence and duplicate profile rejection leave original intact", () => Sync(() =>
{
    string path = Path.Combine(directory, "preferences.json"); var prefs = new Preferences(1, true, true, false, false); JsonFiles.Write(path, prefs); Check(JsonFiles.Read<Preferences>(path) == prefs);
    var store = new ProfileStore(Path.Combine(directory, "unique-profiles")); var original = store.Load(); Throws(() => store.Save(original.Append(original[0]).ToList())); Check(store.Load().Count == 6);
}));
await Test("appearance preferences preserve custom accent and reduced motion with legacy defaults and invalid HEX refusal", () => Sync(() =>
{
    string path = Path.Combine(directory, "appearance.json"); var prefs = new Preferences(AccentColor: "#F29BC1", ReducedMotion: true); prefs.Validate(); JsonFiles.Write(path, prefs); Check(JsonFiles.Read<Preferences>(path) == prefs);
    File.WriteAllText(path, "{\"SchemaVersion\":1,\"English\":false,\"LightTheme\":false,\"CloseToTray\":true,\"Notifications\":true}"); var legacy = JsonFiles.Read<Preferences>(path); legacy.Validate(); Check(legacy.AccentColor == "#8B9FFF" && !legacy.ReducedMotion);
    foreach (string invalid in new[] { "red", "#FFF", "#00000000", "#12345G", "" }) Throws(() => (prefs with { AccentColor = invalid }).Validate());
    Throws(() => (prefs with { AccentColor = null! }).Validate());
}));
await Test("frame statistics separate swapchains and define 1% low without averaging FPS", () => Sync(() =>
{
    var accumulator = new FrameAccumulator("Application,ProcessID,SwapChainAddress,msBetweenPresents", 100);
    for (int i = 0; i < 99; i++) accumulator.Add("\"fixture,quoted\",100,0x1,10");
    accumulator.Add("fixture,100,0x1,100"); accumulator.Add("other,200,0x2,0.001"); accumulator.Add("fixture,100,0x3,1"); accumulator.Add("fixture,100,0x1,NA");
    var result = accumulator.Snapshot()!; Check(result.Intervals == 100 && result.SwapChain == "0x1" && Math.Abs(result.AverageFps - 100000d / 1090) < .0001 && result.Low1PercentFps == 10 && result.P99Ms == 10 && result.IgnoredRows == 2);
    Throws(() => new FrameAccumulator("ProcessID,CPUTime"));
}));
await Test("actual read-only processes, SCM/WMI, devices and telemetry", async () =>
{
    using var own = Native.OpenProcess(Native.Query, false, Environment.ProcessId); Check(Native.GetProcessInformation(own, 7, out uint protection, 4) && protection == 0xFFFFFFFE, "Process protection capability unavailable on this build.");
    var rows = new ProcessInventory().Read(); Check(rows.Any(r => r.Pid == Environment.ProcessId));
    var services = ServiceInventory.Read(); Check(services.Count > 0);
    var devices = DeviceInventory.Read(); Check(devices.Count > 0 && devices.Any(d => d.HardwareIds.Length > 0));
    using var sampler = new TelemetrySampler(); sampler.Sample(); await Task.Delay(1100); var sample = sampler.Sample();
    Check(sample.TotalMemory > 0 && sample.AvailableMemory > 0 && sample.CpuPercent is >= 0 and <= 100);
    JsonFiles.Write(Path.Combine(directory, "read-only-inventory.json"), new { OS = Environment.OSVersion.Version.ToString(), Processes = rows.Count, Services = services.Count, Devices = devices.Count, Sample = sample });
});
await Test("unsigned fixture trust and hash replacement rejected", () => Sync(() =>
{
    var signature = SignatureVerifier.Read(fixture); Check(signature.Status.StartsWith("Not verified"));
    var copy = Path.Combine(directory, "identity.exe"); File.Copy(fixture, copy); var approval = JsonFiles.Approve(copy);
    using (var stream = new FileStream(copy, FileMode.Append, FileAccess.Write)) stream.WriteByte(123);
    Throws(() => ProcessPolicy.ValidateApproval(fakeTarget with { ImagePath = copy }, approval));
}));
await Test("SCM dependency inspection is read-only and signed publisher is extracted", () => Sync(() =>
{
    var services = ServiceInventory.Read(); var spooler = services.FirstOrDefault(s => s.Name == "Spooler");
    if (spooler is null) throw new InvalidOperationException("Spooler service unavailable for read-only fixture assertion.");
    var details = ServiceInspector.Read(spooler.Name); Check(details.BinaryPath.Length > 0 && details.Dependencies.Any(d => d.Equals("RPCSS", StringComparison.OrdinalIgnoreCase)));
    var executable = Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"), "dotnet.exe");
    var signature = SignatureVerifier.Read(executable); Check(signature.Publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase), signature.Publisher + " " + signature.Status);
    JsonFiles.Write(Path.Combine(directory, "signature-scm.json"), new { Details = details, Signature = signature });
}));
async Task<Process> SpawnFixture(string label)
{
    string heartbeat = Path.Combine(directory, label + ".heartbeat"), pidfile = Path.Combine(directory, label + ".pid");
    var start = new ProcessStartInfo(fixture) { UseShellExecute = false, CreateNoWindow = true }; foreach (var arg in new[] { "--spawn", heartbeat, pidfile }) start.ArgumentList.Add(arg);
    using var launcher = Process.Start(start)!; await launcher.WaitForExitAsync(); Check(launcher.ExitCode == 0);
    var child = Process.GetProcessById(int.Parse(File.ReadAllText(pidfile)));
    await Until(() => File.Exists(heartbeat) && new FileInfo(heartbeat).Length > 50, TimeSpan.FromSeconds(10)); return child;
}
async Task Until(Func<bool> predicate, TimeSpan timeout)
{
    var clock = Stopwatch.StartNew(); while (!predicate()) { if (clock.Elapsed > timeout) throw new TimeoutException("Fixture condition timed out."); await Task.Delay(100); }
}
long Heartbeat(string name) => new FileInfo(Path.Combine(directory, name + ".heartbeat")).Length;
void KillFixture(Process p) { if (!p.HasExited) p.Kill(); p.WaitForExit(5000); }
await Test("real freeze heartbeat + resume + priority + duplicate restore", async () =>
{
    using var target = await SpawnFixture("real"); using var client = new OperationClient(host, Path.Combine(directory, "real-session"));
    try
    {
        // Test-owned children inherit the runner's priority; establish the state being tested.
        target.PriorityClass = ProcessPriorityClass.Normal;
        var identity = Native.Identity(target.Id); var approval = JsonFiles.Approve(fixture);
        var reply = await client.Send(new("Priority", identity, approval, 60)); Check(reply.Success, reply.Message); target.Refresh(); Check(target.PriorityClass == ProcessPriorityClass.BelowNormal);
        await client.Send(new("Restore")); target.Refresh(); Check(target.PriorityClass == ProcessPriorityClass.Normal);
        reply = await client.Send(new("Freeze", identity, approval, 60)); Check(reply.Success, reply.Message);
        long before = Heartbeat("real"); await Task.Delay(650); Check(Heartbeat("real") == before, "Heartbeat did not stop.");
        var wrong = await client.Send(new("Terminate", identity, approval)); Check(!wrong.Success, "Unsupported IPC action accepted.");
        await client.Send(new("Restore")); await Until(() => Heartbeat("real") > before, TimeSpan.FromSeconds(5)); await client.Send(new("Restore"));
        var status = await client.Send(new("Status")); Check(status.Changes.All(c => c.Status == ChangeStatus.Reverted));
    }
    finally { try { await client.Send(new("Exit")); } finally { KillFixture(target); } }
});
await Test("watchdog monotonic timeout resumes real target", async () =>
{
    using var target = await SpawnFixture("timeout"); using var client = new OperationClient(host, Path.Combine(directory, "timeout-session"));
    try { var reply = await client.Send(new("Freeze", Native.Identity(target.Id), JsonFiles.Approve(fixture), 5)); Check(reply.Success, reply.Message); long before = Heartbeat("timeout"); await Until(() => Heartbeat("timeout") > before, TimeSpan.FromSeconds(9)); }
    finally { try { await client.Send(new("Exit")); } finally { KillFixture(target); } }
});
await Test("measurement A/B/A uses real engine and restores an owned background fixture", async () =>
{
    using var target = await SpawnFixture("aba");
    string folder = Path.Combine(directory, "aba-capture"), profilePath = Path.Combine(directory, "aba-profile.json");
    var game = Native.Identity(Environment.ProcessId);
    JsonFiles.Write(profilePath, new Profile(1, "Owned A/B/A test", ProfileMode.Gaming, 60, [JsonFiles.Approve(fixture)], game.ImagePath));
    using var scenario = new MeasurementScenario(folder, game, profilePath, null, host);
    try
    {
        target.PriorityClass = ProcessPriorityClass.Normal;
        await scenario.Advance(21, 60); target.Refresh(); Check(target.PriorityClass == ProcessPriorityClass.BelowNormal && scenario.Current == "B");
        await Task.Delay(2100); await scenario.Advance(41, 60); target.Refresh(); Check(target.PriorityClass == ProcessPriorityClass.Normal && scenario.Current == "A2");
        await Task.Delay(2100); scenario.Complete(); Check(scenario.Phases.Select(p => p.Name).SequenceEqual(new[] { "A1", "B", "A2" }));
        int operations = scenario.Operations.Count; await scenario.Restore(); Check(scenario.Operations.Count == operations);
    }
    finally { try { await scenario.Restore(); } finally { KillFixture(target); } }
});
await Test("UI/controller crash detected by independent watchdog", async () =>
{
    using var target = await SpawnFixture("crash"); string session = Path.Combine(directory, "crash-session"), ready = Path.Combine(directory, "owner-ready.json");
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    foreach (var arg in new[] { "--crash-owner", target.Id.ToString(), session, ready }) start.ArgumentList.Add(arg);
    using var owner = Process.Start(start)!; int watchdogPid = 0;
    try
    {
        await Until(() => File.Exists(ready), TimeSpan.FromSeconds(15)); using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(ready));
        Check(json.RootElement.GetProperty("Success").GetBoolean()); watchdogPid = json.RootElement.GetProperty("HostPid").GetInt32();
        long before = Heartbeat("crash"); await Task.Delay(400); Check(Heartbeat("crash") == before);
        owner.Kill(); await owner.WaitForExitAsync(); await Until(() => Heartbeat("crash") > before, TimeSpan.FromSeconds(8));
        await Until(() => Directory.GetFiles(session, "*.json").All(p => JsonFiles.Read<Journal>(p).Changes.All(c => c.Status == ChangeStatus.Reverted)), TimeSpan.FromSeconds(5));
    }
    finally { if (!owner.HasExited) { owner.Kill(); await owner.WaitForExitAsync(); } KillFixture(target); if (watchdogPid > 0) { try { using var p = Process.GetProcessById(watchdogPid); p.WaitForExit(5000); } catch (ArgumentException) { } } }
});
await Test("confirmed worker crash journal reconciles without over-resume", async () =>
{
    using var target = await SpawnFixture("worker-crash"); string session = Path.Combine(directory, "worker-crash-session"); using var client = new OperationClient(host, session);
    try
    {
        var reply = await client.Send(new("Freeze", Native.Identity(target.Id), JsonFiles.Approve(fixture), 60)); Check(reply.Success, reply.Message);
        long before = Heartbeat("worker-crash"); using var worker = Process.GetProcessById(client.HostPid!.Value); worker.Kill(); await worker.WaitForExitAsync();
        await Task.Delay(300); Check(Heartbeat("worker-crash") == before);
        var results = Recovery.Reconcile(session); Check(results.Count == 1 && results[0].Contains("0 unresolved"), string.Join("; ", results));
        await Until(() => Heartbeat("worker-crash") > before, TimeSpan.FromSeconds(5)); Recovery.Reconcile(session);
    }
    finally { KillFixture(target); }
});
await Test("synthetic game detection applies profile and restores after exit", async () =>
{
    using var game = await SpawnFixture("game"); using var background = await SpawnFixture("background");
    using var client = new OperationClient(host, Path.Combine(directory, "game-session"));
    try
    {
        var tracker = new GameSessionTracker(); var gameId = Native.Identity(game.Id); var bgId = Native.Identity(background.Id);
        // Both fixture processes have the same image, so use exact synthetic game rows to validate lifecycle.
        ProcessRow Row(ProcessIdentity id) => new(id, "fixture", 0, "", "UNKNOWN", "test-owned", 0, 0, 0, "Normal");
        var transition = tracker.Update(fixture, [Row(gameId)]); Check(transition.Started && tracker.Roots.Contains(game.Id));
        var reply = await client.Send(new("Freeze", bgId, JsonFiles.Approve(fixture), 60, tracker.Roots.ToArray())); Check(reply.Success, reply.Message);
        long before = Heartbeat("background"); await Task.Delay(400); Check(Heartbeat("background") == before);
        var protectedReply = await client.Send(new("Freeze", gameId, JsonFiles.Approve(fixture), 60, tracker.Roots.ToArray())); Check(!protectedReply.Success, "Game was not protected.");
        KillFixture(game); Check(tracker.Update(fixture, []).Ended); await client.Send(new("Restore")); await Until(() => Heartbeat("background") > before, TimeSpan.FromSeconds(5));
    }
    finally { try { await client.Send(new("Exit")); } finally { KillFixture(game); KillFixture(background); } }
});
await Test("power policy API inventory is read-only and optional profile survives restart", () => Sync(() =>
{
    var platform = new WindowsPowerPlatform(); Guid before = platform.Current(); Check(platform.Schemes().Any(p => p.Id == before)); Check(platform.Current() == before);
    var p = Profile.Presets[2] with { PowerScheme = before }; p.Validate(); var file = Path.Combine(directory, "power-profile.json"); JsonFiles.Write(file, p); Check(JsonFiles.Read<Profile>(file).PowerScheme == before);
    Throws(() => (p with { PowerScheme = Guid.Empty }).Validate());
}));
await Test("native power ownership mutex excludes overlap and releases across async continuation without switching schemes", async () =>
{
    var platform = new WindowsPowerPlatform(); Guid before = platform.Current();
    using (platform.Acquire()) { await Task.Delay(30); Throws(() => platform.Acquire()); }
    using (platform.Acquire()) { Check(platform.Current() == before); }
});
await Test("mock power changes restore original, preserve external choice and are idempotent", () => Sync(() =>
{
    var power = new FakePower(); var controller = new PowerLeaseController(power, Path.Combine(directory, "power-restore.power"));
    var hostId = new ProcessIdentity(99999, 1, "fixture-host.exe"); var ownerId = new ProcessIdentity(99998, 2, "fixture-owner.exe");
    controller.Apply(power.Target, 60, hostId, ownerId); Check(power.Active == power.Target && controller.Lease?.Status == ChangeStatus.Applied);
    controller.Restore(); Check(power.Active == power.Original && power.Writes == 2 && power.Releases == 1); controller.Restore(); Check(power.Writes == 2);
    controller.Apply(power.Target, 60, hostId, ownerId); power.Active = power.External; controller.Restore(); Check(power.Active == power.External && power.Writes == 3 && controller.Lease?.Error?.Contains("External", StringComparison.Ordinal) == true);
}));
await Test("mock power denied selection never leaves an active lease", () => Sync(() =>
{
    var power = new FakePower { FailSelect = true }; var controller = new PowerLeaseController(power, Path.Combine(directory, "power-denied.power"));
    Throws(() => controller.Apply(power.Target, 60, new(99999, 1, "host.exe"), new(99998, 2, "owner.exe")));
    Check(power.Active == power.Original && controller.Lease?.Status == ChangeStatus.Reverted && power.Releases == 1);
}));
await Test("mock power deadline restores without changing host power settings", async () =>
{
    var power = new FakePower(); var controller = new PowerLeaseController(power, Path.Combine(directory, "power-ttl.power"));
    controller.Apply(power.Target, 5, new(99999, 1, "host.exe"), new(99998, 2, "owner.exe")); await Task.Delay(5200); controller.RestoreExpired(); Check(power.Active == power.Original && controller.Lease?.Status == ChangeStatus.Reverted);
});
await Test("power failed snapshot after a previous lease releases ownership without reapplying stale state", () => Sync(() =>
{
    var power = new FakePower(); var controller = new PowerLeaseController(power, Path.Combine(directory, "power-read-failure.power"));
    var host = new ProcessIdentity(99999, 1, "host.exe"); var owner = new ProcessIdentity(99998, 2, "owner.exe");
    controller.Apply(power.Target, 60, host, owner); controller.Restore(); power.FailRead = true;
    Throws(() => controller.Apply(power.Target, 60, host, owner));
    Check(power.Releases == 2 && power.Writes == 2 && controller.Lease is null);
}));
await Test("power crash reconciliation recovers confirmed state and retains ambiguous intent", () => Sync(() =>
{
    var power = new FakePower { Active = new("22222222-2222-2222-2222-222222222222") }; string root = Path.Combine(directory, "power-crash"); Directory.CreateDirectory(root);
    var lease = new PowerLease { Host = new(99999, 1, "host.exe"), Owner = new(99998, 2, "owner.exe"), Before = power.Original, Requested = power.Target, Status = ChangeStatus.Applied, DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(60) };
    string file = Path.Combine(root, "fixture.power"); JsonFiles.Write(file, lease);
    PowerLeaseController.Recover(root, power, _ => false); Check(power.Active == power.Original && power.Writes == 1 && PowerLeaseController.Pending(root).Count == 0);
    power.Active = power.Target; JsonFiles.Write(file, lease with { Status = ChangeStatus.Prepared }); PowerLeaseController.Recover(root, power, _ => false); Check(power.Writes == 1 && PowerLeaseController.Pending(root).Single().Status == ChangeStatus.Attention);
    power.Active = power.Original; PowerLeaseController.Recover(root, power, _ => false); Check(PowerLeaseController.Pending(root).Count == 0 && power.Writes == 1);
}));
await Test("recovery retention keeps unresolved/live/corrupt journals and newest completed records", () => Sync(() =>
{
    string root = Path.Combine(directory, "retention"); Directory.CreateDirectory(root);
    for (int i = 0; i < 7; i++)
    {
        var journal = new Journal { Host = new(99999, 1, "fixture.exe"), Owner = new(99998, 2, "owner.exe"), Pipe = "fixture" };
        if (i == 6) journal.Changes.Add(new() { Target = fakeTarget, Kind = "Priority", Before = 0x20, Requested = 0x4000, Status = ChangeStatus.Applied });
        var file = Path.Combine(root, i + ".json"); JsonFiles.Write(file, journal); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-10 + i));
    }
    File.WriteAllText(Path.Combine(root, "corrupt.json"), "broken");
    var result = RecoveryRetention.Prune(root, 2, _ => false); Check(result.Deleted == 4 && File.Exists(Path.Combine(root, "6.json")) && File.Exists(Path.Combine(root, "corrupt.json")) && File.Exists(Path.Combine(root, "5.json")));
    Check(RecoveryRetention.Prune(root, 1, _ => true).Deleted == 0);
}));
var driverChoice = new DriverCandidate("Fixture network driver", "PCI\\VEN_1234&DEV_5678", "Fixture vendor", "2026-01-01", "11111111-2222-3333-4444-555555555555", "Fixture", 3, "Net", "Fixture EULA", false, "Fixture configured service");
await Test("driver revision/EULA/source binding and firmware refusal", () => Sync(() =>
{
    DriverInstallPolicy.ValidateCandidate(driverChoice);
    Throws(() => DriverInstallPolicy.SameSelection(driverChoice, driverChoice with { Revision = 4 }));
    Throws(() => DriverInstallPolicy.SameSelection(driverChoice, driverChoice with { Eula = "Changed" }));
    Throws(() => DriverInstallPolicy.SameSelection(driverChoice, driverChoice with { Service = "Other source" }));
    Throws(() => DriverInstallPolicy.ValidateCandidate(driverChoice with { DeviceClass = "Firmware" }));
    Throws(() => DriverInstallPolicy.ValidateCandidate(driverChoice with { Title = "OEM BIOS update" }));
    Throws(() => DriverInstaller.Validate(new("InstallWindowsUpdate", false, true, driverChoice)));
    Throws(() => DriverInstaller.Validate(new("ArbitraryCommand", true)));
    Throws(() => DriverInstallPolicy.CanInstall(true, false, true, true)); Throws(() => DriverInstallPolicy.CanInstall(false, true, true, true)); Throws(() => DriverInstallPolicy.CanInstall(false, false, false, false));
    Check(!DriverInstallPolicy.Result("partial", 3, 2, 0).Success && !DriverInstallPolicy.Result("failed", 2, 2, -1).Success);
}));
await Test("driver install resolves exact identity, backs up, downloads and returns reboot without restarting", () => Sync(() =>
{
    var platform = new FakeDriverPlatform(); var source = new FakeDriverUpdate(driverChoice);
    var reply = new DriverInstaller(platform, () => source).Execute(new("InstallWindowsUpdate", true, true, driverChoice), Path.Combine(directory, "driver-success"), () => true);
    Check(reply.Success && reply.RebootRequired && platform.Calls.SequenceEqual(new[] { "Backup" }) && source.Calls.SequenceEqual(new[] { "Resolve", "Download", "Install" }));
    Check(File.ReadAllText(reply.ReportPath!).Contains("Installed", StringComparison.Ordinal));
}));
await Test("driver backup failure and partial download prevent commit", () => Sync(() =>
{
    var platform = new FakeDriverPlatform { BackupFails = true }; var source = new FakeDriverUpdate(driverChoice);
    var installer = new DriverInstaller(platform, () => source); var request = new DriverInstallRequest("InstallWindowsUpdate", true, true, driverChoice);
    Check(!installer.Execute(request, Path.Combine(directory, "driver-backup-fault"), () => true).Success && !source.Calls.Contains("Download"));
    platform.BackupFails = false; source.DownloadFails = true;
    Check(!installer.Execute(request, Path.Combine(directory, "driver-download-fault"), () => true).Success && !source.Calls.Contains("Install"));
    source.DownloadFails = false; source.InstallFails = true;
    Check(!installer.Execute(request, Path.Combine(directory, "driver-install-fault"), () => true).Success);
}));
await Test("driver changed revision, missing EULA consent and dead controller prevent mutations", () => Sync(() =>
{
    var platform = new FakeDriverPlatform(); var source = new FakeDriverUpdate(driverChoice) { Candidate = driverChoice with { Revision = 4 } };
    var installer = new DriverInstaller(platform, () => source); var request = new DriverInstallRequest("InstallWindowsUpdate", true, true, driverChoice);
    Check(!installer.Execute(request, Path.Combine(directory, "driver-changed"), () => true).Success && platform.Calls.Count == 0);
    source.Candidate = driverChoice;
    Check(!installer.Execute(request with { AcceptEula = false }, Path.Combine(directory, "driver-eula"), () => true).Success && platform.Calls.Count == 0);
    Check(!installer.Execute(request, Path.Combine(directory, "driver-owner-exit"), () => false).Success && platform.Calls.Count == 0);
}));
await Test("INF native layouts and unsigned catalog rejection", () => Sync(() =>
{
    Check(InfPackages.Layouts == (32, 584, 1568, 1568), InfPackages.Layouts.ToString()); Check(InfPackages.RankLayout == 32);
    string inf = Path.Combine(directory, "unsigned.inf"); File.WriteAllText(inf, "[Version]\nSignature=\"$Windows NT$\"\nClass=Net\n");
    Throws(() => InfPackages.Inspect(inf, []));
}));
await Test("sealed INF package manifest refuses traversal, replacement and unexpected extra files", () => Sync(() =>
{
    string packageFolder = Path.Combine(directory, "inf-source"); Directory.CreateDirectory(packageFolder); File.WriteAllText(Path.Combine(packageFolder, "fixture.inf"), "fixture"); File.WriteAllText(Path.Combine(packageFolder, "fixture.cat"), "catalog fixture");
    var package = new InfPackage(packageFolder, "fixture.inf", "Net", "Fixture", "fixture.cat", PackageFiles.Snapshot(packageFolder), [new("ROOT\\FIXTURE", "Fixture", "Vendor", "1.0.0.0", 1)]);
    string staged = Path.Combine(directory, "inf-staged"); Directory.CreateDirectory(staged); PackageFiles.CopyVerified(package, staged);
    Check(PackageFiles.Snapshot(staged).SequenceEqual(package.Files));
    var accepted = new FakeDriverPlatform { Package = package };
    var installed = new DriverInstaller(accepted, () => throw new InvalidOperationException()).Execute(new("InstallInf", true, Package: package), Path.Combine(directory, "inf-success"), () => true);
    Check(installed.Success && installed.State == "StagedAndWindowsSelectionCompleted" && accepted.Calls.SequenceEqual(new[] { "InspectSealed", "Backup", "CommitInf" }));
    foreach (string unsafePath in new[] { "../escape", "C:\\absolute", "member:stream", "nested\\..\\escape", "*.inf" }) Throws(() => PackageFiles.Relative(unsafePath));
    File.WriteAllText(Path.Combine(packageFolder, "fixture.cat"), "tampered"); Throws(() => PackageFiles.Verify(packageFolder, package.Files));
    var platform = new FakeDriverPlatform { Package = package };
    Check(!new DriverInstaller(platform, () => throw new InvalidOperationException()).Execute(new("InstallInf", true, Package: package), Path.Combine(directory, "inf-changed"), () => true).Success && platform.Calls.Count == 0);
    var install = NativeDriverInstallPlatform.Command("/add-driver", Path.Combine(staged, "fixture.inf")); Check(!install.UseShellExecute && install.ArgumentList.SequenceEqual(new[] { "/add-driver", Path.Combine(staged, "fixture.inf"), "/install" }));
    Throws(() => NativeDriverInstallPlatform.Command("/delete-driver", "oem1.inf")); Throws(() => NativeDriverInstallPlatform.Command("/add-driver", "C:\\*.inf"));
}));
await Test("OEM and component sources match exact aliases/IDs and reject lookalike hosts", () => Sync(() =>
{
    Check(DriverSources.Catalog.Count >= 15 && DriverSources.Catalog.All(s => DriverSources.Trusted(s.Url)));
    Check(DriverSources.Brand("Dell Inc.") == "Dell" && DriverSources.Brand("Micro-Star International Co., Ltd.") == "MSI" && DriverSources.Brand("NotDellInc") is null);
    Check(!DriverSources.Trusted(new("https://www.dell.com.evil.test/")) && !DriverSources.DownloadAllowed(new("https://dl.dell.com@evil.test/file.zip")) && !DriverSources.DownloadAllowed(new("http://dl.dell.com/file.zip")));
    var device = new DeviceRow("Fixture", "PCI\\fixture", "Net", "", ["PCI\\VEN_10EC&DEV_1234"], [], 0, "", "", "", "", "");
    var sources = DriverSources.For(device, new("Dell Inc.", "Fixture model", "", "")); Check(sources[0].Name == "Realtek" && sources.All(s => s.Name != "Dell") && sources[^1].Url.AbsoluteUri.Contains("Search.aspx", StringComparison.Ordinal));
    Check(!DriverSources.For(device with { HardwareIds = ["PCI\\VEN_110EC&DEV_1234"] }, null).Any(s => s.Name == "Realtek"));
}));
await Test("official download provenance uses fixture transport and never executes content", async () =>
{
    string folder = Path.Combine(directory, "download-ok"); Directory.CreateDirectory(folder);
    var result = await DriverDownloads.Fetch(new("https://dl.dell.com/fixture.zip?secret=excluded"), folder, new FixtureHttp(_ => new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }));
    Check(result.Bytes == 3 && File.ReadAllBytes(result.Path).SequenceEqual(new byte[] { 1, 2, 3 }) && !File.ReadAllText(Path.Combine(folder, "provenance.json")).Contains("secret", StringComparison.Ordinal));
    foreach (bool redirect in new[] { true, false })
    {
        string bad = Path.Combine(directory, "download-bad-" + redirect); Directory.CreateDirectory(bad); bool denied = false;
        try
        {
            await DriverDownloads.Fetch(new("https://dl.dell.com/fixture.zip"), bad, new FixtureHttp(_ => redirect ? new(System.Net.HttpStatusCode.Redirect) { Headers = { Location = new("https://evil.test/payload.exe") } } : new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[50]) }), maxBytes: 20);
        }
        catch (InvalidDataException) { denied = true; }
        Check(denied && Directory.GetFiles(bad).Length == 0);
    }
});
await Test("temporary driver broker authenticates exact pipe peers in non-elevating probe mode", async () =>
{
    string admin = Path.Combine(repo, "src/Serein.Admin/bin/Release/net10.0-windows/Serein.Admin.exe");
    var reply = await DriverAdminClient.Probe(admin); Check(reply.Success && reply.State == "Probe" && reply.ReportPath is null && !reply.RebootRequired);
});
JsonFiles.Write(Path.Combine(directory, "results.json"), new { Passed = passed, Failed = failed, Tests = report });
Console.WriteLine($"{passed} passed, {failed} failed. Evidence: {directory}");
return failed == 0 ? 0 : 1;
