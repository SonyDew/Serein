using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Serein.Core;

public static class Native
{
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    public static bool? IsForeground(int pid)
    {
        nint window = GetForegroundWindow();
        if (window == 0 || GetWindowThreadProcessId(window, out uint owner) == 0) return null;
        return owner == pid;
    }
    public const uint Query = 0x1000, SetInformation = 0x0200;
    [DllImport("kernel32.dll", SetLastError = true)] public static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetProcessTimes(SafeProcessHandle p, out long created, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool QueryFullProcessImageName(SafeProcessHandle p, uint flags, System.Text.StringBuilder path, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool IsProcessCritical(SafeProcessHandle p, out bool critical);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetProcessInformation(SafeProcessHandle p, int informationClass, out uint protection, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ProcessIdToSessionId(uint pid, out uint session);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle p, uint access, out nint token);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(nint h);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern SafeWaitHandle OpenThread(uint access, bool inherit, int tid);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern uint SuspendThread(SafeWaitHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern uint ResumeThread(SafeWaitHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetThreadTimes(SafeWaitHandle thread, out long created, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern uint GetProcessIdOfThread(SafeWaitHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetExitCodeThread(SafeWaitHandle thread, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern uint GetPriorityClass(SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetPriorityClass(SafeProcessHandle process, uint priority);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [StructLayout(LayoutKind.Sequential)] public struct MemoryStatus
    {
        public uint Length, Load;
        public ulong Total, Available, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern nint CreateToolhelp32Snapshot(uint flags, uint pid);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct ProcessEntry
    {
        public uint Size, Usage, Pid; public nint DefaultHeap; public uint ModuleId, Threads, ParentPid; public int BasePriority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool Process32First(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);
    public static Win32Exception Error() => new(Marshal.GetLastWin32Error());
    public static ProcessIdentity Identity(int pid)
    {
        using var h = OpenProcess(Query, false, pid);
        if (h.IsInvalid || !GetProcessTimes(h, out var born, out _, out _, out _)) throw Error();
        var path = new System.Text.StringBuilder(32768); int length = path.Capacity;
        if (!QueryFullProcessImageName(h, 0, path, ref length)) throw Error();
        return new(pid, born, path.ToString());
    }
    public static bool Matches(ProcessIdentity expected)
    {
        try { var actual = Identity(expected.Pid); return actual.CreatedFileTime == expected.CreatedFileTime && actual.ImagePath.Equals(expected.ImagePath, StringComparison.OrdinalIgnoreCase); }
        catch (Win32Exception) { return false; }
    }
    public static string Owner(SafeProcessHandle h)
    {
        if (!OpenProcessToken(h, 8, out var token)) throw Error();
        try { using var identity = new WindowsIdentity(token); return identity.User?.Value ?? throw new InvalidOperationException("Unknown SID."); }
        finally { CloseHandle(token); }
    }
    public static Dictionary<int, int> Parents()
    {
        var result = new Dictionary<int, int>(); var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == -1) throw Error();
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), Exe = "" };
            if (Process32First(snapshot, ref entry)) do { result[(int)entry.Pid] = (int)entry.ParentPid; } while (Process32Next(snapshot, ref entry));
            return result;
        }
        finally { CloseHandle(snapshot); }
    }
}
