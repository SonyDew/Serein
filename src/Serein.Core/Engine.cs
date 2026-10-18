using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Serein.Core;

public record ThreadIdentity(int Tid, long CreatedFileTime);
public enum ChangeStatus { Prepared, Applied, Reverting, Reverted, Attention }
public sealed record Change
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required ProcessIdentity Target { get; init; }
    public required string Kind { get; init; }
    public ThreadIdentity? Thread { get; init; }
    public uint Before { get; set; }
    public uint Requested { get; init; }
    public ChangeStatus Status { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset DeadlineUtc { get; init; }
}
public sealed record Journal
{
    public int SchemaVersion { get; init; } = 1;
    public required ProcessIdentity Host { get; init; }
    public required ProcessIdentity Owner { get; init; }
    public required string Pipe { get; init; }
    public List<Change> Changes { get; init; } = [];
    public void Validate()
    {
        if (SchemaVersion != 1 || Changes.Count > 1024 || Pipe.Length > 80) throw new InvalidDataException("Invalid journal schema or size.");
        foreach (var c in Changes)
            if (c.Target.Pid <= 4 || c.Target.CreatedFileTime <= 0 || c.Kind is not ("Freeze" or "Priority") || !Enum.IsDefined(c.Status) || (c.Kind == "Freeze" && (c.Thread is null || c.Thread.CreatedFileTime <= 0)) || (c.Kind == "Priority" && !new uint[] { 0x20, 0x40, 0x80, 0x100, 0x4000, 0x8000 }.Contains(c.Before)))
                throw new InvalidDataException("Invalid operation in recovery journal.");
    }
}
public interface IOperationPlatform : IDisposable
{
    void ValidateTarget(ProcessIdentity target, Approval approval, HashSet<int> protectedRoots);
    bool Matches(ProcessIdentity target);
    IReadOnlyList<ThreadIdentity> Threads(ProcessIdentity target);
    uint Suspend(ProcessIdentity target, ThreadIdentity thread);
    uint Resume(ProcessIdentity target, ThreadIdentity thread);
    uint Priority(ProcessIdentity target);
    void SetPriority(ProcessIdentity target, uint priority);
}
public sealed class WindowsOperations : IOperationPlatform
{
    private readonly Dictionary<(int, int, long), SafeWaitHandle> handles = [];
    public void ValidateTarget(ProcessIdentity target, Approval approval, HashSet<int> protectedRoots)
    {
        if (!Matches(target)) throw new InvalidOperationException("Target exited or PID identity changed.");
        ProcessPolicy.ValidateApproval(target, approval);
        using var h = OpenVerified(target, Native.Query);
        if (!Native.IsProcessCritical(h, out bool critical)) throw Native.Error();
        if (!Native.GetProcessInformation(h, 7, out uint protection, 4)) throw new InvalidOperationException("Process protection status unavailable; operation denied.");
        if (protection != 0xFFFFFFFE) throw new InvalidOperationException("Protected process (PPL); operation denied.");
        if (!Native.ProcessIdToSessionId((uint)target.Pid, out uint targetSession) || !Native.ProcessIdToSessionId((uint)Environment.ProcessId, out uint ownSession) || targetSession != ownSession) throw new InvalidOperationException("Cross-session target denied.");
        var services = Wmi.Query("SELECT ProcessId FROM Win32_Service");
        var isService = services.Any(s => s.Number("ProcessId") == target.Pid);
        var verdict = ProcessPolicy.Decide(target, Native.Owner(h), critical, isService, ProcessPolicy.Related(target.Pid, protectedRoots, Native.Parents()), true);
        if (verdict.Classification != "ELIGIBLE_USER_APP") throw new InvalidOperationException($"{verdict.Classification}: {verdict.Reason}");
    }
    internal static SafeProcessHandle OpenVerified(ProcessIdentity target, uint access)
    {
        var h = Native.OpenProcess(access | Native.Query, false, target.Pid);
        if (h.IsInvalid) { h.Dispose(); throw Native.Error(); }
        if (!Native.GetProcessTimes(h, out var born, out _, out _, out _) || born != target.CreatedFileTime)
        { h.Dispose(); throw new InvalidOperationException("Process identity mismatch."); }
        return h;
    }
    public bool Matches(ProcessIdentity target) => Native.Matches(target);
    public IReadOnlyList<ThreadIdentity> Threads(ProcessIdentity target)
    {
        using var verified = OpenVerified(target, Native.Query);
        using var process = Process.GetProcessById(target.Pid);
        var list = new List<ThreadIdentity>();
        foreach (ProcessThread thread in process.Threads)
        {
            using (thread)
            {
                using var h = Native.OpenThread(0x40, false, thread.Id);
                if (h.IsInvalid || Native.GetProcessIdOfThread(h) != target.Pid || !Native.GetThreadTimes(h, out var born, out _, out _, out _)) throw Native.Error();
                list.Add(new(thread.Id, born));
            }
        }
        if (list.Count is < 1 or > 512) throw new InvalidOperationException("Unsupported thread count.");
        return list;
    }
    private SafeWaitHandle ThreadHandle(ProcessIdentity target, ThreadIdentity thread)
    {
        var key = (target.Pid, thread.Tid, thread.CreatedFileTime);
        if (handles.TryGetValue(key, out var existing)) return existing;
        var h = Native.OpenThread(0x42, false, thread.Tid);
        if (h.IsInvalid) { h.Dispose(); throw Native.Error(); }
        if (Native.GetProcessIdOfThread(h) != target.Pid || !Native.GetThreadTimes(h, out var born, out _, out _, out _) || born != thread.CreatedFileTime)
        { h.Dispose(); throw new InvalidOperationException("Thread identity mismatch."); }
        handles[key] = h; return h;
    }
    public uint Suspend(ProcessIdentity target, ThreadIdentity thread)
    {
        using var process = OpenVerified(target, Native.Query);
        uint result = Native.SuspendThread(ThreadHandle(target, thread));
        if (result == uint.MaxValue) throw Native.Error();
        return result;
    }
    public uint Resume(ProcessIdentity target, ThreadIdentity thread)
    {
        using var process = OpenVerified(target, Native.Query);
        SafeWaitHandle h;
        try { h = ThreadHandle(target, thread); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 87) { return 0; }
        if (!Native.GetExitCodeThread(h, out uint exitCode)) throw Native.Error();
        if (exitCode != 259) return 0;
        uint result = Native.ResumeThread(h);
        if (result == uint.MaxValue) throw Native.Error();
        return result;
    }
    public uint Priority(ProcessIdentity target)
    {
        using var h = OpenVerified(target, Native.Query); uint result = Native.GetPriorityClass(h);
        if (result == 0) throw Native.Error(); return result;
    }
    public void SetPriority(ProcessIdentity target, uint priority)
    {
        if (priority is not (0x20 or 0x40 or 0x80 or 0x100 or 0x4000 or 0x8000)) throw new InvalidOperationException("Priority not permitted.");
        using var h = OpenVerified(target, Native.SetInformation);
        if (!Native.SetPriorityClass(h, priority)) throw Native.Error();
    }
    public void Dispose() { foreach (var h in handles.Values) h.Dispose(); handles.Clear(); }
}
public sealed class OperationEngine(IOperationPlatform platform, Journal journal, string journalPath)
{
    public Journal Journal => journal;
    private readonly Dictionary<Guid, long> leaseStarts = [];
    private void Save() => JsonFiles.Write(journalPath, journal);
    public void Apply(string kind, ProcessIdentity target, Approval approval, int seconds, HashSet<int> gameRoots)
    {
        if (kind is not ("Freeze" or "Priority") || seconds is < 5 or > 1800) throw new InvalidDataException("Invalid operation or lease.");
        if (journal.Changes.Any(c => c.Target == target && c.Status != ChangeStatus.Reverted)) throw new InvalidOperationException("Target already has an active or unresolved operation.");
        gameRoots = [.. gameRoots, journal.Owner.Pid, journal.Host.Pid];
        platform.ValidateTarget(target, approval, gameRoots);
        if (journal.Changes.Count > 768) journal.Changes.RemoveAll(c => c.Status == ChangeStatus.Reverted);
        if (journal.Changes.Count >= 1024) throw new InvalidOperationException("Operation journal capacity reached. Restore first.");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
        try
        {
            if (kind == "Priority")
            {
                uint before = platform.Priority(target);
                if (before == 0x4000) return;
                var c = new Change { Target = target, Kind = kind, Before = before, Requested = 0x4000, DeadlineUtc = deadline };
                journal.Changes.Add(c); Save();
                try { platform.SetPriority(target, c.Requested); }
                catch (Win32Exception ex) { c.Status = ChangeStatus.Reverted; c.Error = ex.Message; Save(); throw; }
                c.Status = ChangeStatus.Applied; leaseStarts[c.Id] = Stopwatch.GetTimestamp(); Save();
                if (platform.Priority(target) != c.Requested) throw new InvalidOperationException("Priority verification failed.");
            }
            else
            {
                var threads = platform.Threads(target);
                if (journal.Changes.Count + threads.Count > 1024) throw new InvalidOperationException("Thread journal capacity exceeded.");
                foreach (var thread in threads)
                {
                    var c = new Change { Target = target, Kind = kind, Thread = thread, DeadlineUtc = deadline };
                    journal.Changes.Add(c); Save();
                    try { c.Before = platform.Suspend(target, thread); }
                    catch (Win32Exception ex) { c.Status = ChangeStatus.Reverted; c.Error = ex.Message; Save(); throw; }
                    c.Status = ChangeStatus.Applied; leaseStarts[c.Id] = Stopwatch.GetTimestamp(); Save();
                }
                var after = platform.Threads(target);
                if (!threads.ToHashSet().SetEquals(after)) throw new InvalidOperationException("Thread set changed during freeze; rolling back.");
            }
        }
        catch
        {
            Restore(target);
            throw;
        }
    }
    public void RestoreExpired()
    {
        foreach (var target in journal.Changes.Where(c => c.Status == ChangeStatus.Applied && (DateTimeOffset.UtcNow >= c.DeadlineUtc || (leaseStarts.TryGetValue(c.Id, out var start) && Stopwatch.GetElapsedTime(start).TotalSeconds >= (c.DeadlineUtc - c.CreatedUtc).TotalSeconds))).Select(c => c.Target).Distinct().ToList()) Restore(target);
    }
    public void Restore(ProcessIdentity? target = null)
    {
        foreach (var c in journal.Changes.AsEnumerable().Reverse().Where(c => target is null || c.Target == target))
        {
            if (c.Status == ChangeStatus.Reverted || c.Status == ChangeStatus.Attention) continue;
            if (!platform.Matches(c.Target)) { c.Status = ChangeStatus.Reverted; c.Error = "Target exited or identity changed; no native mutation."; Save(); continue; }
            if (c.Status is ChangeStatus.Prepared or ChangeStatus.Reverting)
            { c.Status = ChangeStatus.Attention; c.Error = "Ambiguous native/journal boundary; automatic resume refused to preserve external suspend counts."; Save(); continue; }
            try
            {
                if (c.Kind == "Priority" && platform.Priority(c.Target) != c.Requested)
                { c.Status = ChangeStatus.Reverted; c.Error = "External priority change preserved."; Save(); continue; }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { c.Error = ex.Message; Save(); continue; }
            c.Status = ChangeStatus.Reverting; Save();
            try
            {
                if (c.Kind == "Freeze")
                {
                    uint previous = platform.Resume(c.Target, c.Thread!);
                    if (previous == 0) c.Error = "Owned increment was already absent; external intervention observed.";
                }
                else platform.SetPriority(c.Target, c.Before);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                // API reports failure: no decrement succeeded. Retain retryable ownership.
                c.Status = ChangeStatus.Applied; c.Error = ex.Message; Save(); continue;
            }
            c.Status = ChangeStatus.Reverted; Save();
        }
    }
}
public record OperationRequest(string Action, ProcessIdentity? Target = null, Approval? Approval = null, int Seconds = 600, int[]? GameRoots = null, Guid? PowerScheme = null);
public record OperationReply(bool Success, string Message, List<Change> Changes, PowerLease? Power = null);
public static class Recovery
{
    public static List<string> Reconcile(string directory)
    {
        var results = new List<string>();
        if (!Directory.Exists(directory)) return results;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var journal = JsonFiles.Read<Journal>(path); journal.Validate();
                if (Native.Matches(journal.Host)) continue; // live watchdog is the sole writer
                using var platform = new WindowsOperations();
                var engine = new OperationEngine(platform, journal, path); engine.Restore();
                var pending = journal.Changes.Count(c => c.Status != ChangeStatus.Reverted);
                results.Add($"{System.IO.Path.GetFileName(path)}: {pending} unresolved operations");
            }
            catch (Exception ex) when (ex is IOException or JsonException or Win32Exception or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
            { results.Add($"{System.IO.Path.GetFileName(path)}: {ex.Message}"); }
        }
        results.AddRange(PowerLeaseController.Recover(directory));
        var retention = RecoveryRetention.Prune(directory);
        results.AddRange(retention.Messages);
        return results;
    }
}
