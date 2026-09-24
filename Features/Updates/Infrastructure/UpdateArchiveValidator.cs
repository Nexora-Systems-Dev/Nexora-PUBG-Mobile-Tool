using System.IO.Compression;
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
        if (!WinTrustInterop.HasValidFileSignature(filePath)) return false;

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
            return PublisherIdentity.IsExpectedPublisher(cert, expectedPublisher);
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
    /// Tested surface for the file-binding primitive, whose implementation
    /// lives in <see cref="WinTrustInterop"/>. Forwards unchanged so existing
    /// callers keep their call site.
    /// </summary>
    internal static bool HasValidFileSignature(string filePath) =>
        WinTrustInterop.HasValidFileSignature(filePath);

    /// <summary>The chain policy used to verify an Authenticode publisher; all three flags kept in one place.</summary>
    private static X509ChainPolicy CreateChainPolicy() => new X509ChainPolicy
    {
        RevocationMode = X509RevocationMode.Online,
        RevocationFlag = X509RevocationFlag.ExcludeRoot,
        VerificationFlags = X509VerificationFlags.IgnoreEndRevocationUnknown |
                            X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown |
                            X509VerificationFlags.IgnoreRootRevocationUnknown
    };
}
