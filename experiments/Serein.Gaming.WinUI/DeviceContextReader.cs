using Serein.Core;

namespace Serein_Gaming_WinUI;

internal static class DeviceContextReader
{
    public static DeviceContext Read()
    {
        var warnings = new List<string>(); var processors = new List<ProcessorInfo>(); ComputerModel? computer = null;
        var virtualAdapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<StorageDeviceInfo> storage = [];
        try { storage = StorageDeviceInfo.Read(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { warnings.Add("Storage metadata: " + ex.Message); }
        try
        {
            foreach (var row in Wmi.Query("SELECT Name, SocketDesignation, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                processors.Add(new(row.Text("Name"), row.Text("SocketDesignation"), Positive(row.Number("NumberOfCores")), Positive(row.Number("NumberOfLogicalProcessors"))));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { warnings.Add("Processor metadata: " + ex.Message); }
        try { computer = ComputerModel.Read(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { warnings.Add("Computer metadata: " + ex.Message); }
        try
        {
            foreach (var row in Wmi.QueryInNamespace("root\\StandardCimv2", "SELECT PnPDeviceID, HardwareInterface, Virtual FROM MSFT_NetAdapter"))
            {
                bool software = row.Values.TryGetValue("Virtual", out var isVirtual) && isVirtual is true
                    || row.Values.TryGetValue("HardwareInterface", out var hardware) && hardware is false;
                if (software && row.Text("PnPDeviceID").Length > 0) virtualAdapters.Add(row.Text("PnPDeviceID"));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { warnings.Add("Extended network metadata: " + ex.Message); }
        try
        {
            foreach (var row in Wmi.Query("SELECT PNPDeviceID, PhysicalAdapter FROM Win32_NetworkAdapter"))
                if (row.Values.TryGetValue("PhysicalAdapter", out var physical) && physical is false && row.Text("PNPDeviceID").Length > 0) virtualAdapters.Add(row.Text("PNPDeviceID"));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { warnings.Add("Network metadata: " + ex.Message); }
        return new(processors, computer, virtualAdapters, warnings) { Storage = storage };
    }
    private static int? Positive(long value) => value is > 0 and <= int.MaxValue ? (int)value : null;
}
