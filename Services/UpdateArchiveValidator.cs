using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using Nexora.Configuration;

namespace Nexora.Services;

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
    /// Validates update executable integrity using SHA-256 checksum AND Authenticode verification.
    /// When an expected hash is provided, both the hash and a trusted publisher signature are required.
    /// </summary>
    internal static bool VerifyExecutableIntegrity(string executablePath, string? expectedSha256, out string errorMessage, UpdateOptions? updates = null, Func<string, bool>? signatureCheck = null)
    {
        errorMessage = string.Empty;

        var hasExpectedHash = !string.IsNullOrWhiteSpace(expectedSha256);
        if (hasExpectedHash)
        {
            var actualHash = ComputeSha256(executablePath);
            if (!string.Equals(actualHash, expectedSha256!.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "The update executable hash does not match the expected checksum.";
                return false;
            }

            var signed = signatureCheck?.Invoke(executablePath) ?? IsAuthenticodeSigned(executablePath, updates: updates);
            if (!signed)
            {
                errorMessage = "The update executable hash matches but it has no trusted Authenticode signature.";
                return false;
            }

            return true;
        }

        if (signatureCheck?.Invoke(executablePath) ?? IsAuthenticodeSigned(executablePath, updates: updates))
        {
            return true;
        }

        errorMessage = "The update executable is unverifiable: it has no trusted Authenticode signature or verified checksum.";
        return false;
    }

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
    internal static bool IsAuthenticodeSigned(string filePath, string? expectedPublisher = null, UpdateOptions? updates = null)
    {
        expectedPublisher ??= (updates ?? new UpdateOptions()).ExpectedPublisher;
        try
        {
#pragma warning disable CS0618
            using var rawCert = X509Certificate.CreateFromSignedFile(filePath);
            using var cert = new X509Certificate2(rawCert);
#pragma warning restore CS0618
            using var chain = new X509Chain
            {
                ChainPolicy =
                {
                    RevocationMode = X509RevocationMode.Online,
                    RevocationFlag = X509RevocationFlag.ExcludeRoot,
                    VerificationFlags = X509VerificationFlags.IgnoreEndRevocationUnknown |
                                        X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown |
                                        X509VerificationFlags.IgnoreRootRevocationUnknown
                }
            };

            var chainValid = chain.Build(cert);
            if (!chainValid)
            {
                return false;
            }

            // Verify certificate subject contains the expected publisher name.
            if (!string.IsNullOrWhiteSpace(expectedPublisher) &&
                cert.Subject.IndexOf(expectedPublisher, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return true;
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
}
