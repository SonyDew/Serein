using Microsoft.Win32;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Serein.Core;

public sealed record InstalledGame(string Id, string Name, string Source, string Directory, IReadOnlyList<string> DeclaredExecutables)
{
    public string Caption => Source;
    public override string ToString() => Name + " · " + Source;
}
public sealed record GameLibrary(IReadOnlyList<InstalledGame> Games, IReadOnlyList<string> Warnings);
public enum GameExecutableOrigin { Folder, NameMatch, Metadata }
public sealed record GameExecutable(string Path, string RelativePath, GameExecutableOrigin Origin)
{
    public override string ToString() => RelativePath;
}
public sealed record GameExecutables(IReadOnlyList<GameExecutable> Files, bool Limited);
public sealed record SelectedGame(string Name, string Source, string Executable);

public static class InstalledGames
{
    public static GameLibrary Read()
    {
        var games = new List<InstalledGame>(); var warnings = new List<string>();
        void Provider(string name, Action read)
        {
            try { read(); }
            catch (Exception ex) when (ReadFailure(ex)) { warnings.Add(name); }
        }
        Provider("Steam", () =>
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                if (key?.GetValue("SteamPath") is string root && LocalDirectory(root)) roots.Add(System.IO.Path.GetFullPath(root));
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = hive.OpenSubKey(@"SOFTWARE\Valve\Steam");
                if (key?.GetValue("InstallPath") is string root && LocalDirectory(root)) roots.Add(System.IO.Path.GetFullPath(root));
            }
            foreach (string root in roots.ToArray())
            {
                string file = System.IO.Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(file)) continue;
                foreach (string library in SteamLibraries(ReadText(file))) if (LocalDirectory(library)) roots.Add(library);
            }
            foreach (string root in roots)
                Provider("Steam", () =>
                {
                    string apps = System.IO.Path.Combine(root, "steamapps");
                    if (!System.IO.Directory.Exists(apps)) return;
                    foreach (string file in System.IO.Directory.EnumerateFiles(apps, "appmanifest_*.acf").Take(4096))
                        Provider("Steam", () => { if (SteamManifest(ReadText(file), root) is { } game) games.Add(game); });
                });
        });
        Provider("Epic Games", () =>
        {
            // Local install metadata only. No login, web requests, launch commands or arguments.
            string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!System.IO.Directory.Exists(folder)) return;
            foreach (string file in System.IO.Directory.EnumerateFiles(folder, "*.item").Take(4096))
                Provider("Epic Games", () => { if (EpicManifest(ReadText(file)) is { } game) games.Add(game); });
        });
        Provider("GOG", () =>
        {
            foreach (var hiveName in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                using var hive = RegistryKey.OpenBaseKey(hiveName, view);
                using var root = hive.OpenSubKey(@"SOFTWARE\GOG.com\Games");
                if (root is null) continue;
                foreach (string id in root.GetSubKeyNames().Take(4096)) Provider("GOG", () =>
                {
                    if (!Digits(id)) return;
                    using var key = root.OpenSubKey(id);
                    if (key?.GetValue("path") is not string folder || !LocalDirectory(folder)) return;
                    string manifest = System.IO.Path.Combine(folder, "goggame-" + id + ".info");
                    if (Digits(id) && File.Exists(manifest) && GogManifest(ReadText(manifest), folder) is { } game) games.Add(game);
                    else if (key.GetValue("gameName") is string name && name.Length is > 0 and <= 300)
                    {
                        string? exe = key.GetValue("exe") as string;
                        string? target = exe is not null ? GameFiles.ContainedExecutable(folder, exe) : null;
                        games.Add(new(id, name, "GOG", System.IO.Path.GetFullPath(folder), target is null ? [] : [target]));
                    }
                });
            }
        });
        return new(games.DistinctBy(g => g.Source + "\n" + g.Id + "\n" + g.Directory.ToUpperInvariant()).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).Take(4096).ToList(), warnings.Distinct().ToList());
    }
    public static bool ReadFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException or ArgumentException or InvalidDataException or RegexMatchTimeoutException;
    public static bool LocalDirectory(string path) => !string.IsNullOrWhiteSpace(path) && System.IO.Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal) && System.IO.Directory.Exists(path);
    private static bool Digits(string id) => id.Length is > 0 and <= 20 && id.All(char.IsAsciiDigit);
    private static string ReadText(string file)
    {
        if (new FileInfo(file).Length > 2 * 1024 * 1024) throw new InvalidDataException("Game metadata is too large.");
        return File.ReadAllText(file);
    }
    public static IReadOnlyList<string> SteamLibraries(string content)
    {
        var values = Vdf(content);
        return values.Where(v => v.Key.Equals("path", StringComparison.OrdinalIgnoreCase) || Digits(v.Key))
            .Select(v => v.Value).Where(v => System.IO.Path.IsPathFullyQualified(v) && !v.StartsWith(@"\\", StringComparison.Ordinal)).Distinct(StringComparer.OrdinalIgnoreCase).Take(128).ToList();
    }
    private static List<KeyValuePair<string, string>> Vdf(string text)
    {
        if (text.Length > 2 * 1024 * 1024) throw new InvalidDataException("Game metadata is too large.");
        // Tokenize quoted KeyValues; braces break key/value pairing. Comments outside quotes are ignored.
        var tokens = Regex.Matches(text, "//[^\\r\\n]*|\"((?:\\\\.|[^\"\\\\])*)\"|[{}]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var pairs = new List<KeyValuePair<string, string>>(); string? key = null;
        foreach (Match token in tokens)
        {
            if (token.Value.StartsWith("//", StringComparison.Ordinal)) continue;
            if (!token.Groups[1].Success) { key = null; continue; }
            string value = token.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
            if (key is null) key = value; else { pairs.Add(new(key, value)); key = null; }
        }
        return pairs;
    }
    public static InstalledGame? SteamManifest(string json, string library)
    {
        var values = Vdf(json);
        string Value(string key) => values.FirstOrDefault(v => v.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value ?? "";
        string id = Value("appid"), name = Value("name"), install = Value("installdir");
        if (!Digits(id) || name.Length is < 1 or > 300 || install.Length == 0 || install.Contains('/') || install.Contains('\\') || install is "." or ".." || install.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) return null;
        // Valve's common redistributable/tool entries are not games.
        if (id is "228980" or "1007" or "1070560") return null;
        string folder = System.IO.Path.Combine(library, "steamapps", "common", install);
        if (!LocalDirectory(folder)) return null;
        var declared = new List<string>();
        if (id == "730" && GameFiles.ContainedExecutable(folder, @"game\bin\win64\cs2.exe") is { } cs2) declared.Add(cs2);
        return new(id, name, "Steam", System.IO.Path.GetFullPath(folder), declared);
    }
    private static string String(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static bool True(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    public static InstalledGame? EpicManifest(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = doc.RootElement; string id = String(root, "AppName"), name = String(root, "DisplayName"), folder = String(root, "InstallLocation"), main = String(root, "MainGameAppName");
        if (id.Length is < 1 or > 300 || name.Length is < 1 or > 300 || !LocalDirectory(folder) || True(root, "bIsIncompleteInstall") || main.Length > 0 && !main.Equals(id, StringComparison.OrdinalIgnoreCase)) return null;
        string? exe = GameFiles.ContainedExecutable(folder, String(root, "LaunchExecutable"));
        return new(id, name, "Epic Games", System.IO.Path.GetFullPath(folder), exe is null ? [] : [exe]);
    }
    public static InstalledGame? GogManifest(string json, string folder)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = doc.RootElement; string id = String(root, "gameId"), name = String(root, "name");
        if (!Digits(id) || name.Length is < 1 or > 300 || !LocalDirectory(folder)) return null;
        var targets = new List<string>();
        if (root.TryGetProperty("playTasks", out var tasks) && tasks.ValueKind == JsonValueKind.Array)
            foreach (var task in tasks.EnumerateArray().Take(64))
                if (True(task, "isPrimary") && String(task, "type").Equals("FileTask", StringComparison.OrdinalIgnoreCase) && GameFiles.ContainedExecutable(folder, String(task, "path")) is { } exe) targets.Add(exe);
        return new(id, name, "GOG", System.IO.Path.GetFullPath(folder), targets.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }
}

public static class GameFiles
{
    private static readonly HashSet<string> ExcludedFolders = new(StringComparer.OrdinalIgnoreCase) { "_CommonRedist", "redist", "redistributables", "support", "__installer", "installer", "EasyAntiCheat", "EasyAntiCheat_EOS", "BattlEye", "Engine", ".egstore", "node_modules" };
    private static bool Helper(string path) => Regex.IsMatch(System.IO.Path.GetFileNameWithoutExtension(path), @"^(unins\d*|uninstall.*|setup|install|installer|dxsetup|vcredist.*|vc_redist.*|UnityCrashHandler.*|CrashReportClient.*|crashpad_handler|EasyAntiCheat.*|BEService.*|steam|steamservice|steamwebhelper|steamerrorreporter.*|EpicGamesLauncher|GalaxyClient)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    public static string? ContainedExecutable(string root, string path)
    {
        try
        {
            if (!InstalledGames.LocalDirectory(root) || string.IsNullOrWhiteSpace(path) || path.Contains('"') || path.Contains('\0') || path.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            string folder = System.IO.Path.GetFullPath(root).TrimEnd('\\', '/');
            string candidate = System.IO.Path.GetFullPath(System.IO.Path.IsPathFullyQualified(path) ? path : System.IO.Path.Combine(folder, path.Replace('/', '\\')));
            if (!candidate.StartsWith(folder + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate) || Helper(candidate)) return null;
            string relative = System.IO.Path.GetRelativePath(folder, candidate);
            if (relative.Contains(':')) return null;
            if (relative.Split('\\', '/').Any(p => ExcludedFolders.Contains(p))) return null;
            string current = folder;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
            foreach (string part in relative.Split('\\', '/'))
            {
                current = System.IO.Path.Combine(current, part);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
            }
            return candidate;
        }
        catch (Exception ex) when (InstalledGames.ReadFailure(ex)) { return null; }
    }
    public static GameExecutables Find(InstalledGame game, CancellationToken cancellation = default)
    {
        var results = new Dictionary<string, GameExecutable>(StringComparer.OrdinalIgnoreCase);
        foreach (string declared in game.DeclaredExecutables)
            if (ContainedExecutable(game.Directory, declared) is { } path) results[path] = new(path, System.IO.Path.GetRelativePath(game.Directory, path), GameExecutableOrigin.Metadata);
        var queue = new Queue<(string Path, int Depth)>(); queue.Enqueue((game.Directory, 0));
        int visited = 0, directories = 0; bool limited = false; var watch = System.Diagnostics.Stopwatch.StartNew();
        string Compact(string text) => Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]", "");
        var names = new[] { Compact(game.Name), Compact(System.IO.Path.GetFileName(game.Directory)) };
        while (queue.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (visited >= 12000 || directories++ >= 300 || watch.Elapsed > TimeSpan.FromSeconds(3) || results.Count >= 80) { limited = true; break; }
            var item = queue.Dequeue();
            try
            {
                if ((File.GetAttributes(item.Path) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (string file in System.IO.Directory.EnumerateFiles(item.Path))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (++visited >= 12000 || results.Count >= 80 || watch.Elapsed > TimeSpan.FromSeconds(3)) { limited = true; break; }
                    if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || results.ContainsKey(file) || ContainedExecutable(game.Directory, file) is not { } target) continue;
                    string stem = Compact(System.IO.Path.GetFileNameWithoutExtension(file).Replace("-Win64-Shipping", "", StringComparison.OrdinalIgnoreCase));
                    results[target] = new(target, System.IO.Path.GetRelativePath(game.Directory, target), stem.Length > 2 && names.Contains(stem) ? GameExecutableOrigin.NameMatch : GameExecutableOrigin.Folder);
                }
                if (item.Depth >= 5) { limited |= System.IO.Directory.EnumerateDirectories(item.Path).Any(); continue; }
                var subfolders = System.IO.Directory.EnumerateDirectories(item.Path).Take(301).ToList();
                limited |= subfolders.Count > 300;
                foreach (string folder in subfolders.Take(300))
                    if (!ExcludedFolders.Contains(System.IO.Path.GetFileName(folder))) queue.Enqueue((folder, item.Depth + 1));
            }
            catch (Exception ex) when (InstalledGames.ReadFailure(ex)) { limited = true; }
        }
        return new(results.Values.OrderByDescending(e => e.Origin).ThenBy(e => e.RelativePath, StringComparer.CurrentCultureIgnoreCase).ToList(), limited);
    }
}
