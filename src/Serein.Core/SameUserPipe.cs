using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Serein.Core;
public static class SameUserPipe
{
    // .NET10 CurrentUserOnly compares token.Owner (Administrators after UAC)
    // instead of token.User. Use an explicit user-only DACL across integrity levels.
    public static NamedPipeServerStream CreateServer(string name)
    {
        using var identity = WindowsIdentity.GetCurrent(); var sid = identity.User ?? throw new UnauthorizedAccessException("No current user SID.");
        var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false); security.SetOwner(sid);
        security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, security, HandleInheritability.None);
    }
    public static NamedPipeClientStream OpenClient(string name) => new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification, HandleInheritability.None);
    public static bool ServerMatches(NamedPipeClientStream pipe, ProcessIdentity expected)
    {
        if (!DriverAdminClient.Peer(pipe.SafePipeHandle, expected.Pid, false) || !Native.Matches(expected)) return false;
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Native.OpenProcess(Native.Query, false, expected.Pid);
        return Native.Owner(process) == identity.User?.Value && pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) == identity.User;
    }
}
