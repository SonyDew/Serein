using System.Runtime.InteropServices;
using Serein.Core;

namespace Serein.Measure;

// Read-only bindings to AMD's locked ADLX 1.0 C interfaces (ADLX.h, ISystem.h,
// IPerformanceMonitoring.h). Vtable slots are interface-specific, not COM IUnknown.
// This adapter runs only in the measurement child process, never in the UI/watchdog.
internal sealed class AmdGpuSensors : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize(ulong version, out nint system);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Terminate();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OutPointer(nint self, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GpuPointer(nint self, nint gpu, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int At(nint self, uint index, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint Count(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OutUInt(nint self, out uint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OutInt(nint self, out int result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OutDouble(nint self, out double result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OutBool(nint self, out byte result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Release(nint self);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadLibraryExW(string path, nint file, uint flags);
    private readonly List<(nint Gpu, nint Support, string Name, string Key, double? Capacity)> adapters = [];
    private nint library, services;
    private Terminate? terminate;
    private bool initialized;

    public AmdGpuSensors()
    {
        try
        {
            string path = Path.Combine(Environment.SystemDirectory, "amdadlx64.dll");
            if (!File.Exists(path)) throw new NotSupportedException("AMD ADLX driver library is not installed.");
            var trust = SignatureVerifier.Read(path);
            if (!trust.Status.StartsWith("Verified", StringComparison.Ordinal) || !trust.Publisher.StartsWith("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Installed ADLX library could not be verified as AMD-signed.");
            library = LoadLibraryExW(path, 0, 0x100 | 0x800); // DLL directory + System32 dependencies, no CWD/PATH search.
            if (library == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            terminate = Marshal.GetDelegateForFunctionPointer<Terminate>(NativeLibrary.GetExport(library, "ADLXTerminate"));
            var init = Marshal.GetDelegateForFunctionPointer<Initialize>(NativeLibrary.GetExport(library, "ADLXInitialize"));
            Check(init(1ul << 48, out var system), "ADLXInitialize 1.0"); initialized = true;
            Check(Method<OutPointer>(system, 9)(system, out services), "GetPerformanceMonitoringServices");
            Check(Method<OutPointer>(system, 1)(system, out var list), "GetGPUs");
            try
            {
                uint count = Math.Min(8, Method<Count>(list, 3)(list));
                for (uint i = 0; i < count; i++)
                {
                    Check(Method<At>(list, 11)(list, i, out var gpu), "GPUList.At"); nint support = 0;
                    try
                    {
                        Check(Method<GpuPointer>(services, 21)(services, gpu, out support), "GetSupportedGPUMetrics");
                        string name = String(gpu, 7), key = String(gpu, 9);
                        double? capacity = Method<OutUInt>(gpu, 11)(gpu, out uint total) == 0 && total > 0 ? total : null;
                        adapters.Add((gpu, support, name, key, capacity)); gpu = support = 0;
                    }
                    finally { Drop(support); Drop(gpu); }
                }
            }
            finally { Drop(list); }
        }
        catch { Dispose(); throw; }
    }

    public GpuSnapshot Read()
    {
        var readings = new List<GpuReading>();
        foreach (var adapter in adapters)
        {
            Check(Method<GpuPointer>(services, 18)(services, adapter.Gpu, out var metrics), "GetCurrentGPUMetrics");
            try
            {
                bool Supported(int slot) => Method<OutBool>(adapter.Support, slot)(adapter.Support, out byte value) == 0 && value != 0;
                double? Number(int slot, int supportSlot) => Supported(supportSlot) && Method<OutDouble>(metrics, slot)(metrics, out double value) == 0 ? value : null;
                double? Integer(int slot, int supportSlot) => Supported(supportSlot) && Method<OutInt>(metrics, slot)(metrics, out int value) == 0 ? value : null;
                readings.Add(new GpuReading(adapter.Name, adapter.Key, Number(7, 6), Number(8, 7), Number(4, 3),
                    Integer(12, 11), adapter.Capacity, Number(9, 8), Number(10, 9), Integer(5, 4), Integer(6, 5), Integer(11, 10)).Validate());
            }
            finally { Drop(metrics); }
        }
        return new GpuSnapshot(DateTimeOffset.UtcNow, "AMD ADLX", readings.Count > 0 ? "Available" : "No supported AMD GPUs", readings).Validate();
    }
    private static T Method<T>(nint self, int slot) where T : Delegate
    {
        if (self == 0) throw new InvalidDataException("ADLX returned a null interface.");
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self), slot * nint.Size));
    }
    private static string String(nint self, int slot)
    { Check(Method<OutPointer>(self, slot)(self, out var value), "GPU identity"); return Marshal.PtrToStringUTF8(value) ?? "AMD GPU"; }
    private static void Check(int status, string operation) { if (status != 0) throw new NotSupportedException($"{operation}: ADLX status {status}."); }
    private static void Drop(nint value) { if (value != 0) Method<Release>(value, 1)(value); }
    public void Dispose()
    {
        foreach (var adapter in adapters) { Drop(adapter.Support); Drop(adapter.Gpu); } adapters.Clear();
        Drop(services); services = 0;
        if (initialized) { terminate?.Invoke(); initialized = false; }
        if (library != 0) { NativeLibrary.Free(library); library = 0; }
    }
}
