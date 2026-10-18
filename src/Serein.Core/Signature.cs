using System.Runtime.InteropServices;

namespace Serein.Core;
public record SignatureInfo(string Status, string Publisher, string Detail);
public static class SignatureVerifier
{
    [StructLayout(LayoutKind.Sequential)] private struct FileInfo { public uint Size; public nint Path, Handle, Subject; }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData
    {
        public uint Size; public nint Policy, Callback; public uint UI, Revocation, Union; public nint File; public uint StateAction; public nint StateData, Url; public uint Flags, Context; public nint Signature;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(nint hwnd, ref Guid action, ref TrustData data);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern nint WTHelperProvDataFromStateData(nint state);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern nint WTHelperGetProvSignerFromChain(nint provider, uint signerIndex, bool counterSigner, uint counterIndex);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode)] private static extern uint CertGetNameString(nint context, uint type, uint flags, nint parameter, System.Text.StringBuilder name, uint length);
    [StructLayout(LayoutKind.Sequential)] private struct Signer
    {
        public uint Size, TimeLow, TimeHigh, CertificateCount; public nint Chain; public uint Type; public nint Info; public uint Error, CounterCount; public nint Counters, ChainContext;
    }
    public static SignatureInfo Read(string path)
    {
        nint filename = Marshal.StringToHGlobalUni(System.IO.Path.GetFullPath(path)), fileData = 0;
        try
        {
            var file = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Path = filename };
            fileData = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>()); Marshal.StructureToPtr(file, fileData, false);
            var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UI = 2, Union = 1, File = fileData, StateAction = 1, Flags = 0x1000 };
            var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            int result;
            string publisher = "Unavailable";
            try
            {
                result = WinVerifyTrust(-1, ref action, ref data);
                var provider = WTHelperProvDataFromStateData(data.StateData);
                var signerPointer = provider == 0 ? 0 : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
                if (signerPointer != 0)
                {
                    var signer = Marshal.PtrToStructure<Signer>(signerPointer);
                    if (signer.Size == Marshal.SizeOf<Signer>() && signer.CertificateCount > 0 && signer.Chain != 0)
                    {
                        // CRYPT_PROVIDER_CERT starts with DWORD cbStruct then aligned CERT_CONTEXT* on x64.
                        var certificate = Marshal.ReadIntPtr(signer.Chain, 8);
                        var name = new System.Text.StringBuilder(4096);
                        nint organizationOid = Marshal.StringToHGlobalAnsi("2.5.4.10");
                        try
                        {
                            if (certificate != 0 && (CertGetNameString(certificate, 3, 0, organizationOid, name, (uint)name.Capacity) > 1 || CertGetNameString(certificate, 4, 0, 0, name, (uint)name.Capacity) > 1)) publisher = name.ToString();
                        }
                        finally { Marshal.FreeHGlobal(organizationOid); }
                    }
                }
            }
            finally { data.StateAction = 2; WinVerifyTrust(-1, ref action, ref data); }
            return new(result == 0 ? "Verified (offline cache policy)" : $"Not verified (0x{result:X8})", publisher, "WinVerifyTrust file Authenticode. Offline cache only; no fresh revocation check or separate catalog lookup. Not verified does not mean malicious or necessarily unsigned. Certificate metadata alone does not establish compatibility.");
        }
        finally { if (fileData != 0) Marshal.FreeHGlobal(fileData); Marshal.FreeHGlobal(filename); }
    }
}
