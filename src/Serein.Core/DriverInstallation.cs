using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Serein.Core;
public record DriverInstallRequest(string Action, bool Confirmed = false, bool AcceptEula = false, DriverCandidate? Update = null, InfPackage? Package = null);
public record DriverInstallReply(bool Success, string State, string Message, bool RebootRequired = false, string? ReportPath = null);
public interface IDriverInstallPlatform
{
    void Backup(string directory);
    DriverStepResult InstallInf(string inf);
    InfPackage Inspect(string inf);
    IReadOnlyList<DeviceRow> Devices() => [];
}
public sealed class NativeDriverInstallPlatform : IDriverInstallPlatform
{
    public static ProcessStartInfo Command(string action, string argument, string? destination = null)
    {
        if (action is not ("/export-driver" or "/add-driver")) throw new InvalidDataException("Unsupported PnPUtil operation.");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "pnputil.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(action); start.ArgumentList.Add(argument);
        if (action == "/export-driver") { if (argument != "*" || destination is null) throw new InvalidDataException("Fixed backup operation required."); start.ArgumentList.Add(destination); }
        else { if (destination is not null || !Path.IsPathFullyQualified(argument) || !argument.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) || argument.IndexOfAny(['*', '?']) >= 0) throw new InvalidDataException("Single absolute INF required."); start.ArgumentList.Add("/install"); }
        return start;
    }
    private static async Task<int> Run(ProcessStartInfo command, string log)
    {
        using var process = Process.Start(command) ?? throw new InvalidOperationException("PnPUtil launch failed.");
        async Task<string> Read(StreamReader reader)
        {
            var text = new System.Text.StringBuilder(); var buffer = new char[4096]; int read;
            while ((read = await reader.ReadAsync(buffer)) > 0) { if (text.Length < 262144) text.Append(buffer, 0, Math.Min(read, 262144 - text.Length)); }
            return text.ToString();
        }
        var stdout = Read(process.StandardOutput); var stderr = Read(process.StandardError);
        // Never kill a device installation or reboot the host on a timeout.
        await process.WaitForExitAsync(); File.WriteAllText(log, await stdout + "\n" + await stderr); return process.ExitCode;
    }
    public void Backup(string directory)
    {
        Directory.CreateDirectory(directory); int code = Run(Command("/export-driver", "*", directory), Path.Combine(directory, "pnputil-export.log")).GetAwaiter().GetResult();
        if (code != 0 || !Directory.EnumerateFiles(directory, "*.inf", SearchOption.AllDirectories).Any()) throw new InvalidOperationException($"Driver export could not be confirmed (exit {code}). Installation refused; partial backup retained.");
    }
    public DriverStepResult InstallInf(string inf)
    {
        int code = Run(Command("/add-driver", inf), Path.Combine(Path.GetDirectoryName(inf)!, "pnputil-install.log")).GetAwaiter().GetResult();
        return new(code is 0 or 3010, "PnPUtil", code, code, code == 3010);
    }
    public InfPackage Inspect(string inf) => InfPackages.Inspect(inf, DeviceInventory.Read());
    public IReadOnlyList<DeviceRow> Devices() => DeviceInventory.Read();
}
public sealed class DriverInstaller(IDriverInstallPlatform platform, Func<IDriverUpdateSource> sourceFactory)
{
    public static void Validate(DriverInstallRequest request)
    {
        if (System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request, JsonFiles.Options).Length > 1024 * 1024) throw new InvalidDataException("Driver request exceeds the IPC bound.");
        if (!request.Confirmed || request.Action is not ("InstallWindowsUpdate" or "InstallInf")) throw new InvalidDataException("Explicit install confirmation and a supported typed action are required.");
        if (request.Action == "InstallWindowsUpdate")
        { if (request.Update is null || request.Package is not null) throw new InvalidDataException("Exactly one WUA selection required."); DriverInstallPolicy.ValidateCandidate(request.Update); }
        else
        {
            var package = request.Package ?? throw new InvalidDataException("INF package required.");
            if (request.Update is not null || package.Matches.Count is < 1 or > 512 || !package.Inf.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid INF selection.");
            PackageFiles.Relative(package.Inf); InfPackages.AllowedClass(package.DeviceClass);
        }
    }
    public DriverInstallReply Execute(DriverInstallRequest request, string operationDirectory, Func<bool> ownerAlive)
    {
        Validate(request); string report = Path.Combine(operationDirectory, "result.json");
        Directory.CreateDirectory(operationDirectory);
        var history = new List<object>();
        void State(string state, object? detail = null)
        {
            history.Add(new { State = state, AtUtc = DateTimeOffset.UtcNow, Detail = detail });
            JsonFiles.Write(report, new { SchemaVersion = 1, State = state, Action = request.Action, Update = request.Update?.UpdateId, Revision = request.Update?.Revision, Provider = request.Update?.Provider, HardwareId = request.Update?.HardwareId, Source = request.Update?.Service, Inf = request.Package?.Inf, PackageBinding = request.Package?.Binding, History = history });
        }
        void Owner() { if (!ownerAlive()) throw new InvalidOperationException("Controller exited before installation commit. No new step started."); }
        State("Preparing");
        try
        {
            using var source = request.Action == "InstallWindowsUpdate" ? sourceFactory() : null;
            InfPackage? staged = null;
            if (source is not null)
            {
                var current = source.Resolve(request.Update!); DriverInstallPolicy.SameSelection(request.Update!, current);
                DriverInstallPolicy.CanInstall(false, false, current.EulaAccepted, request.AcceptEula);
            }
            else
            {
                string payload = Path.Combine(operationDirectory, "payload"); Directory.CreateDirectory(payload);
                string inf = PackageFiles.CopyVerified(request.Package!, payload);
                staged = platform.Inspect(inf);
                if (staged.Binding != request.Package!.Binding) throw new InvalidOperationException("Sealed package compatibility/signature changed. Review again.");
            }
            Owner();
            var before = platform.Devices().Where(d => request.Package is not null ? request.Package.Matches.Any(m => m.InstanceId == d.InstanceId) : d.HardwareIds.Concat(d.CompatibleIds).Contains(request.Update!.HardwareId, StringComparer.OrdinalIgnoreCase)).Select(d => new { d.Name, d.Class, d.Provider, d.Version, d.Inf }).ToList();
            State("BackingUp", new { Before = before, Note = "Backup covers third-party INF packages, not firmware or a guaranteed rollback." }); platform.Backup(Path.Combine(operationDirectory, "backup"));
            Owner();
            if (source is not null)
            {
                State("Downloading"); var download = source.Download(request.AcceptEula); State("DownloadResult", download);
                if (!download.Success) throw new InvalidOperationException($"Download failed/partial: {download.ResultCode}, HRESULT 0x{download.HResult:X8}.");
            }
            Owner(); State("Installing", new { Recovery = "Not automatically reversible. Do not interrupt or assume backup guarantees rollback." });
            var installed = source is not null ? source.Install() : platform.InstallInf(Path.Combine(operationDirectory, "payload", staged!.Inf));
            var reply = new DriverInstallReply(installed.Success, installed.Success ? source is null ? "StagedAndWindowsSelectionCompleted" : "Installed" : "FailedOrPartial", source is null ? "Package staged and Windows driver selection requested. A better-ranked installed driver may remain; rescan devices. See PnPUtil log." : $"WUA result {installed.ResultCode}; HRESULT 0x{installed.HResult:X8}. No automatic restart.", installed.RebootRequired, report);
            State(reply.State, new { Reply = reply, Result = installed }); return reply;
        }
        catch (Exception ex)
        {
            State("FailedOrUnknown", new { Error = ex.Message, Note = "If the native install had started, partial effects may remain. No automatic uninstall or reboot." });
            return new(false, "FailedOrUnknown", ex.Message, false, report);
        }
    }
}
public static class ProtectedDriverStorage
{
    public static string CreateOperation()
    {
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException("Driver installation requires UAC elevation.");
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Serein-DriverOperations");
        PackageFiles.NormalPath(root);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null); var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null); var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        DirectorySecurity security = new(); security.SetAccessRuleProtection(true, false); security.SetOwner(admins);
        foreach (var sid in new[] { admins, system }) security.AddAccessRule(new(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new(users, FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(root).Create(security); new DirectoryInfo(root).SetAccessControl(security); PackageFiles.NormalPath(root);
        string directory = Path.Combine(root, Guid.NewGuid().ToString("N")); new DirectoryInfo(directory).Create(security); return directory;
    }
}
