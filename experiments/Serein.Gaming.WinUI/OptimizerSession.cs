using System.IO;
using Serein.Core;

namespace Serein_Gaming_WinUI;

// The experiment has its own profile and recovery directory. It never reads or
// writes the production desktop's preferences, approvals or operation journals.
internal sealed class OptimizerSession : IDisposable
{
    private readonly OperationClient operations;
    private readonly ProfileStore store;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly GameSessionTracker tracker = new();
    private bool suppressed;
    public Profile Profile { get; private set; }
    public bool Armed { get; private set; }
    public bool Playing => tracker.Active;
    public IReadOnlyList<Change> Changes { get; private set; } = [];
    public int ActiveChanges => Changes.Count(c => c.Status != ChangeStatus.Reverted);
    public string Status => Playing ? UiStrings.Get("Text257") : Armed ? UiStrings.Get("Text004") : UiStrings.Get("Text258");
    public OptimizerSession(string directory)
    {
        store = new ProfileStore(directory);
        Profile = store.Load().First(p => p.Name == "Gaming");
        operations = new OperationClient(Path.Combine(AppContext.BaseDirectory, "Serein.Host.exe"), Path.Combine(directory, "recovery"));
    }
    public void Save(Profile profile)
    {
        if (Armed) throw new InvalidOperationException(UiStrings.Get("Text253"));
        profile.Validate();
        var all = store.Load();
        all[all.FindIndex(p => p.Name == "Gaming")] = profile with { Name = "Gaming" };
        store.Save(all); Profile = all.First(p => p.Name == "Gaming");
    }
    public async Task Arm()
    {
        if (Profile.GamePath is null || !File.Exists(Profile.GamePath)) throw new InvalidOperationException(UiStrings.Get("Text245"));
        if (Profile.Mode is not (ProfileMode.Gaming or ProfileMode.ExtremeGaming)) throw new InvalidOperationException(UiStrings.Get("Text259"));
        await gate.WaitAsync();
        try { await RestoreCore(); tracker.Reset(); suppressed = false; Armed = true; }
        finally { gate.Release(); }
    }
    public async Task Poll(IReadOnlyList<ProcessRow> rows)
    {
        rows = ExperimentalProcessProtection.Apply(rows, Profile.GamePath);
        await gate.WaitAsync();
        try
        {
            if (!Armed)
            {
                if (operations.HostPid is not null) await Send(new("Status"));
                return;
            }
            var update = tracker.Update(Profile.GamePath, rows);
            if (update.Ended) { await RestoreCore(); suppressed = false; }
            if (update.Started && !suppressed)
            {
                try
                {
                    foreach (var approval in Profile.AllowedApps)
                    foreach (var row in rows.Where(r => r.Path.Equals(approval.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!row.CanApprove) throw new InvalidOperationException(UiStrings.Get("Text260") + row.Name);
                        await Send(new(Profile.Mode == ProfileMode.ExtremeGaming ? "Freeze" : "Priority", row.Identity, approval, Profile.DeadlineSeconds, tracker.Roots.ToArray()));
                    }
                }
                catch { Armed = false; await RestoreCore(); throw; }
            }
            if (operations.HostPid is not null) await Send(new("Status"));
        }
        finally { gate.Release(); }
    }
    public async Task Restore(bool disarm = true)
    {
        await gate.WaitAsync();
        try
        {
            suppressed = true;
            if (disarm) { Armed = false; tracker.Reset(); }
            await RestoreCore();
        }
        finally { gate.Release(); }
    }
    public async Task Manual(string action, ProcessRow row)
    {
        if (action is not ("Freeze" or "Priority")) throw new InvalidDataException(UiStrings.Get("Text261"));
        if (!row.CanApprove) throw new InvalidOperationException(UiStrings.Get("Text262"));
        await gate.WaitAsync();
        try
        {
            if (Armed) throw new InvalidOperationException(UiStrings.Get("Text253"));
            var current = await Task.Run(() => new ProcessInventory().Read());
            current = ExperimentalProcessProtection.Apply(current, Profile.GamePath);
            if (current.FirstOrDefault(p => p.Identity == row.Identity) is not { CanApprove: true })
                throw new InvalidOperationException(UiStrings.Get("Text263"));
            var roots = current.Where(p => p.Path.Equals(Profile.GamePath, StringComparison.OrdinalIgnoreCase)).Select(p => p.Pid).ToHashSet();
            roots.UnionWith(tracker.Roots);
            if (ProcessPolicy.Related(row.Pid, roots, current.ToDictionary(p => p.Pid, p => p.ParentPid)))
                throw new InvalidOperationException(UiStrings.Get("Text264"));
            var approval = await Task.Run(() => JsonFiles.Approve(row.Path));
            await Send(new(action, row.Identity, approval, 120, roots.ToArray()));
        }
        finally { gate.Release(); }
    }
    private async Task RestoreCore() => await Send(new("Restore"));
    private async Task Send(OperationRequest request)
    {
        var reply = await operations.Send(request);
        Changes = reply.Changes;
        if (!reply.Success) throw new InvalidOperationException(reply.Message);
    }
    public async Task Shutdown()
    {
        await Restore();
        if (operations.HostPid is not null) await Send(new("Exit"));
    }
    public void Dispose() { operations.Dispose(); gate.Dispose(); }
}
