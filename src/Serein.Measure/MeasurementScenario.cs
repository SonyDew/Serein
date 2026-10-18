using System.Diagnostics;
using Serein.Core;

internal sealed class MeasurementScenario : IDisposable
{
    private readonly string output;
    private readonly ProcessIdentity game;
    private readonly List<(ProcessIdentity Identity, Approval Approval)> targets = [];
    private readonly OperationClient client;
    private readonly string? action, trayPath;
    private Process? tray;
    private ProcessIdentity? trayIdentity;
    private long trayStarted;
    private bool restored;
    private string current = "A1";
    private long start = Stopwatch.GetTimestamp();
    public List<CapturePhase> Phases { get; } = [];
    public List<object> Operations { get; } = [];
    public string Description { get; }
    public string Current => current;
    public MeasurementScenario(string output, ProcessIdentity game, string? profilePath, string? trayPath, string? hostPath = null)
    {
        this.output = output; this.game = game; this.trayPath = trayPath;
        client = new(hostPath ?? Path.Combine(AppContext.BaseDirectory, "Serein.Host.exe"), Path.Combine(output, "recovery"));
        if (profilePath is not null)
        {
            var profile = JsonFiles.Read<Profile>(profilePath); profile.Validate();
            if (profile.PowerScheme is not null) throw new InvalidDataException("The measurement console currently compares process policies only. Export a profile without a power scheme; power policies are supported in the desktop application.");
            if (profile.GamePath is null || !profile.GamePath.Equals(game.ImagePath, StringComparison.OrdinalIgnoreCase) || profile.AllowedApps.Count == 0) throw new InvalidDataException("Comparison requires the exact current game path and nonempty explicit approvals.");
            action = profile.Mode == ProfileMode.ExtremeGaming ? "Freeze" : "Priority";
            var inventory = new ProcessInventory().Read(profile.AllowedApps, [game.Pid, Environment.ProcessId]);
            foreach (var approval in profile.AllowedApps)
            {
                var matching = inventory.Where(p => p.Path.Equals(approval.Path, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matching.Count == 0 || matching.Any(p => p.Classification != "ELIGIBLE_USER_APP")) throw new InvalidOperationException("Approved application absent or protected: " + approval.Path);
                foreach (var row in matching)
                {
                    using var platform = new WindowsOperations(); platform.ValidateTarget(row.Identity, approval, [game.Pid, Environment.ProcessId]);
                    targets.Add((row.Identity, approval));
                }
            }
            if (targets.Count > 16) throw new InvalidDataException("Live comparison is bounded to 16 approved instances.");
            Description = action + " on explicitly approved targets; exact lifetime and watchdog restoration";
            JsonFiles.Write(Path.Combine(output, "operation-preview.json"), new { Action = action, Game = game, Targets = targets.Select(t => new { t.Identity, t.Approval }) });
        }
        else if (trayPath is not null)
        {
            if (!Path.GetFileName(trayPath).Equals("Serein.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(trayPath)) throw new InvalidDataException("Choose the built Serein.exe.");
            var existing = Process.GetProcessesByName("Serein"); bool running = existing.Length > 0; foreach (var p in existing) p.Dispose();
            if (running) throw new InvalidOperationException("Serein is already running; cannot create an owned overhead comparison.");
            string folder = Path.GetDirectoryName(Path.GetFullPath(trayPath))!;
            JsonFiles.Write(Path.Combine(output, "app-components.json"), new[] { "Serein.exe", "Serein.dll", "Serein.Core.dll", "WindowsBase.dll" }.Select(name => JsonFiles.Approve(Path.Combine(folder, name))).ToList());
            Description = "Tray application overhead only; no optimization operations";
        }
        else { current = "Baseline"; Description = "None / read-only baseline"; }
    }
    public async Task Advance(double seconds, int duration)
    {
        if (current == "B" && trayPath is not null && (tray is null || tray.HasExited || trayIdentity is null || !Native.Matches(trayIdentity))) throw new InvalidOperationException("Owned tray app exited during B; comparison is invalid.");
        if (action is null && trayPath is null) return;
        if (current == "A1" && seconds >= duration / 3d)
        {
            ClosePhase("Optimizer off"); current = "Transition";
            if (action is not null)
            {
                foreach (var target in targets)
                {
                    var reply = await client.Send(new(action, target.Identity, target.Approval, Math.Clamp(duration, 5, 1800), [game.Pid]));
                    Operations.Add(new { AtUtc = DateTimeOffset.UtcNow, Action = action, Target = target.Identity, Reply = reply });
                    JsonFiles.Write(Path.Combine(output, "operations.json"), Operations);
                    if (!reply.Success) throw new InvalidOperationException(reply.Message);
                }
                if (!Operations.Any() || !(await client.Send(new("Status"))).Changes.Any(c => c.Status == ChangeStatus.Applied)) throw new InvalidOperationException("No actual optimization change applied; comparison aborted.");
            }
            else
            {
                var launch = new ProcessStartInfo(trayPath!) { UseShellExecute = false, CreateNoWindow = true }; launch.ArgumentList.Add("--tray");
                launch.ArgumentList.Add("--test-data"); launch.ArgumentList.Add(Path.Combine(output, "tray-state"));
                tray = Process.Start(launch) ?? throw new InvalidOperationException("Tray application launch failed.");
                trayIdentity = Native.Identity(tray.Id);
                trayStarted = Stopwatch.GetTimestamp();
                bool ready = false;
                for (int attempt = 0; attempt < 15 && !ready; attempt++)
                {
                    if (tray.HasExited) throw new InvalidOperationException("Tray startup failed; B is invalid.");
                    await Task.Delay(250);
                    var check = new ProcessStartInfo(trayPath!) { UseShellExecute = false, CreateNoWindow = true }; check.ArgumentList.Add("--status");
                    using var status = Process.Start(check)!; await status.WaitForExitAsync(); ready = status.ExitCode == 0;
                }
                if (!ready) throw new InvalidOperationException("Tray startup not confirmed; B is invalid.");
            }
            current = "B"; start = Stopwatch.GetTimestamp() + 2 * Stopwatch.Frequency;
        }
        if (current == "B" && seconds >= duration * 2 / 3d)
        {
            ClosePhase(action ?? "Tray overhead; no tuning"); current = "Transition";
            await Restore(); current = "A2"; start = Stopwatch.GetTimestamp() + 2 * Stopwatch.Frequency;
        }
    }
    public void Complete()
    {
        ClosePhase(current == "B" ? action ?? "Tray overhead; no tuning" : "Optimizer off");
        JsonFiles.Write(Path.Combine(output, "phases.json"), Phases);
    }
    private void ClosePhase(string actualAction)
    {
        long end = Stopwatch.GetTimestamp(); if (end > start) Phases.Add(new(current, start, end, actualAction));
    }
    public async Task Restore()
    {
        if (restored) return;
        if (action is not null)
        {
            var restored = await client.Send(new("Restore"));
            Operations.Add(new { AtUtc = DateTimeOffset.UtcNow, Action = "Restore", Reply = restored });
            JsonFiles.Write(Path.Combine(output, "operations.json"), Operations);
            if (!restored.Success || restored.Changes.Any(c => c.Status != ChangeStatus.Reverted)) throw new InvalidOperationException("Restoration requires attention; see recovery journal.");
            var exit = await client.Send(new("Exit")); if (!exit.Success) throw new InvalidOperationException(exit.Message);
        }
        if (tray is { HasExited: false } && trayIdentity is not null && Native.Matches(trayIdentity))
        {
            tray.Refresh();
            double elapsed = Stopwatch.GetElapsedTime(trayStarted).TotalSeconds;
            JsonFiles.Write(Path.Combine(output, "tray-resources.json"), new { Target = trayIdentity, Seconds = elapsed, IncludesStartup = true, CpuSeconds = tray.TotalProcessorTime.TotalSeconds, MachineCpuPercent = tray.TotalProcessorTime.TotalSeconds / elapsed / Environment.ProcessorCount * 100, WorkingSetMiB = tray.WorkingSet64 / 1048576d, PrivateMiB = tray.PrivateMemorySize64 / 1048576d });
            var shutdown = new ProcessStartInfo(trayPath!) { UseShellExecute = false, CreateNoWindow = true }; shutdown.ArgumentList.Add("--shutdown");
            using var control = Process.Start(shutdown); if (control is not null) await control.WaitForExitAsync();
            if (control?.ExitCode != 0) throw new InvalidOperationException("Owned tray application did not confirm shutdown.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await tray.WaitForExitAsync(timeout.Token);
        }
        restored = true;
    }
    public void Dispose() { client.Dispose(); tray?.Dispose(); }
}
