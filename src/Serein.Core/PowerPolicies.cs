using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Serein.Core;
public record PowerScheme(Guid Id, string Name) { public override string ToString() => Name + " · " + Id; }
public interface IPowerPlatform
{
    Guid Current();
    IReadOnlyList<PowerScheme> Schemes();
    void Select(Guid id);
    IDisposable Acquire();
}
public sealed class WindowsPowerPlatform : IPowerPlatform
{
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(nint root, out nint scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(nint root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(nint root, nint scheme, nint subgroup, uint access, uint index, byte[] buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(nint root, ref Guid scheme, nint subgroup, nint setting, byte[] buffer, ref uint size);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint value);
    public Guid Current()
    {
        uint error = PowerGetActiveScheme(0, out nint value); if (error != 0) throw new Win32Exception((int)error);
        try { return Marshal.PtrToStructure<Guid>(value); } finally { LocalFree(value); }
    }
    public IReadOnlyList<PowerScheme> Schemes()
    {
        var result = new List<PowerScheme>();
        for (uint index = 0; index < 128; index++)
        {
            byte[] data = new byte[16]; uint size = 16; uint error = PowerEnumerate(0, 0, 0, 16, index, data, ref size);
            if (error == 259) return result; if (error != 0) throw new Win32Exception((int)error);
            var id = new Guid(data); byte[] name = new byte[4096]; size = (uint)name.Length;
            error = PowerReadFriendlyName(0, ref id, 0, 0, name, ref size);
            result.Add(new(id, error == 0 && size <= name.Length ? Encoding.Unicode.GetString(name, 0, (int)size).TrimEnd('\0') : id.ToString()));
        }
        throw new InvalidDataException("More than 128 installed power schemes.");
    }
    public void Select(Guid id)
    {
        if (id == Guid.Empty || !Schemes().Any(p => p.Id == id)) throw new InvalidDataException("Power scheme is no longer installed.");
        uint error = PowerSetActiveScheme(0, ref id); if (error != 0) throw new Win32Exception((int)error, "Power policy denied or changed by Windows/OEM/Group Policy.");
    }
    public IDisposable Acquire() => new PowerPolicyLock();
}
// Dedicated thread preserves Win32 mutex thread ownership across the async watchdog loop.
internal sealed class PowerPolicyLock : IDisposable
{
    private readonly ManualResetEvent ready = new(false), release = new(false);
    private readonly Thread thread;
    private Exception? failure;
    public PowerPolicyLock()
    {
        thread = new Thread(() =>
        {
            bool held = false;
            Mutex? mutex = null;
            try
            {
                mutex = new Mutex(false, "Global\\Serein-PowerPolicy-v1");
                try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                if (!held) throw new InvalidOperationException("Another Serein session owns the system power policy. Restore it first.");
            }
            catch (Exception ex) { failure = ex; }
            finally { ready.Set(); }
            if (held) { release.WaitOne(); mutex!.ReleaseMutex(); }
            mutex?.Dispose();
        }) { IsBackground = true, Name = "Serein power ownership" };
        thread.Start(); ready.WaitOne(); if (failure is not null) { Dispose(); throw new InvalidOperationException(failure.Message, failure); }
    }
    public void Dispose() { release.Set(); thread.Join(); ready.Dispose(); release.Dispose(); }
}
public sealed record PowerLease
{
    public int SchemaVersion { get; init; } = 1;
    public required ProcessIdentity Host { get; init; }
    public required ProcessIdentity Owner { get; init; }
    public Guid Before { get; init; }
    public Guid Requested { get; init; }
    public ChangeStatus Status { get; set; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset DeadlineUtc { get; init; }
    public string? Error { get; set; }
    public void Validate()
    {
        if (SchemaVersion != 1 || Host.Pid <= 4 || Host.CreatedFileTime <= 0 || Owner.Pid <= 4 || Before == Guid.Empty || Requested == Guid.Empty || !Enum.IsDefined(Status) || (DeadlineUtc - CreatedUtc).TotalSeconds is < 4 or > 1801) throw new InvalidDataException("Invalid power lease.");
    }
}
public sealed class PowerLeaseController(IPowerPlatform platform, string path)
{
    public PowerLease? Lease { get; private set; }
    private IDisposable? ownership;
    private long started;
    private void Save() => JsonFiles.Write(path, Lease!);
    public string Apply(Guid scheme, int seconds, ProcessIdentity host, ProcessIdentity owner)
    {
        if (seconds is < 5 or > 1800 || scheme == Guid.Empty) throw new InvalidDataException("Invalid power lease/scheme.");
        if (Lease is { Status: not ChangeStatus.Reverted }) throw new InvalidOperationException("Restore the current power lease first.");
        Lease = null;
        ownership = platform.Acquire();
        try
        {
            Guid before = platform.Current(); if (before == scheme) { ownership.Dispose(); ownership = null; return "Power scheme already active; no change."; }
            if (!platform.Schemes().Any(s => s.Id == scheme)) throw new InvalidDataException("Scheme not installed.");
            Lease = new() { Host = host, Owner = owner, Before = before, Requested = scheme, DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(seconds) }; Lease.Validate(); Save();
            platform.Select(scheme); Lease.Status = ChangeStatus.Applied; started = Stopwatch.GetTimestamp(); Save();
            if (platform.Current() != scheme) throw new InvalidOperationException("Power change was overridden; verify Windows/OEM policy.");
            return "Temporary power scheme verified. No scheme settings were edited.";
        }
        catch { if (Lease is not null) { Lease.Status = ChangeStatus.Applied; Restore(); } else { ownership?.Dispose(); ownership = null; } throw; }
    }
    public void RestoreExpired()
    {
        if (Lease is { Status: ChangeStatus.Applied } lease && (DateTimeOffset.UtcNow >= lease.DeadlineUtc || Stopwatch.GetElapsedTime(started).TotalSeconds >= (lease.DeadlineUtc - lease.CreatedUtc).TotalSeconds)) Restore();
    }
    public void Restore()
    {
        if (Lease is null || Lease.Status == ChangeStatus.Reverted) return;
        try
        {
            Guid current = platform.Current();
            if (current == Lease.Before) { Lease.Status = ChangeStatus.Reverted; Save(); return; }
            if (current != Lease.Requested) { Lease.Status = ChangeStatus.Reverted; Lease.Error = "External power selection preserved."; Save(); return; }
            if (Lease.Status is ChangeStatus.Prepared or ChangeStatus.Reverting or ChangeStatus.Attention) { Lease.Status = ChangeStatus.Attention; Lease.Error = "Ambiguous power journal boundary; choose a scheme explicitly in Windows settings."; Save(); return; }
            Lease.Status = ChangeStatus.Reverting; Save(); platform.Select(Lease.Before);
            if (platform.Current() != Lease.Before) throw new InvalidOperationException("Original power scheme restoration could not be verified.");
            Lease.Status = ChangeStatus.Reverted; Save();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or InvalidDataException) { Lease.Status = ChangeStatus.Applied; Lease.Error = ex.Message; Save(); }
        finally { if (Lease.Status == ChangeStatus.Reverted) { ownership?.Dispose(); ownership = null; } }
    }
    public static List<PowerLease> Pending(string directory) => !Directory.Exists(directory) ? [] : Directory.EnumerateFiles(directory, "*.power").Select(p => { var lease = JsonFiles.Read<PowerLease>(p); lease.Validate(); return lease; }).Where(p => p.Status != ChangeStatus.Reverted).ToList();
    public static List<string> Recover(string directory, IPowerPlatform? recoveryPlatform = null, Func<ProcessIdentity, bool>? isLive = null)
    {
        var result = new List<string>(); if (!Directory.Exists(directory)) return result;
        foreach (string file in Directory.EnumerateFiles(directory, "*.power"))
        {
            try
            {
                var lease = JsonFiles.Read<PowerLease>(file); lease.Validate(); if (lease.Status == ChangeStatus.Reverted || (isLive ?? Native.Matches)(lease.Host)) continue;
                var platform = recoveryPlatform ?? new WindowsPowerPlatform(); using var gate = platform.Acquire(); var controller = new PowerLeaseController(platform, file) { Lease = lease }; controller.Restore();
                result.Add($"{Path.GetFileName(file)}: {(lease.Status == ChangeStatus.Reverted ? 0 : 1)} unresolved power operations");
            }
            catch (Exception ex) { result.Add(Path.GetFileName(file) + ": " + ex.Message); }
        }
        return result;
    }
}
