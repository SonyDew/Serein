using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Serein.Core;

public record CleanupFile(string Path, long Length, DateTime LastWriteUtc, DateTime CreatedUtc);
public record CleanupResult(string Path, bool Deleted, string Message, long Bytes);
public sealed class CleanupService
{
    private readonly string root;
    public CleanupService(string directory)
    {
        root = System.IO.Path.GetFullPath(directory).TrimEnd('\\') + "\\";
        if (!Directory.Exists(root) || IsReparse(root)) throw new InvalidOperationException("Cleanup root missing or redirected.");
    }
    private static bool IsReparse(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private void ValidatePath(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || full.Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Path outside cleanup root.");
        for (string? node = full; node is not null && node.Length >= root.TrimEnd('\\').Length; node = System.IO.Path.GetDirectoryName(node))
            if (IsReparse(node)) throw new InvalidOperationException("Reparse point excluded.");
    }
    public (List<CleanupFile> Files, List<string> Errors) Preview(TimeSpan minimumAge)
    {
        if (minimumAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(minimumAge));
        var files = new List<CleanupFile>(); var errors = new List<string>();
        var pending = new Stack<string>(); pending.Push(root); int count = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (pending.Count > 0 && count < 10000 && clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            string directory = pending.Pop();
            try
            {
                ValidatePath(directory.TrimEnd('\\') + "\\");
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (++count > 10000) break;
                    if (IsReparse(path)) continue;
                    if (Directory.Exists(path)) { pending.Push(path); continue; }
                    var file = new FileInfo(path);
                    if (DateTime.UtcNow - file.LastWriteTimeUtc >= minimumAge) files.Add(new(file.FullName, file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { errors.Add($"{directory}: {ex.Message}"); }
        }
        if (pending.Count > 0 || count >= 10000) errors.Add("Preview capped at 10,000 entries or 10 seconds; only listed files can be selected.");
        return (files, errors);
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct FileInfoNative
    {
        public uint Attributes; public long Creation, Access, Write; public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Disposition { [MarshalAs(UnmanagedType.Bool)] public bool Delete; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfoNative info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetFileInformationByHandle(SafeFileHandle file, int cls, ref Disposition info, uint size);
    public CleanupResult Delete(CleanupFile candidate)
    {
        try
        {
            ValidatePath(candidate.Path);
            using (var handle = CreateFile(candidate.Path, 0x10080, 0, 0, 3, 0x00200000, 0))
            {
                if (handle.IsInvalid) throw Native.Error();
                if (!GetFileInformationByHandle(handle, out var info)) throw Native.Error();
                long length = ((long)info.SizeHigh << 32) | info.SizeLow;
                if ((info.Attributes & 0x410) != 0 || length != candidate.Length || info.Write != candidate.LastWriteUtc.ToFileTimeUtc() || info.Creation != candidate.CreatedUtc.ToFileTimeUtc())
                    throw new InvalidOperationException("Preview identity changed; rescan before deleting.");
                var disposition = new Disposition { Delete = true };
                if (!SetFileInformationByHandle(handle, 4, ref disposition, (uint)Marshal.SizeOf<Disposition>())) throw Native.Error();
            }
            return new(candidate.Path, true, "Deleted via exact file handle", candidate.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return new(candidate.Path, false, ex.Message, 0); }
    }
}
