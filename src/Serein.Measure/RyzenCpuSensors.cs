using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32;
using Serein.Core;

namespace Serein.Measure;

// AMD Monitoring SDK 3.1.1.5478 public x64 IPlatform/IDeviceManager/ICPUEx ABI.
// No driver installer, SCM mutation, BIOS interface, setters, or tuning exports.
// The optional vendor binaries are never distributed with Serein.
internal sealed class RyzenCpuSensors : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetPlatform();
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate byte Init(nint self, nint vendor, byte cpuOnly);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate byte UnInit(nint self);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate nint GetManager(nint self);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate nint GetDevice(nint self, int type, uint index);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate nint GetName(nint self);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int GetParameters(nint self, nint parameters);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadLibraryExW(string file, nint reserved, uint flags);
    private nint library, platform, cpu, deviceLibrary;
    private bool initialized;
    private readonly Stopwatch interval = new();
    private CpuSnapshot? last;
    public RyzenCpuSensors(string? explicitDirectory = null)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AMD\RyzenMasterMonitoringSDK");
            string? root = explicitDirectory ?? key?.GetValue("InstallationPath") as string;
            if (string.IsNullOrWhiteSpace(root)) throw new CpuSensorException(CpuSensorStatus.SdkNotInstalled, "AMD Ryzen Master Monitoring SDK is not installed.");
            // Must already be running. Never start/install/reconfigure a driver to obtain sensors.
            if (!Wmi.Query("SELECT State FROM Win32_SystemDriver WHERE Name='AMDRyzenMasterDriverV32'").Any(r => r.Text("State") == "Running"))
                throw new CpuSensorException(CpuSensorStatus.DriverNotRunning, "The installed AMD V32 monitoring driver is not running.");
            string bin = Path.Combine(Path.GetFullPath(root), "bin");
            foreach (string name in new[] { "Platform.dll", "Device.dll" })
            {
                string file = Path.Combine(bin, name);
                if (!File.Exists(file)) throw new CpuSensorException(CpuSensorStatus.SdkNotInstalled, "Missing official SDK component: " + name);
                string expected = name == "Platform.dll" ? "38FB8D5D2A815DD0AD7903E5CF5C016C992F0A3BF9E2BE718B972D6BF4669C88" : "8DB02788D268AAEC250B332BF343AF49CF6562AC101F1FC2F170F0BBB81BB497";
                using var bytes = File.OpenRead(file);
                if (FileVersionInfo.GetVersionInfo(file).FileVersion != "3.1.1.5478" || Convert.ToHexString(SHA256.HashData(bytes)) != expected) throw new CpuSensorException(CpuSensorStatus.UnsupportedVersion, "Only the reviewed public SDK 3.1.1.5478 binaries are supported.");
                var signature = SignatureVerifier.Read(file);
                if (!signature.Status.StartsWith("Verified", StringComparison.Ordinal) || !signature.Publisher.StartsWith("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unverified AMD SDK component.");
            }
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                throw new CpuSensorException(CpuSensorStatus.AccessRequired, "AMD Monitoring SDK requires administrator access. Connect the read-only CPU helper explicitly.");
            // Preload the verified direct SDK dependency from its exact location.
            // Static dependencies search only that DLL directory and System32.
            deviceLibrary = LoadLibraryExW(Path.Combine(bin, "Device.dll"), 0, 0x100 | 0x800);
            if (deviceLibrary == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            library = LoadLibraryExW(Path.Combine(bin, "Platform.dll"), 0, 0x100 | 0x800);
            if (library == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            platform = Marshal.GetDelegateForFunctionPointer<GetPlatform>(NativeLibrary.GetExport(library, "GetPlatform"))();
            if (Method<Init>(platform, 0)(platform, 0, 1) == 0) throw new InvalidOperationException("AMD SDK initialization failed. The SDK may require administrator access or official installation.");
            initialized = true;
            nint manager = Method<GetManager>(platform, 2)(platform);
            // MSVC groups virtual overloads in reverse declaration order:
            // GetDevice(index) = slot2, GetDevice(type,index) = slot3.
            cpu = Method<GetDevice>(manager, 3)(manager, 0, 0);
            if (cpu == 0) throw new NotSupportedException("No supported Ryzen processor was returned.");
        }
        catch { Dispose(); throw; }
    }
    public CpuSnapshot Read()
    {
        // AMD requires >= 1 second between reads. UI samples every 5 seconds.
        if (last is not null && interval.Elapsed < TimeSpan.FromSeconds(1)) return last;
        nint data = Marshal.AllocHGlobal(192);
        try
        {
            // CPUParameters sizeof=192, default x64 alignment=8. Returned per-core
            // arrays are owned by the SDK; never dereference/free them.
            Marshal.Copy(new byte[192], 0, data, 192);
            int result = Method<GetParameters>(cpu, 17)(cpu, data);
            interval.Restart();
            if (result != 0) throw new NotSupportedException("GetCPUParameters status " + result);
            double? Number(int offset) { double n = Marshal.PtrToStructure<double>(data + offset); return n == -1 ? null : n; }
            float power = Marshal.PtrToStructure<float>(data + 108);
            string name = (Marshal.PtrToStringUni(Method<GetName>(cpu, 2)(cpu)) ?? "AMD Ryzen").Trim();
            last = new CpuSnapshot(DateTimeOffset.UtcNow, CpuSensorStatus.Available, new CpuReading(name, Number(72), Number(96), power == -1 ? null : power)).Validate();
            return last;
        }
        finally { Marshal.FreeHGlobal(data); }
    }
    private static T Method<T>(nint self, int slot) where T : Delegate
    {
        if (self == 0) throw new InvalidDataException("Null AMD interface.");
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self), slot * nint.Size));
    }
    public void Dispose()
    {
        if (initialized) { initialized = false; Method<UnInit>(platform, 1)(platform); }
        if (library != 0) { NativeLibrary.Free(library); library = 0; }
        if (deviceLibrary != 0) { NativeLibrary.Free(deviceLibrary); deviceLibrary = 0; }
        cpu = platform = 0;
    }
}
internal sealed class CpuSensorException(CpuSensorStatus status, string message) : Exception(message)
{ public CpuSensorStatus Status { get; } = status; }
