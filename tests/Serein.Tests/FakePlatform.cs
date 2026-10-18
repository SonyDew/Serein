using System.ComponentModel;
using Serein.Core;

namespace Serein.Tests;
public sealed class FakePlatform : IOperationPlatform
{
    public bool SameIdentity = true, Denied;
    public int FailSuspend = -1, FailResume = -1;
    public Dictionary<int, uint> Counts = new() { [1] = 0, [2] = 0 };
    public uint CurrentPriority = 0x20;
    public int ResumeCalls;
    public void ValidateTarget(ProcessIdentity target, Approval approval, HashSet<int> protectedRoots) { if (Denied) throw new InvalidOperationException("Denied"); }
    public bool Matches(ProcessIdentity target) => SameIdentity;
    public IReadOnlyList<ThreadIdentity> Threads(ProcessIdentity target) => Counts.Keys.Select(id => new ThreadIdentity(id, id * 100)).ToList();
    public uint Suspend(ProcessIdentity target, ThreadIdentity thread) { if (thread.Tid == FailSuspend) throw new Win32Exception(5); uint before = Counts[thread.Tid]; Counts[thread.Tid]++; return before; }
    public uint Resume(ProcessIdentity target, ThreadIdentity thread) { if (thread.Tid == FailResume) throw new Win32Exception(5); ResumeCalls++; uint before = Counts[thread.Tid]; if (before > 0) Counts[thread.Tid]--; return before; }
    public uint Priority(ProcessIdentity target) => CurrentPriority;
    public void SetPriority(ProcessIdentity target, uint priority) => CurrentPriority = priority;
    public void Dispose() { }
}
