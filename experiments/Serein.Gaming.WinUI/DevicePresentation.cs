using Serein.Core;

namespace Serein_Gaming_WinUI;

internal enum DeviceCategory { Graphics, Network, Audio, Storage, Bluetooth, Input, Monitor, Camera, Printer, Usb, Processor, System, Other }
internal sealed record ProcessorInfo(string Name, string Socket, int? Cores, int? Threads);
internal sealed record DeviceContext(IReadOnlyList<ProcessorInfo> Processors, ComputerModel? Computer, IReadOnlySet<string> VirtualAdapters, IReadOnlyList<string> Warnings)
{
    public static DeviceContext Empty { get; } = new([], null, new HashSet<string>(StringComparer.OrdinalIgnoreCase), []);
    public IReadOnlyList<StorageDeviceInfo> Storage { get; init; } = [];
}
internal sealed record DeviceComponent(string Key, string Name, DeviceCategory Category, bool Secondary, bool ProcessorGroup, IReadOnlyList<DeviceRow> Members, string Summary = "")
{
    public RecognizedDevice? Recognition { get; init; }
    // Model-level processor summaries are never an installation target.
    public DeviceRow? Target => !ProcessorGroup && Members.Count == 1 ? Members[0] : null;
    public bool NeedsAttention => Members.Any(d => d.ProblemCode is > 0);
    public string CategoryName => DevicePresentation.CategoryName(Category);
    public string Glyph => Category switch { DeviceCategory.Graphics => "\uE7F4", DeviceCategory.Network => "\uE701", DeviceCategory.Audio => "\uE767", DeviceCategory.Storage => "\uEDA2", DeviceCategory.Bluetooth => "\uE702", DeviceCategory.Input => "\uE765", DeviceCategory.Monitor => "\uE7F4", DeviceCategory.Camera => "\uE722", DeviceCategory.Printer => "\uE749", DeviceCategory.Processor => "\uE950", _ => "\uE713" };
    public string Status => Members.Any(d => d.ProblemCode == 28) ? UiStrings.Get("Text046") : NeedsAttention ? UiStrings.Get("Text047") : Members.All(d => d.ProblemCode == 0) ? UiStrings.Get("Text048") : UiStrings.Get("Text049");
    public string Caption => ProcessorGroup ? $"{CategoryName} · {Summary}" : Recognition?.Storage is { SizeBytes: > 0 } disk ? UiStrings.Format("Text050", CategoryName, disk.SizeBytes / 1_000_000_000d) : Recognition?.Nature == DeviceNature.Software ? UiStrings.Format("Text051", CategoryName) : CategoryName;
    public override string ToString() => $"{Name}. {Caption}. {Status}";
    public string Version => ProcessorGroup && Members.Select(d => d.Version).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1 ? UiStrings.Get("Text052") : DevicePresentation.Known(Members[0].Version);
    public bool Matches(string query) => string.IsNullOrWhiteSpace(query) || $"{Name} {CategoryName} {Status} {Recognition?.Vendor}".Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
        || Members.Any(d => $"{d.Name} {d.InstanceId} {d.Manufacturer} {d.Provider} {d.Inf} {d.Version} {string.Join(' ', d.HardwareIds)} {string.Join(' ', d.CompatibleIds)}".Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
}
internal static class DevicePresentation
{
    public static string Known(string? value) => string.IsNullOrWhiteSpace(value) || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? UiStrings.Get("Text053") : value.Trim();
    public static string CategoryName(DeviceCategory category) => category switch
    {
        DeviceCategory.Graphics => UiStrings.Get("Text054"), DeviceCategory.Network => UiStrings.Get("Text055"), DeviceCategory.Audio => UiStrings.Get("Text056"), DeviceCategory.Storage => UiStrings.Get("Text057"),
        DeviceCategory.Bluetooth => "Bluetooth", DeviceCategory.Input => UiStrings.Get("Text058"), DeviceCategory.Monitor => UiStrings.Get("Text059"), DeviceCategory.Camera => UiStrings.Get("Text060"),
        DeviceCategory.Printer => UiStrings.Get("Text061"), DeviceCategory.Usb => "USB", DeviceCategory.Processor => UiStrings.Get("Text026"), DeviceCategory.System => UiStrings.Get("Text062"), _ => UiStrings.Get("Text063")
    };
    public static DeviceCategory Classify(string deviceClass) => deviceClass.ToUpperInvariant() switch
    {
        "DISPLAY" => DeviceCategory.Graphics, "NET" => DeviceCategory.Network, "MEDIA" or "AUDIOENDPOINT" => DeviceCategory.Audio,
        "DISKDRIVE" or "HDC" or "SCSIADAPTER" or "VOLUME" or "VOLUMESNAPSHOT" => DeviceCategory.Storage,
        "BLUETOOTH" => DeviceCategory.Bluetooth, "KEYBOARD" or "MOUSE" or "HIDCLASS" => DeviceCategory.Input, "MONITOR" => DeviceCategory.Monitor,
        "CAMERA" or "IMAGE" => DeviceCategory.Camera, "PRINTER" or "PRINTQUEUE" => DeviceCategory.Printer, "USB" => DeviceCategory.Usb,
        "PROCESSOR" => DeviceCategory.Processor, "SYSTEM" or "COMPUTER" or "SOFTWAREDEVICE" or "SOFTWARECOMPONENT" or "FIRMWARE" or "SECURITYDEVICES" => DeviceCategory.System,
        _ => DeviceCategory.Other
    };
    public static List<DeviceComponent> Build(IReadOnlyList<DeviceRow> rows, DeviceContext context)
    {
        var result = new List<DeviceComponent>();
        foreach (var group in rows.Where(d => Classify(d.Class) == DeviceCategory.Processor).GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase))
            result.Add(new("processor-model:" + group.Key, group.Key, DeviceCategory.Processor, false, true, group.ToList(), ProcessorSummary(group.Key, context)));
        foreach (var row in rows.Where(d => Classify(d.Class) != DeviceCategory.Processor))
        {
            var disks = context.Storage.Where(d => d.InstanceId.Equals(row.InstanceId, StringComparison.OrdinalIgnoreCase)).ToList();
            var recognized = DeviceRecognition.Resolve(row, disks.Count == 1 ? disks[0] : null);
            if (context.VirtualAdapters.Contains(row.InstanceId)) recognized = recognized with { Vendor = null, Nature = DeviceNature.Software, Evidence = UiStrings.Get("Text064") };
            bool secondary = Classify(row.Class) == DeviceCategory.System || new[] { "AudioEndpoint", "Volume", "VolumeSnapshot", "HIDClass", "USB", "PrintQueue" }.Contains(row.Class, StringComparer.OrdinalIgnoreCase)
                || recognized.Nature is DeviceNature.Software or DeviceNature.WindowsComponent;
            result.Add(new(row.InstanceId, Known(recognized.Name), Classify(row.Class), secondary, false, [row]) { Recognition = recognized });
        }
        return result.OrderByDescending(d => d.NeedsAttention).ThenBy(d => d.Category).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    public static IEnumerable<DeviceComponent> Filter(IEnumerable<DeviceComponent> components, string query, DeviceCategory? category, int scope) =>
        components.Where(d => (scope == 2 || scope == 1 && d.NeedsAttention || scope == 0 && (!d.Secondary || d.NeedsAttention)) && (category is null || d.Category == category) && d.Matches(query));
    public static string ProcessorSummary(string name, DeviceContext context)
    {
        var packages = context.Processors.Where(p => p.Name.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (packages.Count == 0) return UiStrings.Get("Text065");
        var pieces = new List<string>();
        if (packages.Count > 1) pieces.Add(UiStrings.Format("Text066", packages.Count));
        if (packages.All(p => p.Cores is > 0)) pieces.Add(UiStrings.Format("Text067", packages.Sum(p => p.Cores)));
        if (packages.All(p => p.Threads is > 0)) pieces.Add(UiStrings.Format("Text068", packages.Sum(p => p.Threads)));
        return pieces.Count == 0 ? UiStrings.Get("Text065") : string.Join(" · ", pieces);
    }
}
