using System.IO.Pipes;
using System.Security.Principal;
using Serein.Core;

bool probe = args.FirstOrDefault() == "--probe"; if (probe) args = args[1..];
if (args.Length != 6 || args[0] != "--pipe" || args[2] != "--owner" || args[4] != "--born" || !int.TryParse(args[3], out int ownerPid) || !long.TryParse(args[5], out long born)) return 2;
string pipeName = args[1];
if (!pipeName.StartsWith("serein-admin-", StringComparison.Ordinal) || pipeName.Length != 45 || !pipeName[13..].All(Uri.IsHexDigit)) return 2;
try
{
    var owner = Native.Identity(ownerPid); if (owner.CreatedFileTime != born) return 2;
    using (var handle = Native.OpenProcess(Native.Query, false, ownerPid)) if (Native.Owner(handle) != WindowsIdentity.GetCurrent().User?.Value) return 2;
    if (!probe && !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return 3;
    using var pipe = SameUserPipe.OpenClient(pipeName);
    using var connect = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await pipe.ConnectAsync(connect.Token);
    if (!SameUserPipe.ServerMatches(pipe, owner)) return 3;
    using var read = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var request = await Wire.Read<DriverInstallRequest>(pipe, read.Token);
    DriverInstallReply reply;
    if (probe) reply = new(request.Action == "Probe" && request.Update is null && request.Package is null && !request.Confirmed, "Probe", "Probe mode never performs installation or UAC.");
    else
    {
        DriverInstaller.Validate(request);
        var installer = new DriverInstaller(new NativeDriverInstallPlatform(), () => new WindowsUpdateDrivers());
        reply = installer.Execute(request, ProtectedDriverStorage.CreateOperation(), () => Native.Matches(owner));
    }
    using var write = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await Wire.Write(pipe, reply, write.Token); return reply.Success ? 0 : 1;
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
