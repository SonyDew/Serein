using Microsoft.Win32;

namespace Serein.Core;

public record GameEntry(string Name, string Path, string Source, string? LaunchUri = null);
public static class GameDiscovery
{
    public static List<GameEntry> Read()
    {
        var games = new List<GameEntry>();
        using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        string? home = steam?.GetValue("SteamPath") as string;
        if (home is not null)
        {
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { home };
            var config = System.IO.Path.Combine(home, "steamapps", "libraryfolders.vdf");
            if (File.Exists(config))
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(config), "\"path\"\\s*\"([^\"]+)\"")) libraries.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            foreach (var library in libraries)
            {
                string apps = System.IO.Path.Combine(library, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    string content = File.ReadAllText(manifest);
                    string Value(string key) => System.Text.RegularExpressions.Regex.Match(content, "\"" + key + "\"\\s*\"([^\"]+)\"").Groups[1].Value;
                    string id = Value("appid"), name = Value("name"), install = Value("installdir");
                    if (id.All(char.IsAsciiDigit) && id.Length > 0 && install.Length > 0 && !install.Contains("..") && !System.IO.Path.IsPathFullyQualified(install))
                        games.Add(new(name, System.IO.Path.Combine(apps, "common", install), "Steam manifest (choose actual game EXE for monitoring)", "steam://rungameid/" + id));
                }
            }
        }
        // Launcher file layouts are not a guaranteed vendor API; discovery never executes a manifest-provided command.
        return games;
    }
}
public sealed class GameSessionTracker
{
    private HashSet<ProcessIdentity> active = [];
    public bool Active => active.Count > 0;
    public HashSet<int> Roots => active.Select(p => p.Pid).ToHashSet();
    public (bool Started, bool Ended) Update(string? gamePath, IReadOnlyList<ProcessRow> rows)
    {
        var parents = rows.ToDictionary(r => r.Pid, r => r.ParentPid);
        var next = rows.Where(r => gamePath is not null && r.Path.Equals(gamePath, StringComparison.OrdinalIgnoreCase)).Select(r => r.Identity).ToHashSet();
        // Retain descendants only while their exact PID/lifetime remains observed; never capture a reused PID.
        foreach (var row in rows)
            if (active.Contains(row.Identity) || ProcessPolicy.Related(row.Pid, active.Select(p => p.Pid).Where(p => rows.Any(r => r.Pid == p && active.Contains(r.Identity))).ToHashSet(), parents))
            { if (active.Contains(row.Identity) || active.Any(p => p.Pid == row.ParentPid)) next.Add(row.Identity); }
        bool started = active.Count == 0 && next.Count > 0, ended = active.Count > 0 && next.Count == 0;
        active = next; return (started, ended);
    }
    public void Reset() => active.Clear();
}
