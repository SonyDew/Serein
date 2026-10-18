using System.Runtime.InteropServices;

namespace Serein.Core;
public record ServiceDetails(string[] Dependencies, string[] Dependents, string BinaryPath, string Account, uint StartType);
public static class ServiceInspector
{
    [StructLayout(LayoutKind.Sequential)] private struct Config
    {
        public uint Type, Start, Error; public nint Binary, Group; public uint Tag; public nint Dependencies, Account, Display;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Status { public uint Type, State, Accepted, Exit, SpecificExit, Checkpoint, Wait; }
    [StructLayout(LayoutKind.Sequential)] private struct Entry { public nint Name, Display; public Status Status; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenService(nint scm, string name, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryServiceConfig(nint service, nint config, uint size, out uint required);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool EnumDependentServices(nint service, uint state, nint buffer, uint size, out uint required, out uint count);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(nint handle);
    public static ServiceDetails Read(string name)
    {
        if (name.Length is < 1 or > 256 || name.Contains('\0')) throw new InvalidDataException("Invalid service name.");
        nint scm = OpenSCManager(null, null, 1); if (scm == 0) throw Native.Error();
        nint service = 0, buffer = 0, dependentBuffer = 0;
        try
        {
            service = OpenService(scm, name, 9); if (service == 0) throw Native.Error();
            QueryServiceConfig(service, 0, 0, out uint needed);
            if (Marshal.GetLastWin32Error() != 122 || needed > 65536) throw Native.Error();
            buffer = Marshal.AllocHGlobal((int)needed); if (!QueryServiceConfig(service, buffer, needed, out _)) throw Native.Error();
            var config = Marshal.PtrToStructure<Config>(buffer); var dependencies = new List<string>();
            nint ptr = config.Dependencies;
            if (ptr != 0) while (Marshal.PtrToStringUni(ptr) is string text && text.Length > 0) { dependencies.Add(text); ptr += (text.Length + 1) * 2; }
            var dependents = new List<string>();
            if (!EnumDependentServices(service, 3, 0, 0, out uint dependentSize, out _))
            {
                if (Marshal.GetLastWin32Error() != 234 || dependentSize > 1024 * 1024) throw Native.Error();
                dependentBuffer = Marshal.AllocHGlobal((int)dependentSize);
                if (!EnumDependentServices(service, 3, dependentBuffer, dependentSize, out _, out uint count)) throw Native.Error();
                for (int i = 0; i < count; i++) dependents.Add(Marshal.PtrToStringUni(Marshal.PtrToStructure<Entry>(dependentBuffer + i * Marshal.SizeOf<Entry>()).Name) ?? "");
            }
            return new(dependencies.ToArray(), dependents.ToArray(), Marshal.PtrToStringUni(config.Binary) ?? "", Marshal.PtrToStringUni(config.Account) ?? "", config.Start);
        }
        finally { if (buffer != 0) Marshal.FreeHGlobal(buffer); if (dependentBuffer != 0) Marshal.FreeHGlobal(dependentBuffer); if (service != 0) CloseServiceHandle(service); CloseServiceHandle(scm); }
    }
}
