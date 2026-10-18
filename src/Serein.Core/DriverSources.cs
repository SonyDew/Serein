using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Serein.Core;
public record ComputerModel(string Manufacturer, string Model, string BoardManufacturer, string BoardModel)
{
    public static ComputerModel Read()
    {
        var pc = Wmi.Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem").FirstOrDefault(); var board = Wmi.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        return new(pc?.Text("Manufacturer") ?? "", pc?.Text("Model") ?? "", board?.Text("Manufacturer") ?? "", board?.Text("Product") ?? "");
    }
}
public record DriverSource(string Name, Uri Url, string Evidence, string Coverage = "Official support route; exact package/availability unknown");
public static class DriverSources
{
    public static IReadOnlyList<DriverSource> Catalog { get; } = new List<DriverSource>
    {
        new("Dell", new("https://www.dell.com/support/home/en-us?app=drivers"), "Model or Service Tag; Dell OEM packages"),
        new("Lenovo", new("https://pcsupport.lenovo.com/us/en/"), "Exact model/series; Lenovo System Update supported families"),
        new("HP", new("https://support.hp.com/us-en/drivers"), "Product model/number and OS; business PCs can use HP Image Assistant"),
        new("ASUS", new("https://www.asus.com/support/download-center/"), "Exact laptop/motherboard model; OEM/MyASUS customizations"),
        new("Acer", new("https://www.acer.com/us-en/support/drivers-and-manuals"), "Exact model/SNID/part number"),
        new("MSI", new("https://www.msi.com/support/download/"), "Exact product/model; distinguish drivers from BIOS"),
        new("GIGABYTE", new("https://www.gigabyte.com/Support/Consumer/Download"), "Exact motherboard/laptop model and revision"),
        new("ASRock", new("https://www.asrock.com/support/index.asp?cat=Drivers"), "Exact board model and OS"),
        new("Samsung", new("https://www.samsung.com/us/support/computing/windows-laptops/"), "Exact model code; regional availability differs"),
        new("NVIDIA", new("https://www.nvidia.com/en-us/drivers/"), "PCI VEN_10DE; product/OS/branch selection required"),
        new("AMD", new("https://www.amd.com/en/support"), "PCI VEN_1002/1022; prefer OEM laptop packages when customized"),
        new("Intel", new("https://www.intel.com/content/www/us/en/download-center/home.html"), "PCI VEN_8086; Intel DSA has limited product coverage"),
        new("Realtek", new("https://www.realtek.com/download/list"), "PCI VEN_10EC / USB VID_0BDA; OEM audio customizations may be required"),
        new("Qualcomm", new("https://www.qualcomm.com/drivers"), "Usually OEM distribution; do not select ARM64 packages for this x64 app"),
        new("Crucial", new("https://www.crucial.com/support/ssd-support"), "Recognized Crucial model or manufacturer; choose exact SSD; driver and firmware are different"),
        new("Crucial Storage Executive", new("https://www.crucial.com/support/storage-executive"), "Official SSD utility; review supported model and OS; not an identified driver update"),
        new("Micron", new("https://www.micron.com/sales-support/downloads/software-drivers"), "Micron hardware only; do not substitute data-center/OEM packages for consumer SSDs"),
        new("Samsung SSD", new("https://semiconductor.samsung.com/consumer-storage/support/tools/"), "SSD tools/firmware and model-specific NVMe driver support; not Samsung notebook support"),
        new("Kingston", new("https://www.kingston.com/en/support/technical/ssdmanager"), "SSD Manager coverage is model/OS dependent"),
        new("Apacer", new("https://www.apacer.com/en/download/personal-downloads"), "Official consumer download center; no compatible driver package asserted"),
        new("TP-Link", new("https://www.tp-link.com/en/support/download/"), "Exact manufacturer metadata; select exact model AND hardware revision"),
        new("Logitech", new("https://www.logitech.com/en-us/software"), "Exact manufacturer metadata; select supported device and software family"),
        new("Razer", new("https://www.razer.com/pc/software"), "Exact manufacturer metadata; model and application compatibility required"),
        new("Corsair", new("https://www.corsair.com/us/en/s/downloads"), "Exact manufacturer metadata; select supported product"),
        new("SteelSeries", new("https://steelseries.com/gg"), "Exact manufacturer metadata; supported peripherals/software only"),
        new("Microsoft Update Catalog", new("https://www.catalog.update.microsoft.com/"), "Manual Hardware ID search beyond local WUA assignment; still verify OS/architecture/catalog/INF")
    };
    private static readonly HashSet<string> SupportHosts = Catalog.Select(s => s.Url.IdnHost).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> DownloadHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "dl.dell.com", "download.lenovo.com", "download.microsoft.com", "download.windowsupdate.com", "ftp.hp.com", "dlcdnets.asus.com", "global-download.acer.com", "download.msi.com", "download.gigabyte.com", "download.asrock.com", "downloadmirror.intel.com", "us.download.nvidia.com", "international.download.nvidia.com"
    };
    private static bool Https(Uri url) => url.IsAbsoluteUri && url.Scheme == "https" && url.IsDefaultPort && string.IsNullOrEmpty(url.UserInfo);
    public static bool Trusted(Uri url) => Https(url) && SupportHosts.Contains(url.IdnHost);
    public static bool DownloadAllowed(Uri url) => Https(url) && DownloadHosts.Contains(url.IdnHost);
    public static string? Brand(string value)
    {
        string normalized = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", "");
        return normalized switch
        {
            "dellinc" or "dellcomputercorporation" or "dell" => "Dell",
            "lenovo" => "Lenovo", "hp" or "hewlettpackard" => "HP",
            "asus" or "asustekcomputerinc" or "asustekcomputerincorporated" => "ASUS",
            "acer" or "acerinc" or "acerincorporated" => "Acer",
            "msi" or "microstarinternationalcoltd" or "microstarinternationalco" => "MSI",
            "gigabytetechnologycoltd" or "gigabytetechnologyco" or "gigabyte" => "GIGABYTE",
            "asrock" or "asrockinc" => "ASRock", "samsungelectronicscoltd" or "samsungelectronics" or "samsung" => "Samsung", _ => null
        };
    }
    public static List<DriverSource> For(DeviceRow device, ComputerModel? pc, StorageDeviceInfo? storage = null)
    {
        // PC/board support belongs to a separate computer-level action, never a per-device fallback.
        _ = pc;
        var recognized = DeviceRecognition.Resolve(device, storage);
        string? name = recognized.Vendor == "Samsung" && device.Class.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase) ? "Samsung SSD" : recognized.Vendor;
        bool disk = device.Class.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase);
        bool controller = device.Class.Equals("SCSIAdapter", StringComparison.OrdinalIgnoreCase) || device.Class.Equals("HDC", StringComparison.OrdinalIgnoreCase);
        if (!disk && new[] { "Crucial", "Kingston", "Apacer" }.Contains(name) || name == "Micron" && !disk && !controller) name = null;
        // Samsung's notebook route must never be offered for arbitrary Samsung peripherals.
        var results = Catalog.Where(s => s.Name == name && s.Name != "Samsung").ToList();
        if (name == "ASUS" && device.Name.Equals("ROG DELTA II (2.4GHz)", StringComparison.OrdinalIgnoreCase))
            results = results.Select(s => s with { Url = new("https://www.asus.com/ca-en/supportonly/rog%20delta%20ii%20wireless%20gaming%20headset/helpdesk_download/") }).ToList();
        if (name == "Crucial" && device.Class.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase)) results.Add(Catalog.Single(s => s.Name == "Crucial Storage Executive"));
        string idQuery = device.HardwareIds.FirstOrDefault() ?? "";
        results.Add(Catalog[^1] with { Url = idQuery.Length > 0 ? new("https://www.catalog.update.microsoft.com/Search.aspx?q=" + Uri.EscapeDataString(idQuery)) : Catalog[^1].Url });
        return results;
    }
}
public record DownloadedDriverFile(string Path, string Sha256, long Bytes, string Source, string FinalSource);
public static class DriverDownloads
{
    public static async Task<DownloadedDriverFile> Fetch(Uri url, string emptyDirectory, HttpMessageHandler? testHandler = null, long maxBytes = 1024L * 1024 * 1024)
    {
        if (!DriverSources.DownloadAllowed(url) || maxBytes is < 1 or > 1024L * 1024 * 1024) throw new InvalidDataException("Only a listed official HTTPS download host is permitted.");
        emptyDirectory = Path.GetFullPath(emptyDirectory);
        PackageFiles.NormalPath(emptyDirectory);
        if (!Directory.Exists(emptyDirectory) || Directory.EnumerateFileSystemEntries(emptyDirectory).Any()) throw new InvalidDataException("Choose an empty download folder.");
        using var client = new HttpClient(testHandler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(20) };
        Uri current = url; HttpResponseMessage? response = null;
        try
        {
            for (int redirects = 0; ; redirects++)
            {
                if (!DriverSources.DownloadAllowed(current)) throw new InvalidDataException("Redirect left the official download-host list.");
                response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    Uri location = response.Headers.Location ?? throw new InvalidDataException("Redirect location missing.");
                    current = location.IsAbsoluteUri ? location : new Uri(current, location); response.Dispose(); response = null;
                    if (redirects >= 4) throw new InvalidDataException("Too many redirects."); continue;
                }
                response.EnsureSuccessStatusCode(); break;
            }
            long? expected = response.Content.Headers.ContentLength; if (expected > maxBytes) throw new InvalidDataException("Package exceeds download size limit.");
            string extension = Path.GetExtension(current.AbsolutePath).ToLowerInvariant();
            if (extension is not (".zip" or ".cab" or ".exe" or ".msi" or ".inf")) extension = ".bin";
            string target = Path.Combine(emptyDirectory, "driver-package" + extension), temporary = target + ".partial";
            long total = 0;
            try
            {
                using var input = await response.Content.ReadAsStreamAsync(); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[65536]; int read;
                    while ((read = await input.ReadAsync(buffer)) > 0) { total += read; if (total > maxBytes) throw new InvalidDataException("Streaming download limit exceeded."); await output.WriteAsync(buffer.AsMemory(0, read)); hash.AppendData(buffer, 0, read); }
                    output.Flush(true);
                }
                if (total == 0 || expected is long length && total != length) throw new InvalidDataException("Empty or incomplete download.");
                File.Move(temporary, target);
                var result = new DownloadedDriverFile(target, Convert.ToHexString(hash.GetHashAndReset()), total, url.GetLeftPart(UriPartial.Path), current.GetLeftPart(UriPartial.Path));
                JsonFiles.Write(Path.Combine(emptyDirectory, "provenance.json"), result); return result;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { response?.Dispose(); }
    }
}
