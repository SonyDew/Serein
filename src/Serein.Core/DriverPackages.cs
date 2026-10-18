using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Serein.Core;
public record PackageFile(string RelativePath, long Length, string Sha256);
public record InfMatch(string InstanceId, string DeviceName, string Provider, string Version, uint Rank);
public record InfPackage(string Directory, string Inf, string DeviceClass, string Signer, string Catalog, List<PackageFile> Files, List<InfMatch> Matches)
{
    public string Binding => Convert.ToHexString(SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { Inf, DeviceClass, Signer, Catalog, Files, Matches })));
}
public static class PackageFiles
{
    public static void NormalPath(string path)
    {
        path = Path.GetFullPath(path);
        if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0) throw new InvalidDataException("Network, device and alternate-stream paths are not supported.");
        for (string? item = path; !string.IsNullOrEmpty(item); item = Path.GetDirectoryName(item))
            if (File.Exists(item) || System.IO.Directory.Exists(item))
                if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Redirected package path refused.");
    }
    public static void Relative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Split('\\', '/').Any(p => p is "" or "." or "..") || path.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0) throw new InvalidDataException("Unsafe package member.");
    }
    public static List<PackageFile> Snapshot(string folder)
    {
        folder = Path.GetFullPath(folder); NormalPath(folder); var files = new List<PackageFile>(); long total = 0;
        var pending = new Stack<string>(); pending.Push(folder); int directories = 0;
        while (pending.TryPop(out string? current))
        {
            if (++directories > 128) throw new InvalidDataException("Package directory limit exceeded.");
            foreach (var item in System.IO.Directory.EnumerateFileSystemEntries(current))
            {
                NormalPath(item);
                if (System.IO.Directory.Exists(item)) { pending.Push(item); continue; }
                var info = new FileInfo(item); total = checked(total + info.Length);
                if (files.Count >= 512 || total > 1024L * 1024 * 1024) throw new InvalidDataException("Package exceeds 512 files / 1 GiB; use official OEM installer.");
                using var stream = new FileStream(item, FileMode.Open, FileAccess.Read, FileShare.Read);
                string relative = Path.GetRelativePath(folder, item); Relative(relative);
                files.Add(new(relative, stream.Length, Convert.ToHexString(SHA256.HashData(stream))));
            }
        }
        return files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static void Verify(string folder, IReadOnlyList<PackageFile> expected)
    {
        if (expected.Count is < 2 or > 512 || expected.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != expected.Count) throw new InvalidDataException("Invalid package manifest.");
        foreach (var file in expected) { Relative(file.RelativePath); if (file.Length < 0 || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid member hash/size."); }
        if (!Snapshot(folder).SequenceEqual(expected)) throw new InvalidOperationException("Package contents changed after review.");
    }
    public static string CopyVerified(InfPackage package, string emptyDestination)
    {
        if (!System.IO.Directory.Exists(emptyDestination) || System.IO.Directory.EnumerateFileSystemEntries(emptyDestination).Any()) throw new InvalidDataException("Empty staging directory required.");
        NormalPath(emptyDestination); Relative(package.Inf); Verify(package.Directory, package.Files);
        foreach (var entry in package.Files)
        {
            string source = Path.Combine(package.Directory, entry.RelativePath), destination = Path.Combine(emptyDestination, entry.RelativePath);
            NormalPath(source); System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None); input.CopyTo(output); output.Flush(true);
        }
        Verify(emptyDestination, package.Files); return Path.Combine(emptyDestination, package.Inf);
    }
}
public static class InfPackages
{
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint DevInst; public nint Reserved; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct InstallParams
    {
        public uint Size, Flags, FlagsEx; public nint Window, Callback, Context, Queue, ClassReserved; public uint Reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Path;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DriverInfo
    {
        public uint Size, Type; public nint Reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Manufacturer;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Provider;
        public long Date; public ulong Version;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DriverParams { public uint Size, Rank, Flags; public nint Private; public uint Reserved; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SignerInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Catalog;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Signer;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Version;
        public uint Score;
    }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupVerifyInfFile(string inf, nint platform, ref SignerInfo signer);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetINFClass(string inf, out Guid cls, StringBuilder name, uint size, out uint required);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern nint SetupDiCreateDeviceInfoList(nint cls, nint window);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiOpenDeviceInfo(nint set, string id, nint window, uint flags, ref DeviceInfo device);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInstallParams(nint set, ref DeviceInfo device, ref InstallParams parameters);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiSetDeviceInstallParams(nint set, ref DeviceInfo device, ref InstallParams parameters);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiBuildDriverInfoList(nint set, ref DeviceInfo device, uint type);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiEnumDriverInfo(nint set, ref DeviceInfo device, uint type, uint index, ref DriverInfo driver);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDriverInstallParams(nint set, ref DeviceInfo device, ref DriverInfo driver, ref DriverParams parameters);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
    public static (int Device, int Install, int Driver, int Signer) Layouts => (Marshal.SizeOf<DeviceInfo>(), Marshal.SizeOf<InstallParams>(), Marshal.SizeOf<DriverInfo>(), Marshal.SizeOf<SignerInfo>());
    public static int RankLayout => Marshal.SizeOf<DriverParams>();
    public static void AllowedClass(string cls)
    {
        if (new[] { "Firmware", "Computer", "HDC", "SCSIAdapter", "Volume", "DiskDrive", "System" }.Contains(cls, StringComparer.OrdinalIgnoreCase)) throw new NotSupportedException("Firmware/boot/storage/system-class local INF installation is not supported. Use Windows Update/OEM guidance.");
    }
    public static InfPackage Inspect(string inf, IReadOnlyList<DeviceRow> devices)
    {
        inf = Path.GetFullPath(inf); PackageFiles.NormalPath(inf);
        if (!inf.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) || inf.Length >= 260) throw new InvalidDataException("Choose a local INF with a path shorter than MAX_PATH.");
        var signer = new SignerInfo { Size = (uint)Marshal.SizeOf<SignerInfo>() };
        if (!SetupVerifyInfFile(inf, 0, ref signer)) throw new InvalidOperationException($"Windows catalog verification rejected this INF (0x{Marshal.GetLastWin32Error():X8}). No installation allowed.");
        var className = new StringBuilder(256);
        if (!SetupDiGetINFClass(inf, out _, className, (uint)className.Capacity, out _)) throw Native.Error(); AllowedClass(className.ToString());
        string folder = Path.GetDirectoryName(inf)!; var files = PackageFiles.Snapshot(folder);
        string catalog = Path.GetFileName(signer.Catalog);
        if (!files.Any(f => f.RelativePath.Equals(catalog, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("The verified catalog must be included beside this INF.");
        var matches = new List<InfMatch>();
        foreach (var device in devices.Where(d => d.Class.Equals(className.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            nint set = SetupDiCreateDeviceInfoList(0, 0); if (set == -1) throw Native.Error();
            try
            {
                var data = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiOpenDeviceInfo(set, device.InstanceId, 0, 0, ref data)) throw Native.Error();
                var parameters = new InstallParams { Size = (uint)Marshal.SizeOf<InstallParams>(), Path = "" };
                if (!SetupDiGetDeviceInstallParams(set, ref data, ref parameters)) throw Native.Error();
                parameters.Flags |= 0x10000; parameters.Path = inf;
                if (!SetupDiSetDeviceInstallParams(set, ref data, ref parameters) || !SetupDiBuildDriverInfoList(set, ref data, 2)) throw Native.Error();
                for (uint i = 0; i < 256; i++)
                {
                    var driver = new DriverInfo { Size = (uint)Marshal.SizeOf<DriverInfo>() };
                    if (!SetupDiEnumDriverInfo(set, ref data, 2, i, ref driver)) { if (Marshal.GetLastWin32Error() != 259) throw Native.Error(); break; }
                    var rank = new DriverParams { Size = (uint)Marshal.SizeOf<DriverParams>() };
                    if (!SetupDiGetDriverInstallParams(set, ref data, ref driver, ref rank)) throw Native.Error();
                    string version = $"{driver.Version >> 48}.{(driver.Version >> 32) & 65535}.{(driver.Version >> 16) & 65535}.{driver.Version & 65535}";
                    matches.Add(new(device.InstanceId, device.Name, driver.Provider, version, rank.Rank));
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
        if (matches.Count == 0) throw new InvalidOperationException("Windows found no compatible driver node for present devices on this OS/architecture.");
        return new(folder, Path.GetFileName(inf), className.ToString(), signer.Signer, catalog, files, matches.OrderBy(m => m.InstanceId, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Rank).ThenBy(m => m.Provider, StringComparer.Ordinal).ThenBy(m => m.Version, StringComparer.Ordinal).ToList());
    }
}
