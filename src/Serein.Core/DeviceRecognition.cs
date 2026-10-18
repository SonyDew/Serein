using System.Text.RegularExpressions;

namespace Serein.Core;

public enum DeviceNature { Hardware, WindowsComponent, Software, Unknown }
public sealed record StorageDeviceInfo(string InstanceId, string Model, string Firmware, long SizeBytes)
{
    public static List<StorageDeviceInfo> Read() => Wmi.Query("SELECT PNPDeviceID, Model, FirmwareRevision, Size FROM Win32_DiskDrive")
        .Where(r => r.Text("PNPDeviceID").Length > 0)
        .Select(r => new StorageDeviceInfo(r.Text("PNPDeviceID"), r.Text("Model").Trim(), r.Text("FirmwareRevision").Trim(), r.Number("Size"))).ToList();
}
public sealed record RecognizedDevice(string Name, string? Vendor, DeviceNature Nature, string Evidence, StorageDeviceInfo? Storage = null)
{
    public bool IsStorage => Storage is not null;
}

// Recognition supplies display/support provenance only. It never proves package applicability.
// Driver matching continues to use the untouched original opaque PnP identities.
public static class DeviceRecognition
{
    private static bool Is(string value, string other) => value.Equals(other, StringComparison.OrdinalIgnoreCase);
    private static bool Prefix(string value, string prefix) => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    private static Match Match(string value, string pattern) => Regex.Match(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static RecognizedDevice Resolve(DeviceRow row, StorageDeviceInfo? storage = null)
    {
        // A caller must join metadata by exact instance ID, never a coincidentally identical model.
        if (storage is not null && (!Is(row.Class, "DiskDrive") || !Is(row.InstanceId, storage.InstanceId))) storage = null;
        string name = storage?.Model is { Length: > 0 } model ? model : row.Name.Trim();
        if (Is(row.Class, "DiskDrive") && (Prefix(row.InstanceId, "STORAGE\\DISK\\") && Is(name, "Microsoft Storage Space Device")))
            return new(name, null, DeviceNature.Software, UiStrings.Get("Text330"), storage);
        if (new[] { "SoftwareDevice", "SoftwareComponent", "AudioEndpoint", "Volume", "VolumeSnapshot", "PrintQueue" }.Contains(row.Class, StringComparer.OrdinalIgnoreCase) || Prefix(row.InstanceId, "SWD\\") || Prefix(row.InstanceId, "SW\\") || Is(row.InstanceId, @"HTREE\ROOT\0") || Prefix(row.InstanceId, @"ROOT\SPACEPORT\"))
            return new(name, null, DeviceNature.WindowsComponent, UiStrings.Get("Text331"), storage);
        if (Prefix(row.InstanceId, "ROOT\\") && new[] { "MEDIA", "Camera", "Image", "Net" }.Contains(row.Class, StringComparer.OrdinalIgnoreCase))
            return new(name, null, DeviceNature.Software, UiStrings.Get("Text332"), storage);

        if (Is(row.Class, "DiskDrive"))
        {
            string clean = Regex.Replace(name, @"^(NVMe\s+|ATA\s+)", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
            clean = Regex.Replace(clean, @"\s+(SCSI Disk Device|ATA Device)$", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
            string? family = Match(clean, @"^CT(500|1000|2000|4000)P3PSSD8$").Success ? "P3 Plus"
                : Match(clean, @"^CT(120|250|500|1000)BX100SSD1$").Success ? "BX100" : null;
            if (family is not null) return new($"Crucial {family} · {clean}", "Crucial", DeviceNature.Hardware, UiStrings.Get("Text333"), storage);
            string? diskVendor = StorageVendor(clean) ?? VendorAlias(row.Manufacturer);
            if (diskVendor is not null && new[] { "Crucial", "Micron", "Samsung", "Kingston", "Apacer" }.Contains(diskVendor))
                return new(name, diskVendor, DeviceNature.Hardware, UiStrings.Get("Text334"), storage);
            return new(name, null, DeviceNature.Hardware, UiStrings.Get("Text335"), storage);
        }

        var vendors = row.HardwareIds.Select(VendorFromId).Where(v => v is not null).Distinct().ToList();
        if (vendors.Count == 1) return new(name, vendors[0], DeviceNature.Hardware, UiStrings.Get("Text336"));
        if (vendors.Count > 1) return new(name, null, DeviceNature.Unknown, UiStrings.Get("Text337"));
        if (Is(row.Class, "MEDIA") && Match(name, @"^ROG DELTA II \(2\.4GHz\)$").Success)
            return new(name, "ASUS", DeviceNature.Hardware, UiStrings.Get("Text338"));
        string? manufacturer = VendorAlias(row.Manufacturer);
        // The installed driver publisher is deliberately not a hardware manufacturer fallback.
        if (manufacturer is not null) return new(name, manufacturer, DeviceNature.Hardware, UiStrings.Get("Text339"));
        bool system = new[] { "System", "Computer", "Processor", "SecurityDevices" }.Contains(row.Class, StringComparer.OrdinalIgnoreCase);
        return new(name, null, system ? DeviceNature.WindowsComponent : DeviceNature.Unknown,
            system ? UiStrings.Get("Text340") : UiStrings.Get("Text341"));
    }
    private static string? StorageVendor(string model)
    {
        foreach (string brand in new[] { "Crucial", "Micron", "Samsung", "Kingston", "Apacer" })
            if (Match(model, "^" + brand + @"(\s|_|$)").Success) return brand;
        return null;
    }
    public static string? VendorAlias(string value)
    {
        string normalized = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", "");
        return normalized switch
        {
            "crucial" or "crucialbymicron" => "Crucial", "micron" or "microntechnologyinc" => "Micron",
            "samsung" or "samsungelectronicscoltd" or "samsungelectronics" => "Samsung",
            "kingston" or "kingstontechnology" or "kingstontechnologycompanyinc" => "Kingston",
            "apacer" or "apacertechnologyinc" => "Apacer",
            "amd" or "advancedmicrodevicesinc" => "AMD", "intel" or "intelcorporation" => "Intel",
            "nvidia" or "nvidiacorporation" => "NVIDIA", "realtek" or "realteksemiconductorcorp" => "Realtek",
            "tplink" or "tplinksystemsinc" or "tplinktechnologiescoltd" => "TP-Link",
            "logitech" or "logitechinc" => "Logitech", "razer" or "razerinc" => "Razer", "corsair" or "corsaircomponentsinc" => "Corsair", "steelseries" or "steelseriesaps" => "SteelSeries", _ => null
        } ?? DriverSources.Brand(value);
    }
    private static string? VendorFromId(string id)
    {
        var match = Match(id, @"^PCI\\VEN_([0-9A-F]{4})&");
        if (!match.Success) match = Match(id, @"^HDAUDIO\\FUNC_[0-9A-F]{2}&VEN_([0-9A-F]{4})&");
        if (match.Success) return match.Groups[1].Value.ToUpperInvariant() switch
        {
            "10DE" => "NVIDIA", "1002" or "1022" => "AMD", "8086" => "Intel", "10EC" => "Realtek", "168C" or "17CB" => "Qualcomm", _ => null
        };
        return Match(id, @"^USB\\VID_0BDA&").Success ? "Realtek" : null;
    }
}
