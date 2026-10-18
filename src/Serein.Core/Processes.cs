using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Serein.Core;

public static class ProcessPolicy
{
    private static readonly string[] ProtectedNames = ["serein", "serein.host", "system", "registry", "idle", "explorer", "dwm", "csrss", "wininit", "winlogon", "lsass", "services", "svchost", "smss", "audiodg", "fontdrvhost", "conhost", "sihost", "taskhostw", "runtimebroker", "searchhost", "startmenuexperiencehost", "shellexperiencehost", "steam", "steamwebhelper", "epicgameslauncher", "galaxyclient", "gamingservices", "discord", "obs64", "nvidia", "nvcontainer", "radeon", "atiesrxx", "atieclxx", "anticheat", "easyanticheat", "battleye", "beservice", "vgc", "vgtray", "faceit", "msmpeng", "securityhealth", "defender", "avast", "avg", "kaspersky", "eset", "mcafee", "crowdstrike", "sense", "codex"];
    public static (string Classification, string Reason) Decide(ProcessIdentity identity, string sid, bool critical, bool serviceHost, bool gameDependency, bool explicitlyApproved)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(identity.ImagePath).ToLowerInvariant();
        if (identity.Pid <= 4 || identity.Pid == Environment.ProcessId || critical) return ("CRITICAL", "Windows critical process or optimizer");
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
        if (serviceHost || identity.ImagePath.StartsWith(windows, StringComparison.OrdinalIgnoreCase) || name.Contains("chatgpt", StringComparison.Ordinal) || name.Contains("amdrss", StringComparison.Ordinal) || ProtectedNames.Any(n => name.Contains(n, StringComparison.Ordinal)))
            return ("PROTECTED", "System path, service host, infrastructure or protected application");
        if (gameDependency) return ("GAME_DEPENDENCY", "Game, launcher or linked process");
        if (sid != WindowsIdentity.GetCurrent().User?.Value) return ("UNKNOWN", "Ownership cannot be verified as current user");
        if (!explicitlyApproved) return ("UNKNOWN", "Executable has no explicit approval");
        return ("ELIGIBLE_USER_APP", "Same-user executable with explicit path and hash approval; shared-lock risks remain");
    }
    public static bool Related(int target, HashSet<int> protectedRoots, Dictionary<int, int> parents)
    {
        foreach (var root in protectedRoots)
            if (Ancestor(target, root, parents) || Ancestor(root, target, parents)) return true;
        return false;
    }
    private static bool Ancestor(int child, int ancestor, Dictionary<int, int> parents)
    {
        var seen = new HashSet<int>();
        while (child > 0 && seen.Add(child)) { if (child == ancestor) return true; child = parents.GetValueOrDefault(child); }
        return false;
    }
    public static void ValidateApproval(ProcessIdentity identity, Approval approval)
    {
        if (!identity.ImagePath.Equals(approval.Path, StringComparison.OrdinalIgnoreCase) || !JsonFiles.Approve(identity.ImagePath).Sha256.Equals(approval.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Executable path/hash no longer matches consent.");
    }
}
public sealed class ProcessInventory
{
    private readonly object gate = new();
    private HashSet<int> servicePids = [];
    private DateTimeOffset serviceStamp;
    private string? serviceError;
    private readonly Dictionary<(int, long), (TimeSpan Cpu, long Stamp)> previous = [];
    public IReadOnlyList<ProcessRow> Read(IReadOnlyList<Approval>? approvals = null, HashSet<int>? gameRoots = null)
    { lock (gate) return ReadInternal(approvals, gameRoots); }
    private IReadOnlyList<ProcessRow> ReadInternal(IReadOnlyList<Approval>? approvals, HashSet<int>? gameRoots)
    {
        if (DateTimeOffset.UtcNow - serviceStamp > TimeSpan.FromMinutes(1))
        {
            try { servicePids = Wmi.Query("SELECT ProcessId FROM Win32_Service").Select(r => (int)r.Number("ProcessId")).Where(pid => pid > 0).ToHashSet(); serviceError = null; }
            catch (System.Runtime.InteropServices.COMException ex) { servicePids.Clear(); serviceError = ex.Message; }
            serviceStamp = DateTimeOffset.UtcNow;
        }
        var rows = new List<ProcessRow>(); var parents = Native.Parents(); long stamp = Stopwatch.GetTimestamp();
        var seen = new HashSet<(int, long)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var identity = Native.Identity(process.Id);
                    using var h = Native.OpenProcess(Native.Query, false, process.Id);
                    string sid = Native.Owner(h);
                    bool criticalKnown = Native.IsProcessCritical(h, out bool critical);
                    bool protectionKnown = Native.GetProcessInformation(h, 7, out uint protection, 4);
                    var key = (identity.Pid, identity.CreatedFileTime); var cpuTime = process.TotalProcessorTime;
                    double? cpu = previous.TryGetValue(key, out var last) ? Math.Clamp((cpuTime - last.Cpu).TotalSeconds / Stopwatch.GetElapsedTime(last.Stamp, stamp).TotalSeconds / Environment.ProcessorCount * 100, 0, 100) : null;
                    previous[key] = (cpuTime, stamp); seen.Add(key);
                    bool approved = approvals?.Any(a => a.Path.Equals(identity.ImagePath, StringComparison.OrdinalIgnoreCase)) == true;
                    var verdict = ProcessPolicy.Decide(identity, sid, critical, servicePids.Contains(process.Id) || protectionKnown && protection != 0xFFFFFFFE, ProcessPolicy.Related(process.Id, gameRoots ?? [], parents), approved);
                    if (verdict.Classification is "UNKNOWN" or "ELIGIBLE_USER_APP" && (!criticalKnown || !protectionKnown || serviceError is not null)) verdict = ("UNKNOWN", "Critical/PPL/service-host verification unavailable; operations denied");
                    bool candidate = criticalKnown && protectionKnown && serviceError is null && ProcessPolicy.Decide(identity, sid, critical, servicePids.Contains(process.Id) || protection != 0xFFFFFFFE, ProcessPolicy.Related(process.Id, gameRoots ?? [], parents), true).Classification == "ELIGIBLE_USER_APP";
                    rows.Add(new(identity, process.ProcessName, parents.GetValueOrDefault(process.Id), sid, verdict.Classification, verdict.Reason, cpu, process.WorkingSet64, process.PrivateMemorySize64, process.PriorityClass.ToString()) { CanApprove = candidate });
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
                {
                    int pid; try { pid = process.Id; } catch (InvalidOperationException) { continue; }
                    rows.Add(new(new(pid, 0, ""), "Access unavailable", parents.GetValueOrDefault(pid), "Unknown", "UNKNOWN", ex.Message, null, 0, 0, "Unknown"));
                }
            }
        }
        foreach (var key in previous.Keys.Where(k => !seen.Contains(k)).ToList()) previous.Remove(key);
        return rows.OrderByDescending(p => p.WorkingSet).ToList();
    }
}
