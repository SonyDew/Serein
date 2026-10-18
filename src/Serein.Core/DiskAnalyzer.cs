namespace Serein.Core;
public record LargeFile(string Path, long Bytes, DateTime ModifiedUtc) { public double MiB => Math.Round(Bytes / 1048576d, 2); }
public record DiskScan(List<LargeFile> Files, List<string> Errors, bool Complete, long ObservedBytes);
public static class DiskAnalyzer
{
    public static DiskScan Read(string directory, int maxEntries = 20000)
    {
        if (maxEntries is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        var files = new List<LargeFile>(); var errors = new List<string>(); var pending = new Stack<string>(); pending.Push(System.IO.Path.GetFullPath(directory));
        int entries = 0; long bytes = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
        while (pending.Count > 0 && entries < maxEntries && clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            var folder = pending.Pop();
            try
            {
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) { errors.Add("Redirected folder skipped: " + folder); continue; }
                foreach (var path in Directory.EnumerateFileSystemEntries(folder))
                {
                    if (++entries > maxEntries) break;
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0) { pending.Push(path); continue; }
                    var file = new FileInfo(path); bytes += file.Length; files.Add(new(file.FullName, file.Length, file.LastWriteTimeUtc));
                    if (files.Count > 1000) files = files.OrderByDescending(f => f.Bytes).Take(500).ToList();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add(folder + ": " + ex.Message); }
        }
        bool complete = pending.Count == 0 && entries < maxEntries && errors.Count == 0;
        if (!complete) errors.Add("Partial scan: entry/time limit, inaccessible or redirected paths. Observed total is not a full disk total.");
        return new(files.OrderByDescending(f => f.Bytes).Take(500).ToList(), errors, complete, bytes);
    }
}
