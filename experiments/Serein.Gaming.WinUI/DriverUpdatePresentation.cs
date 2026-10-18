using Serein.Core;

namespace Serein_Gaming_WinUI;

internal sealed record UpdateOffer(DriverCandidate Candidate, IReadOnlyList<DeviceRow> Matches, bool Exact)
{
    public IReadOnlyList<string> DeviceNames => Matches.Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    public string DeviceName => Matches.Count == 0 ? UiStrings.Get("Text120") : DeviceNames.Count == 1 ? DeviceNames[0] : UiStrings.Format("Text121", Matches.Count);
    public string Installed => Matches.Count == 0 ? UiStrings.Get("Text122") : UiStrings.Get("Text123") + string.Join(", ", Matches.Select(d => DevicePresentation.Known(d.Version)).Distinct(StringComparer.OrdinalIgnoreCase));
    public string Association => Matches.Count == 0 ? UiStrings.Get("Text124") : Exact ? UiStrings.Format("Text125", Matches.Count) : UiStrings.Get("Text126");
}
internal static class DriverUpdatePresentation
{
    public static IReadOnlyList<DriverSource> ManufacturerSources(DeviceRow row, ComputerModel? computer, StorageDeviceInfo? storage = null) => DriverSources.For(row, computer, storage)
        .Where(s => s.Name != "Microsoft Update Catalog" && !s.Url.IdnHost.Equals("support.microsoft.com", StringComparison.OrdinalIgnoreCase)).ToList();
    public static List<UpdateOffer> Build(IReadOnlyList<DriverCandidate> candidates, IReadOnlyList<DeviceRow> devices) => candidates
        .Where(c => !DriverInstallPolicy.Firmware(c.DeviceClass, c.Title))
        .GroupBy(c => (c.UpdateId.ToUpperInvariant(), c.Revision)).Select(g => g.First())
        .Select(candidate =>
        {
            var exact = devices.Where(d => d.HardwareIds.Contains(candidate.HardwareId, StringComparer.OrdinalIgnoreCase)).ToList();
            IReadOnlyList<DeviceRow> matches = exact.Count > 0 ? exact : devices.Where(d => d.CompatibleIds.Contains(candidate.HardwareId, StringComparer.OrdinalIgnoreCase)).ToList();
            return new UpdateOffer(candidate, matches, exact.Count > 0);
        }).OrderBy(o => o.Matches.Count == 0).ThenBy(o => o.DeviceName).ToList();
}
