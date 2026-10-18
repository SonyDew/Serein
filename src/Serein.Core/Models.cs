using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Serein.Core;

public record ProcessIdentity(int Pid, long CreatedFileTime, string ImagePath);
public record ProcessRow(ProcessIdentity Identity, string Name, int ParentPid, string Owner, string Classification, string Reason, double? CpuPercent, long WorkingSet, long PrivateBytes, string Priority)
{
    public bool CanApprove { get; init; }
    public int Pid => Identity.Pid;
    public string Path => Identity.ImagePath;
    public double WorkingSetMb => Math.Round(WorkingSet / 1048576d, 1);
    public string Cpu => CpuPercent is double x ? $"{x:F1}%" : "—";
}
public record Approval(string Path, string Sha256);
public enum ProfileMode { Balanced, Performance, Gaming, ExtremeGaming, Work, PowerSaving }
public record Profile(int SchemaVersion, string Name, ProfileMode Mode, int DeadlineSeconds, List<Approval> AllowedApps, string? GamePath = null, Guid? PowerScheme = null)
{
    public static List<Profile> Presets => Enum.GetValues<ProfileMode>().Select(m => new Profile(1, m.ToString(), m, 600, [])).ToList();
    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || !Enum.IsDefined(Mode) || DeadlineSeconds is < 5 or > 1800 || AllowedApps is null || AllowedApps.Count > 64)
            throw new InvalidDataException("Invalid profile: schema, name, mode, lease or approval count.");
        foreach (var a in AllowedApps)
            if (a is null || !System.IO.Path.IsPathFullyQualified(a.Path) || !a.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || a.Sha256 is null || a.Sha256.Length != 64 || !a.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Approval requires an absolute EXE path and SHA256.");
        if (AllowedApps.Select(a => a.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != AllowedApps.Count)
            throw new InvalidDataException("Duplicate executable approval.");
        if (GamePath is not null && (!System.IO.Path.IsPathFullyQualified(GamePath) || !GamePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Invalid game executable.");
        if (PowerScheme == Guid.Empty) throw new InvalidDataException("Invalid optional power scheme GUID.");
    }
}
public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, Converters = { new JsonStringEnumConverter() } };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(ReadBounded(path), Options) ?? throw new InvalidDataException("Empty JSON.");
    public static string ReadBounded(string path)
    {
        if (new FileInfo(path).Length > 1048576) throw new InvalidDataException("JSON exceeds 1 MiB.");
        return File.ReadAllText(path);
    }
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, Options);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static Approval Approve(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        using var stream = File.OpenRead(path);
        return new(path, Convert.ToHexString(SHA256.HashData(stream)));
    }
}
public sealed class ProfileStore(string directory)
{
    private static void ValidateSet(List<Profile> profiles)
    {
        if (profiles.Count is < 1 or > 64 || profiles.Any(p => p is null)) throw new InvalidDataException("Invalid profile count or null entry.");
        profiles.ForEach(p => p.Validate());
        if (profiles.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != profiles.Count) throw new InvalidDataException("Duplicate profile names.");
    }
    public List<Profile> Load()
    {
        var path = System.IO.Path.Combine(directory, "profiles.json");
        if (!File.Exists(path)) { var presets = Profile.Presets; Save(presets); return presets; }
        var profiles = JsonFiles.Read<List<Profile>>(path);
        ValidateSet(profiles);
        return profiles;
    }
    public void Save(List<Profile> profiles) { ValidateSet(profiles); JsonFiles.Write(System.IO.Path.Combine(directory, "profiles.json"), profiles); }
    public Profile Import(string path) { var p = JsonFiles.Read<Profile>(path); p.Validate(); return p; }
}
public static class AppPaths
{
    public static string Data => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Serein");
}
public record Preferences(int SchemaVersion = 1, bool English = false, bool LightTheme = false, bool CloseToTray = true, bool Notifications = true, string AccentColor = "#8B9FFF", bool ReducedMotion = false)
{
    public void Validate()
    {
        if (SchemaVersion != 1 || AccentColor is null || AccentColor.Length != 7 || AccentColor[0] != '#' || !AccentColor.AsSpan(1).ToString().All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid appearance preferences; use an opaque #RRGGBB accent.");
    }
}
public sealed class AuditLog(string directory)
{
    private readonly object gate = new();
    public void Write(string message)
    {
        lock (gate)
        {
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "audit.log");
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Move(path, path + ".1", true);
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {message.Replace('\r', ' ').Replace('\n', ' ')}{Environment.NewLine}");
        }
    }
}
