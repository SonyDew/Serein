using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Serein.Core;

public sealed record WmiRow(Dictionary<string, object?> Values)
{
    public string Text(string key) => Values.TryGetValue(key, out var value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "" : "";
    public long Number(string key) => long.TryParse(Text(key), out long value) ? value : 0;
}
public static class Wmi
{
    public static List<WmiRow> Query(string query) => QueryInNamespace("root\\cimv2", query);
    public static List<WmiRow> QueryInNamespace(string namespacePath, string query)
    {
        object? locator = null, services = null, results = null;
        var rows = new List<WmiRow>();
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator") ?? throw new NotSupportedException("WMI scripting unavailable."));
            services = ((dynamic)locator!).ConnectServer(".", namespacePath);
            // SWbem forward-only collections cannot satisfy the CLR enumerator's clone/reset contract.
            results = ((dynamic)services).ExecQuery(query, "WQL", 16);
            foreach (var item in (System.Collections.IEnumerable)results)
            {
                object? properties = null;
                try
                {
                    properties = ((dynamic)item).Properties_;
                    var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in (System.Collections.IEnumerable)properties)
                        try { values[(string)((dynamic)property).Name] = ((dynamic)property).Value; }
                        finally { Release(property); }
                    rows.Add(new(values));
                }
                finally { Release(properties); Release(item); }
            }
            return rows;
        }
        finally { Release(results); Release(services); Release(locator); }
    }
    public static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
}
public record DeviceRow(string Name, string InstanceId, string Class, string Manufacturer, string[] HardwareIds, string[] CompatibleIds, uint? ProblemCode, string Provider, string Version, string Date, string Inf, string Signature)
{
    public string Health => ProblemCode switch { 0 => "OK", null => "Unknown", 28 => "Missing driver (28)", _ => $"Problem {ProblemCode}" };
    public string Hardware => string.Join("; ", HardwareIds);
}
public record DriverAdvice(string Provider, Uri Url, string Evidence, string Status);
public record DriverCandidate(string Title, string HardwareId, string Provider, string Date, string UpdateId, string Match, int Revision = 0, string DeviceClass = "", string Eula = "", bool EulaAccepted = false, string Service = "Configured Windows Update service")
{
    public string Binding => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { Title, HardwareId, Provider, Date, UpdateId, Revision, DeviceClass, Eula, Service })));
}
public static class DriverRecommendations
{
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase) { "www.nvidia.com", "www.amd.com", "www.intel.com", "support.microsoft.com" };
    public static bool Trusted(Uri url) => DriverSources.Trusted(url) || url.Scheme == "https" && url.IsDefaultPort && string.IsNullOrEmpty(url.UserInfo) && Hosts.Contains(url.IdnHost);
    public static DriverAdvice For(DeviceRow device)
    {
        foreach (var id in device.HardwareIds)
        {
            var match = Regex.Match(id, @"^PCI\\VEN_([0-9A-F]{4})&", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            var vendor = match.Groups[1].Value.ToUpperInvariant();
            var result = vendor switch
            {
                "10DE" => new DriverAdvice("NVIDIA", new("https://www.nvidia.com/en-us/drivers/"), "PCI VEN_10DE identifies vendor; product/OS/OEM selection still required", "Official support page — update availability unknown"),
                "1002" or "1022" => new DriverAdvice("AMD", new("https://www.amd.com/en/support"), "PCI AMD vendor ID; verify exact model and OEM preference", "Official support page — update availability unknown"),
                "8086" => new DriverAdvice("Intel", new("https://www.intel.com/content/www/us/en/download-center/home.html"), "PCI VEN_8086 identifies vendor; DSA coverage is incomplete", "Official support page — update availability unknown"),
                _ => null
            };
            if (result is not null) return result;
        }
        return new("Windows / OEM", new("https://support.microsoft.com/windows"), "No verified vendor package match; use optional Windows Update drivers or exact OEM support", "Unknown — no package recommendation");
    }
    public static string Match(DeviceRow device, string id) => device.HardwareIds.Contains(id, StringComparer.OrdinalIgnoreCase) ? "Exact hardware ID; Windows still validates applicability/rank" : device.CompatibleIds.Contains(id, StringComparer.OrdinalIgnoreCase) ? "Compatible ID; lower-specificity match" : "Unmatched — do not install by device association";
    public static List<DriverCandidate> SearchWindowsUpdate(IReadOnlyList<DeviceRow> devices)
    {
        using var source = new WindowsUpdateDrivers(); return source.Search(devices);
    }
}
public static class DeviceInventory
{
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint DevInst; public nint Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevs(nint cls, string? enumerator, nint window, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInfo(nint set, uint index, ref DeviceInfo info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceRegistryProperty(nint set, ref DeviceInfo info, uint property, out uint type, byte[] buffer, uint length, out uint required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInstanceId(nint set, ref DeviceInfo info, StringBuilder buffer, uint length, out uint required);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devinst, uint flags);
    public static List<DeviceRow> Read()
    {
        var drivers = Wmi.Query("SELECT DeviceID, DriverVersion, DriverDate, DriverProviderName, InfName, IsSigned FROM Win32_PnPSignedDriver").GroupBy(d => d.Text("DeviceID"), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var set = SetupDiGetClassDevs(0, null, 0, 6);
        if (set == -1) throw Native.Error();
        var devices = new List<DeviceRow>();
        try
        {
            for (uint i = 0; ; i++)
            {
                var info = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref info)) { if (Marshal.GetLastWin32Error() != 259) throw Native.Error(); break; }
                var id = new StringBuilder(4096);
                if (!SetupDiGetDeviceInstanceId(set, ref info, id, (uint)id.Capacity, out _)) throw Native.Error();
                string instance = id.ToString(); drivers.TryGetValue(instance, out var driver);
                string[] Prop(uint p)
                {
                    var bytes = new byte[32768];
                    if (!SetupDiGetDeviceRegistryProperty(set, ref info, p, out _, bytes, (uint)bytes.Length, out uint required))
                    { int error = Marshal.GetLastWin32Error(); if (error is 13 or 2) return []; throw Native.Error(); }
                    return Encoding.Unicode.GetString(bytes, 0, (int)required).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                }
                uint? problem = CM_Get_DevNode_Status(out _, out var code, info.DevInst, 0) == 0 ? code : null;
                string name = Prop(12).FirstOrDefault() ?? Prop(0).FirstOrDefault() ?? instance;
                string date = driver?.Text("DriverDate") ?? "";
                if (date.Length >= 8) date = $"{date[..4]}-{date[4..6]}-{date[6..8]}";
                devices.Add(new(name, instance, Prop(7).FirstOrDefault() ?? "", Prop(11).FirstOrDefault() ?? "", Prop(1), Prop(2), problem, driver?.Text("DriverProviderName") ?? "Unknown", driver?.Text("DriverVersion") ?? "Unknown", date, driver?.Text("InfName") ?? "Unknown", driver?.Text("IsSigned") ?? "Unknown"));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return devices.OrderByDescending(d => d.ProblemCode > 0).ThenBy(d => d.Name).ToList();
    }
}
public record ServiceRow(string Name, string DisplayName, string State, string StartMode, int Pid, string Account);
public static class ServiceInventory
{
    public static List<ServiceRow> Read() => Wmi.Query("SELECT Name, DisplayName, State, StartMode, ProcessId, StartName FROM Win32_Service").Select(s => new ServiceRow(s.Text("Name"), s.Text("DisplayName"), s.Text("State"), s.Text("StartMode"), (int)s.Number("ProcessId"), s.Text("StartName"))).ToList();
}
