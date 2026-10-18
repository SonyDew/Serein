using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Serein.Core;
public record DriverStepResult(bool Success, string Phase, int ResultCode, int HResult, bool RebootRequired = false);
public static class DriverInstallPolicy
{
    public static bool Firmware(string deviceClass, string title) => deviceClass.Equals("Firmware", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(title, @"\b(firmware|BIOS|UEFI)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static void ValidateCandidate(DriverCandidate candidate)
    {
        if (!Guid.TryParseExact(candidate.UpdateId, "D", out _) || candidate.Revision < 1 || candidate.Title.Length is < 1 or > 4096 || candidate.HardwareId.Length is < 1 or > 4096 || candidate.Eula.Length > 131072 || Firmware(candidate.DeviceClass, candidate.Title)) throw new InvalidDataException("Invalid driver identity, oversized metadata, or firmware/BIOS package (unsupported).");
    }
    public static void SameSelection(DriverCandidate expected, DriverCandidate actual)
    {
        ValidateCandidate(expected); ValidateCandidate(actual);
        if (expected.Binding != actual.Binding) throw new InvalidOperationException("Update revision, source, driver metadata or EULA changed. Search and review again.");
    }
    public static void CanInstall(bool busy, bool rebootFirst, bool eulaAccepted, bool explicitAcceptance)
    {
        if (busy) throw new InvalidOperationException("Windows Update is busy. Finish the other installation first.");
        if (rebootFirst) throw new InvalidOperationException("Windows requires a restart before this update. No restart was triggered.");
        if (!eulaAccepted && !explicitAcceptance) throw new InvalidOperationException("Explicit EULA acceptance required.");
    }
    public static DriverStepResult Result(string phase, int overall, int perUpdate, int hresult, bool reboot = false) => new(overall == 2 && perUpdate == 2 && hresult >= 0, phase, perUpdate, hresult, reboot);
}
public interface IDriverUpdateSource : IDisposable
{
    DriverCandidate Resolve(DriverCandidate selected);
    DriverStepResult Download(bool acceptEula);
    DriverStepResult Install();
}
public sealed class WindowsUpdateDrivers : IDriverUpdateSource
{
    private readonly List<object> objects = [];
    private object? session, selected, collection, installer;
    private T Keep<T>(T value) where T : class { objects.Add(value); return value; }
    private dynamic Session => session ??= Keep(Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session") ?? throw new NotSupportedException("Windows Update Agent unavailable."))!);
    private DriverCandidate Read(object item, string service, IReadOnlyList<DeviceRow> devices)
    {
        dynamic u = item; object identity = u.Identity;
        try
        {
            string hardware = u.DriverHardwareID;
            string match = devices.Select(d => DriverRecommendations.Match(d, hardware)).FirstOrDefault(m => !m.StartsWith("Unmatched", StringComparison.Ordinal)) ?? "Windows assignment; local association unknown";
            string eula = u.EulaText;
            if (eula.Length > 131072) throw new InvalidDataException("EULA exceeds preview limit; use Windows Update UI.");
            return new((string)u.Title, hardware, (string)u.DriverProvider, Convert.ToString(u.DriverVerDate, CultureInfo.InvariantCulture) ?? "", (string)((dynamic)identity).UpdateID, match, (int)((dynamic)identity).RevisionNumber, (string)u.DriverClass, eula, (bool)u.EulaAccepted, service);
        }
        finally { Wmi.Release(identity); }
    }
    private (object Updates, string Service) Find(string criteria)
    {
        Session.ClientApplicationID = "Serein explicit driver workflow";
        dynamic searcher = Keep((object)Session.CreateUpdateSearcher());
        string service = "ServerSelection=" + (int)searcher.ServerSelection + ";ServiceID=" + (string)searcher.ServiceID;
        dynamic result = Keep((object)searcher.Search(criteria));
        if ((int)result.ResultCode != 2) throw new InvalidOperationException($"Incomplete Windows Update search: {(int)result.ResultCode}.");
        return (Keep((object)result.Updates), service);
    }
    public List<DriverCandidate> Search(IReadOnlyList<DeviceRow> devices)
    {
        var found = Find("IsInstalled=0 and IsHidden=0 and Type='Driver'"); dynamic updates = found.Updates;
        int count = (int)updates.Count; if (count > 256) throw new InvalidDataException("More than 256 assigned candidates; use Windows Update UI.");
        var result = new List<DriverCandidate>();
        for (int i = 0; i < count; i++) { object item = updates.Item(i); try { result.Add(Read(item, found.Service, devices)); } finally { Wmi.Release(item); } }
        return result;
    }
    public DriverCandidate Resolve(DriverCandidate candidate)
    {
        DriverInstallPolicy.ValidateCandidate(candidate);
        var found = Find($"IsInstalled=0 and IsHidden=0 and Type='Driver' and UpdateID='{candidate.UpdateId}' and RevisionNumber={candidate.Revision}"); dynamic updates = found.Updates;
        if ((int)updates.Count != 1) throw new InvalidOperationException("Selected update is no longer uniquely applicable. Search again.");
        selected = Keep((object)updates.Item(0));
        var actual = Read(selected, found.Service, []); DriverInstallPolicy.SameSelection(candidate, actual);
        if ((bool)((dynamic)selected).IsHidden || (bool)((dynamic)selected).IsInstalled || (int)((dynamic)selected).Type != 2) throw new InvalidOperationException("Not an applicable driver update.");
        collection = Keep(Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl") ?? throw new NotSupportedException("WUA collection unavailable."))!);
        ((dynamic)collection).Add((dynamic)selected);
        installer = Keep((object)Session.CreateUpdateInstaller()); ((dynamic)installer).Updates = (dynamic)collection;
        DriverInstallPolicy.CanInstall((bool)((dynamic)installer).IsBusy, (bool)((dynamic)installer).RebootRequiredBeforeInstallation, true, true);
        return actual;
    }
    public DriverStepResult Download(bool acceptEula)
    {
        if (selected is null || collection is null || installer is null) throw new InvalidOperationException("Resolve a driver selection first.");
        dynamic u = selected;
        DriverInstallPolicy.CanInstall((bool)((dynamic)installer).IsBusy, (bool)((dynamic)installer).RebootRequiredBeforeInstallation, (bool)u.EulaAccepted, acceptEula);
        if (!(bool)u.EulaAccepted) u.AcceptEula();
        dynamic downloader = Keep((object)Session.CreateUpdateDownloader()); downloader.Updates = (dynamic)collection;
        dynamic result = Keep((object)downloader.Download()); dynamic perUpdate = Keep((object)result.GetUpdateResult(0));
        return DriverInstallPolicy.Result("Download", (int)result.ResultCode, (int)perUpdate.ResultCode, (int)perUpdate.HResult);
    }
    public DriverStepResult Install()
    {
        if (installer is null || selected is null || !(bool)((dynamic)selected).IsDownloaded) throw new InvalidOperationException("Verified download is not ready.");
        dynamic target = installer;
        DriverInstallPolicy.CanInstall((bool)target.IsBusy, (bool)target.RebootRequiredBeforeInstallation, (bool)((dynamic)selected).EulaAccepted, false);
        target.AllowSourcePrompts = false; target.ForceQuiet = true;
        dynamic result = Keep((object)target.Install()); dynamic perUpdate = Keep((object)result.GetUpdateResult(0));
        return DriverInstallPolicy.Result("Install", (int)result.ResultCode, (int)perUpdate.ResultCode, (int)perUpdate.HResult, (bool)result.RebootRequired);
    }
    public void Dispose() { foreach (var item in objects.AsEnumerable().Reverse()) Wmi.Release(item); objects.Clear(); }
}
