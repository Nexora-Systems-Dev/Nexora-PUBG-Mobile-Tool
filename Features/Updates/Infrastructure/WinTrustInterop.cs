using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Nexora.Features.Updates.Infrastructure;

/// <summary>
/// The WinTrust file-binding primitive: asks Windows whether a signature
/// covers a file's exact bytes. Moved here verbatim from
/// <see cref="UpdateArchiveValidator"/>; the composition that interprets the
/// answer stays there.
/// </summary>
internal static class WinTrustInterop
{
    /// <summary>
    /// Asks Windows to hash the file's bytes and compare that digest against the
    /// one sealed inside its signature — the binding <see cref="X509Chain.Build"/>
    /// never performs, and the reason a signature can otherwise be lifted off a
    /// genuinely signed file and dropped onto one that was never signed at all.
    /// </summary>
    /// <returns>True only when Windows accepts a signature over this exact file.</returns>
    internal static bool HasValidFileSignature(string filePath)
    {
        // Fail closed before touching native code: a missing file has no
        // signature to verify, and must fall through to the checksum path.
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;

        // Both buffers are blittable copies holding no reference types, so
        // StructureToPtr is a plain byte copy and FreeCoTaskMem frees all of it.
        var pathPtr = Marshal.StringToCoTaskMemUni(filePath);
        var fileInfoPtr = IntPtr.Zero;
        try
        {
            var infoSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            fileInfoPtr = Marshal.AllocCoTaskMem((int)infoSize);
            Marshal.StructureToPtr(
                new WinTrustFileInfo { cbSize = infoSize, pcwszFilePath = pathPtr },
                fileInfoPtr, fDeleteOld: false);

            var data = BuildTrustData(fileInfoPtr);
            // Copied to a local because DllImport needs a writable ref; the GUID
            // itself is constant and the call never mutates it.
            var actionId = VerifyActionGuid;
            // Any non-zero HRESULT is a verification failure (no signature, bad
            // digest, expired or revoked certificate), so it maps to false and
            // the checksum path takes over exactly as an unsigned file would.
            var verified = WinVerifyTrust(IntPtr.Zero, ref actionId, ref data) == S_OK;
            if (data.hWVTStateData != IntPtr.Zero) WintrustDataFree(ref data);
            return verified;
        }
        catch (Win32Exception)
        {
            // Native-side failure (bad handle, provider fault): not a
            // verification success — fall through to the checksum path.
            return false;
        }
        catch (MarshalDirectiveException)
        {
            // Marshalling fault: same fail-closed outcome, never a throw out
            // of a boolean predicate.
            return false;
        }
        finally
        {
            if (fileInfoPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(fileInfoPtr);
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }

    /// <summary>
    /// The verification request itself: no UI, the embedded signature over the
    /// named file, and no revocation flags — today's chain policy already
    /// governs revocation, so this adds nothing to that posture.
    /// </summary>
    private static WinTrustData BuildTrustData(IntPtr fileInfoPtr) => new()
    {
        cbSize = (uint)Marshal.SizeOf<WinTrustData>(),
        dwUIChoice = WinTrustUiNone,
        fdwRevocationChecks = WinTrustRevokeNone,
        dwUnionChoice = WinTrustChoiceFile,
        pFile = fileInfoPtr,
        // IGNORE: this call allocates no state data, so hWVTStateData stays zero
        // and WintrustDataFree has nothing to release. Named explicitly because
        // the field sits between the union and hWVTStateData, where a default
        // value would be easy to misread as an omission.
        dwStateAction = WinTrustStateActionIgnore,
        dwUIContext = WinTrustUiContextExecute
    };

    /// <summary>The sole native entry point; S_OK means the signature binds to the file.</summary>
    [DllImport("wintrust.dll", PreserveSig = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);

    /// <summary>Releases state WinVerifyTrust may allocate, which the caller otherwise leaks.</summary>
    [DllImport("wintrust.dll", PreserveSig = true)]
    private static extern void WintrustDataFree(ref WinTrustData pWVTData);

    /// <summary>WINTRUST_ACTION_GENERIC_VERIFY_V2: the Authenticode policy provider.</summary>
    private static readonly Guid VerifyActionGuid = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WinTrustUiNone = 2;           // WTD_UI_NONE: never prompt the user.
    private const uint WinTrustRevokeNone = 0;       // WTD_REVOKE_NONE: chain policy handles revocation.
    private const uint WinTrustChoiceFile = 1;       // WTD_CHOICE_FILE: verify the embedded signature.
    private const uint WinTrustStateActionIgnore = 0;// WTD_STATEACTION_IGNORE: no state data is allocated.
    private const uint WinTrustUiContextExecute = 0; // WTD_UICONTEXT_EXECUTE.
    private const int S_OK = 0;

    /// <summary>
    /// WINTRUST_FILE_INFO, field-for-field per wintrust.h on x64 (32 bytes):
    /// size, path, file handle, known-subject GUID. The path MUST come second —
    /// a swapped hFile/pcwszFilePath pair hands Windows a null path and a path
    /// pointer as a handle, after which every verification answer is garbage
    /// (this exact defect shipped in F1 and was caught by CI, not by review).
    /// Both handles stay zero: Windows opens the file by path, unconstrained.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbSize;
        public IntPtr pcwszFilePath; // LPWSTR, owned by the caller and freed in the finally.
        public IntPtr hFile;         // Zero: let Windows open the file by path.
        public IntPtr pgKnownSubject;// Zero: no known-subject GUID constraint.
    }

    /// <summary>
    /// WINTRUST_DATA, laid out to match wintrust.h field-for-field on x64 (88 bytes).
    /// Explicit layout for two reasons. First, the C header places the choice union —
    /// pFile/pCatalog/pBlob/pSgnr/pCert — at a single offset: declaring those five as
    /// consecutive fields would put pFile 32 bytes too early and shift every field
    /// after it, so they overlap at 40. Second, the header interleaves DWORDs and
    /// pointers with ABI padding between them (after cbSize, and a 4-byte pad at 52
    /// before hWVTStateData), so explicit offsets pin every field to its true slot.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct WinTrustData
    {
        [FieldOffset(0)] public uint cbSize;
        [FieldOffset(8)] public IntPtr pPolicyCallbackData;
        [FieldOffset(16)] public IntPtr pSIPClientData;
        [FieldOffset(24)] public uint dwUIChoice;
        [FieldOffset(28)] public uint fdwRevocationChecks;
        [FieldOffset(32)] public uint dwUnionChoice;
        [FieldOffset(40)] public IntPtr pFile;
        [FieldOffset(40)] public IntPtr pCatalog;
        [FieldOffset(40)] public IntPtr pBlob;
        [FieldOffset(40)] public IntPtr pSgnr;
        [FieldOffset(40)] public IntPtr pCert;
        [FieldOffset(48)] public uint dwStateAction;
        [FieldOffset(56)] public IntPtr hWVTStateData;
        [FieldOffset(64)] public IntPtr pwszURLReference;
        [FieldOffset(72)] public uint dwProvFlags;
        [FieldOffset(76)] public uint dwUIContext;
        [FieldOffset(80)] public IntPtr pSignatureSettings;
    }
}
