using System.Formats.Asn1;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Nexora.Configuration;

namespace Nexora.Features.Updates.Infrastructure;

/// <summary>
/// Validates downloaded update archives: zip-slip path guard, SHA-256 checksum,
/// and Authenticode publisher chain verification.
/// </summary>
public static class UpdateArchiveValidator
{
    internal static ZipArchiveEntry? FindExpectedExecutable(ZipArchive archive, string latestVersion, UpdateOptions? updates = null)
    {
        updates ??= new UpdateOptions();
        var expectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"Nexora-{latestVersion}-{updates.Runtime}.exe",
            updates.PortableExecutableName
        };
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) ||
                !entry.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (expectedNames.Contains(relativePath))
            {
                return entry;
            }
        }

        return null;
    }

    internal static void ExtractEntriesSafely(ZipArchive archive, string extractionRoot, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(extractionRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var destination = GetSafeExtractionPath(extractionRoot, entry.FullName);
            if (destination is null || !destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The update archive contains an unsafe entry path.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    internal static string? GetSafeExtractionPath(string extractionRoot, string entryFullName)
    {
        try
        {
            var root = Path.GetFullPath(extractionRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var destination = Path.GetFullPath(Path.Combine(extractionRoot, entryFullName.Replace('/', Path.DirectorySeparatorChar)));
            return destination.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? destination : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Validates update executable integrity. Strongest available evidence wins:
    /// a valid Authenticode signature from the trusted publisher is accepted on
    /// its own; otherwise the payload must match an expected SHA-256 checksum
    /// that was published with the release. An unsigned payload with no
    /// published checksum is rejected outright.
    /// </summary>
    internal static bool VerifyExecutableIntegrity(string executablePath, string? expectedSha256, out string errorMessage, UpdateOptions? updates = null, Func<string, bool>? signatureCheck = null)
    {
        errorMessage = string.Empty;

        // Signature is the strongest evidence: it proves both integrity and
        // publisher identity, and it is the only evidence that scales once a
        // signing certificate is configured. It is checked first so a signed
        // release is never rejected over a stale published checksum.
        if (signatureCheck?.Invoke(executablePath) ?? IsAuthenticodeSigned(executablePath, updates: updates))
        {
            return true;
        }

        // Unsigned releases ship a published SHA-256 checksum instead. A payload
        // that matches it byte-for-byte, fetched over a trusted HTTPS feed, is
        // safe to install — this is the path the v1.0.15 release notes describe
        // as enabled.
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            if (MatchesExpectedChecksum(executablePath, expectedSha256)) return true;

            errorMessage = "The update executable is not signed by the trusted publisher and its hash does not match the expected checksum.";
            return false;
        }

        errorMessage = "The update executable is unverifiable: it has no trusted Authenticode signature or verified checksum.";
        return false;
    }

    /// <summary>Compares the on-disk SHA-256 against the checksum published with the release.</summary>
    private static bool MatchesExpectedChecksum(string executablePath, string expectedSha256) =>
        string.Equals(ComputeSha256(executablePath), expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Computes the hex-encoded SHA-256 hash of the specified file.
    /// </summary>
    internal static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Extracts a SHA-256 hash from release notes or changelog text.
    /// </summary>
    internal static string? TryExtractSha256(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Match labeled checksum (for example, SHA256: <hash>).
        var labelMatch = System.Text.RegularExpressions.Regex.Match(
            text,
            @"(?:sha-?256|checksum)[*`\s:=]+([0-9a-fA-F]{64})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (labelMatch.Success)
        {
            return labelMatch.Groups[1].Value.ToLowerInvariant();
        }

        // Match checksum table lines targeting an executable file.
        var sumMatch = System.Text.RegularExpressions.Regex.Match(
            text,
            @"([0-9a-fA-F]{64})\s+[^\r\n]*\.exe",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (sumMatch.Success)
        {
            return sumMatch.Groups[1].Value.ToLowerInvariant();
        }

        return null;
    }

    /// <summary>
    /// Verifies that the file carries a valid Authenticode signature from the expected publisher.
    /// </summary>
    internal static bool IsAuthenticodeSigned(string filePath, UpdateOptions? updates = null)
    {
        // Binding before identity: a signature that does not cover this file's
        // bytes proves nothing about it, no matter who issued the certificate.
        if (!HasValidFileSignature(filePath)) return false;

        // The expected publisher always resolves through options. Taking it as
        // an argument let a caller pass an empty string, which IsExpectedPublisher
        // treats as "any publisher is fine" — a silent widening of this check.
        var expectedPublisher = (updates ?? new UpdateOptions()).ExpectedPublisher;
        try
        {
#pragma warning disable CS0618
            using var rawCert = X509Certificate.CreateFromSignedFile(filePath);
            using var cert = new X509Certificate2(rawCert);
#pragma warning restore CS0618
            using var chain = new X509Chain { ChainPolicy = CreateChainPolicy() };
            if (!chain.Build(cert)) return false;
            return IsExpectedPublisher(cert, expectedPublisher);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Asks Windows to hash the file's bytes and compare that digest against the
    /// one sealed inside its signature — the binding <see cref="X509Chain.Build"/>
    /// never performs, and the reason a signature can otherwise be lifted off a
    /// genuinely signed file and dropped onto one that was never signed at all.
    /// </summary>
    /// <returns>True only when Windows accepts a signature over this exact file.</returns>
    internal static bool HasValidFileSignature(string filePath)
    {
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
    /// WINTRUST_FILE_INFO. Sequential layout over uint + two pointers: the runtime
    /// supplies the 4 bytes of padding the x64 ABI requires after cbSize, so the
    /// native size is 24 and Marshal.SizeOf reports exactly that for cbSize.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbSize;
        public IntPtr hFile;         // Zero: let Windows open the file by path.
        public IntPtr pcwszFilePath; // LPWSTR, owned by the caller and freed in the finally.
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

    /// <summary>The chain policy used to verify an Authenticode publisher; all three flags kept in one place.</summary>
    private static X509ChainPolicy CreateChainPolicy() => new X509ChainPolicy
    {
        RevocationMode = X509RevocationMode.Online,
        RevocationFlag = X509RevocationFlag.ExcludeRoot,
        VerificationFlags = X509VerificationFlags.IgnoreEndRevocationUnknown |
                            X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown |
                            X509VerificationFlags.IgnoreRootRevocationUnknown
    };

    /// <summary>
    /// Blank expected-publisher means the chain alone is sufficient evidence.
    /// Otherwise the certificate's Common Name must equal it exactly: an anchored
    /// comparison of the one attribute that names the publisher, never a
    /// substring search over the whole distinguished name.
    /// </summary>
    private static bool IsExpectedPublisher(X509Certificate2 cert, string? expectedPublisher)
    {
        if (string.IsNullOrWhiteSpace(expectedPublisher)) return true;
        return TryGetCommonName(cert) is { } commonName &&
            commonName.Equals(expectedPublisher, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the Common Name out of the subject's ASN.1 rather than out of the
    /// rendered string, so neither Windows' quoting of comma-bearing values nor
    /// a reordering of attributes changes what this predicate anchors on.
    /// </summary>
    /// <returns>The CN value, or null when the subject carries no CN at all.</returns>
    private static string? TryGetCommonName(X509Certificate2 cert)
    {
        try
        {
            // Name ::= SEQUENCE OF SET OF AttributeTypeAndValue.
            var relativeNames = new AsnReader(cert.SubjectName.RawData, AsnEncodingRules.BER).ReadSequence();
            while (relativeNames.HasData)
            {
                if (ReadCommonNameFrom(relativeNames.ReadSetOf()) is { } commonName) return commonName;
            }
            return null;
        }
        catch
        {
            // An unreadable subject has no CN this code can trust, so it matches nothing.
            return null;
        }
    }

    /// <summary>The first id-at-commonName attribute within this relative name, if it has one.</summary>
    private static string? ReadCommonNameFrom(AsnReader attributeSet)
    {
        while (attributeSet.HasData)
        {
            var attribute = attributeSet.ReadSequence();
            // The type is read as a whole OID, so an attacker-supplied value can never
            // be mistaken for the CN type: an "O=CN=Nexora" attribute still has type O.
            if (attribute.ReadObjectIdentifier() == CommonNameOid)
            {
                return DecodeDirectoryString(attribute);
            }
        }
        return null;
    }

    /// <summary>
    /// DirectoryString may be encoded under any of several universal string tags —
    /// Windows emits UTF8String, older CAs PrintableString — so the tag is peeked
    /// rather than assumed. A value this decoder cannot read becomes empty, which
    /// cannot match a non-blank expected publisher.
    /// </summary>
    private static string DecodeDirectoryString(AsnReader value)
    {
        var tag = value.PeekTag();
        return tag.TagClass == TagClass.Universal
            ? value.ReadCharacterString((UniversalTagNumber)tag.TagValue)
            : string.Empty;
    }

    /// <summary>id-at-commonName: the attribute this publisher check anchors on.</summary>
    private const string CommonNameOid = "2.5.4.3";
}
