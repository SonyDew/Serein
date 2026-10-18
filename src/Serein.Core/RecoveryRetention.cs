namespace Serein.Core;
public record RetentionResult(int Deleted, int Retained, List<string> Messages);
public static class RecoveryRetention
{
    public static RetentionResult Prune(string directory, int keep = 128, Func<ProcessIdentity, bool>? live = null)
    {
        if (keep is < 1 or > 1024) throw new InvalidDataException("Invalid recovery retention bound.");
        if (!Directory.Exists(directory)) return new(0, 0, []);
        live ??= Native.Matches; PackageFiles.NormalPath(directory);
        var completed = new List<FileInfo>(); var messages = new List<string>(); int retained = 0, deleted = 0;
        foreach (var path in Directory.EnumerateFiles(directory).Where(p => Path.GetExtension(p) is ".json" or ".power").Take(4096))
        {
            try
            {
                PackageFiles.NormalPath(path); ProcessIdentity host; bool done;
                if (Path.GetExtension(path) == ".power") { var lease = JsonFiles.Read<PowerLease>(path); lease.Validate(); host = lease.Host; done = lease.Status == ChangeStatus.Reverted; }
                else { var journal = JsonFiles.Read<Journal>(path); journal.Validate(); host = journal.Host; done = journal.Changes.All(c => c.Status == ChangeStatus.Reverted); }
                if (!done || live(host)) { retained++; continue; } completed.Add(new(path));
            }
            catch (Exception ex) { retained++; messages.Add(Path.GetFileName(path) + ": retained: " + ex.Message); }
        }
        foreach (var file in completed.OrderByDescending(f => f.LastWriteTimeUtc).Skip(keep))
        {
            try { PackageFiles.NormalPath(file.FullName); File.Delete(file.FullName); deleted++; }
            catch (Exception ex) { retained++; messages.Add(file.Name + ": retention failed: " + ex.Message); }
        }
        return new(deleted, retained + Math.Min(keep, completed.Count), messages);
    }
}
