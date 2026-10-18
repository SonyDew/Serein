using Serein.Core;

namespace Serein_Gaming_WinUI;

internal static class ExperimentalProcessProtection
{
    // Additional experiment-side exclusions. These complement the engine's
    // independent PID/lifetime, service, game and explicit-hash validation.
    public static IReadOnlyList<ProcessRow> Apply(IReadOnlyList<ProcessRow> rows, string? gamePath)
    {
        var parents = rows.ToDictionary(p => p.Pid, p => p.ParentPid);
        var roots = rows.Where(p => Infrastructure(p) || p.Path.Equals(gamePath, StringComparison.OrdinalIgnoreCase)).Select(p => p.Pid).ToHashSet();
        return rows.Select(p => Infrastructure(p) || ProcessPolicy.Related(p.Pid, roots, parents)
            ? p with { CanApprove = false, Classification = "PROTECTED", Reason = "Optimizer tooling, hardware component, game or linked process" } : p).ToList();
    }
    private static bool Infrastructure(ProcessRow row)
    {
        string path = row.Path.Replace('/', '\\');
        if (new[] { "\\OpenAI\\", "\\.codex\\", "\\AMD\\", "\\NVIDIA Corporation\\", "\\Intel\\", "\\ASUS\\", "\\MSI\\", "\\Microsoft GameInput\\" }.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase))) return true;
        return new[] { "codex", "chatgpt", "discord", "steam", "msedgewebview2", "node", "VBCSCompiler", "MSBuild", "dotnet", "GameInput", "Roblox", "cs2", "dota2", "StudioMCP", "XboxPcApp", "Armoury", "RyzenMaster", "cpumetrics", "AMDRSS", "cncmd" }.Any(n => row.Name.Contains(n, StringComparison.OrdinalIgnoreCase));
    }
}
