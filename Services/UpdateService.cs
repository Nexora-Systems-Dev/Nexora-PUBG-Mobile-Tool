using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Nexora.Configuration;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

public sealed record UpdateInfo(
    bool Available,
    string LatestVersion,
    string AssetName,
    string DownloadUrl,
    string ChangeLog,
    string? ExpectedSha256 = null);

public sealed class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly IProcessRunner _runner;
    private readonly string? _currentExecutablePath;

    public UpdateService(IProcessRunner? runner = null, HttpClient? httpClient = null, string? currentExecutablePath = null)
    {
        _runner = runner ?? new ProcessRunner();
        _httpClient = httpClient ?? new HttpClient { Timeout = AppConstants.Update.HttpTimeout };
        _currentExecutablePath = currentExecutablePath;
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AppConstants.Update.ReleasesUrl);
        request.Headers.UserAgent.ParseAdd(AppConstants.Update.UserAgent);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return new UpdateInfo(false, AppConstants.CurrentVersion, string.Empty, string.Empty, string.Empty);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var latest = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? AppConstants.CurrentVersion : AppConstants.CurrentVersion;
        var changelog = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;
        var asset = root.TryGetProperty("assets", out var assets)
            ? assets.EnumerateArray().FirstOrDefault(candidate =>
                candidate.TryGetProperty("name", out var name) &&
                name.GetString()?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
            : default;
        var assetName = asset.ValueKind == JsonValueKind.Object && asset.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty;
        var downloadUrl = asset.ValueKind == JsonValueKind.Object && asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() ?? string.Empty : string.Empty;
        var expectedSha256 = TryExtractSha256(changelog);
        return new UpdateInfo(!string.Equals(AppConstants.CurrentVersion, latest, StringComparison.OrdinalIgnoreCase), latest, assetName, downloadUrl, changelog, expectedSha256);
    }

    public async Task<OperationResult> DownloadAndLaunchAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        if (!update.Available || string.IsNullOrWhiteSpace(update.DownloadUrl) || string.IsNullOrWhiteSpace(update.AssetName)) return OperationResult.Fail("No downloadable update was found.");
        if (!IsTrustedDownloadUrl(update.DownloadUrl)) return OperationResult.Fail("The update download address was rejected.");

        var targetExecutablePath = GetCurrentExecutablePath();
        if (!ValidateTargetPath(targetExecutablePath, out var targetError))
        {
            return OperationResult.Fail(targetError);
        }

        // Clean up orphaned staging directories from previous runs.
        PurgeStaleStagingDirectories();

        // Use a unique directory to prevent local pre-creation attacks in %TEMP%.
        var stagingRoot = Path.Combine(Path.GetTempPath(), AppConstants.Update.StagingPrefix + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(stagingRoot, AppConstants.Update.ArchiveFileName);
        var extractionRoot = Path.Combine(stagingRoot, AppConstants.Update.ExtractionFolderName);

        var stagingPrefix = Path.Combine(Path.GetTempPath(), AppConstants.Update.StagingPrefix);
        if (targetExecutablePath.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail("The application cannot be updated while running from a temporary update staging directory.");
        }

        var stagedSuccessfully = false;
        try
        {
            Directory.CreateDirectory(extractionRoot);
            await using (var source = await _httpClient.GetStreamAsync(update.DownloadUrl, cancellationToken))
            await using (var destination = File.Create(archivePath)) await source.CopyToAsync(destination, cancellationToken);
            using var archive = ZipFile.OpenRead(archivePath);
            var executableEntry = FindExpectedExecutable(archive, update.LatestVersion);
            if (executableEntry is null) return OperationResult.Fail("The update archive does not contain the expected Nexora executable.");
            ExtractEntriesSafely(archive, extractionRoot, cancellationToken);
            var executablePath = GetSafeExtractionPath(extractionRoot, executableEntry.FullName);
            if (executablePath is null || !File.Exists(executablePath)) return OperationResult.Fail("The extracted update executable was not found.");
            if (!VerifyExecutableIntegrity(executablePath, update.ExpectedSha256, out var integrityError))
            {
                return OperationResult.Fail(integrityError);
            }

            var handoffScript = BuildHandoffScript(
                Environment.ProcessId,
                executablePath,
                targetExecutablePath,
                extractionRoot,
                stagingRoot);
            var handoffArguments = BuildPowerShellArguments(handoffScript);

            if (!_runner.StartDetachedElevated("powershell.exe", handoffArguments))
            {
                return OperationResult.Fail("The update handoff process could not be started.");
            }

            stagedSuccessfully = true;
            return OperationResult.Ok("Update downloaded and verified. The application will restart to complete installation.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Update failed: {ex.Message}");
        }
        finally
        {
            // If the handoff process was not launched, remove staging files immediately.
            if (!stagedSuccessfully)
            {
                TryDeleteDirectory(stagingRoot);
            }
        }
    }

    /// <summary>
    /// Removes staging directories older than <paramref name="maxAge"/>.
    /// Ignores directories that are currently locked or in active use.
    /// </summary>
    internal static int PurgeStaleStagingDirectories(string? tempDirectory = null, TimeSpan? maxAge = null)
    {
        var threshold = DateTime.UtcNow - (maxAge ?? AppConstants.Update.StaleStagingMaxAge);
        string[] candidates;
        try
        {
            candidates = Directory.EnumerateDirectories(
                tempDirectory ?? Path.GetTempPath(),
                AppConstants.Update.StagingPrefix + "*").ToArray();
        }
        catch
        {
            return 0;
        }

        var removed = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(candidate) > threshold) continue;
                TryDeleteDirectory(candidate);
                if (!Directory.Exists(candidate)) removed++;
            }
            catch
            {
                // Skip directories that cannot be accessed or deleted.
            }
        }

        return removed;
    }

    /// <summary>
    /// Validates that <paramref name="downloadUrl"/> uses HTTPS and targets an allowed GitHub host.
    /// </summary>
    internal static bool IsTrustedDownloadUrl(string downloadUrl)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TrustedDownloadHosts.Contains(uri.Host);
    }

    private static readonly HashSet<string> TrustedDownloadHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "objects.githubusercontent.com",
        "github-releases.githubusercontent.com"
    };

    private static ZipArchiveEntry? FindExpectedExecutable(ZipArchive archive, string latestVersion)
    {
        var expectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"Nexora-{latestVersion}-{AppConstants.Update.Runtime}.exe",
            AppConstants.Update.PortableExecutableName
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

    private static void ExtractEntriesSafely(ZipArchive archive, string extractionRoot, CancellationToken cancellationToken)
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

    private static string? GetSafeExtractionPath(string extractionRoot, string entryFullName)
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

    internal static void TryDeleteDirectory(string path) => FileUtilities.TryDeleteDirectory(path);

    /// <summary>
    /// Validates update executable integrity using Authenticode verification or expected SHA-256 hash.
    /// </summary>
    internal static bool VerifyExecutableIntegrity(string executablePath, string? expectedSha256, out string errorMessage)
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

            return true;
        }

        if (IsAuthenticodeSigned(executablePath))
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
    internal static bool IsAuthenticodeSigned(string filePath, string? expectedPublisher = AppConstants.Update.ExpectedPublisher)
    {
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

    /// <summary>
    /// Resolves the file path of the currently running application executable.
    /// Excludes test host processes to prevent tests from targeting the test runner.
    /// </summary>
    internal string GetCurrentExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(_currentExecutablePath))
        {
            return Path.GetFullPath(_currentExecutablePath);
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var fileName = Path.GetFileNameWithoutExtension(processPath);
            if (!string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fileName, "testhost", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(processPath);
            }
        }

        try
        {
            var mainModulePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(mainModulePath))
            {
                var fileName = Path.GetFileNameWithoutExtension(mainModulePath);
                if (!string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(fileName, "testhost", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(mainModulePath);
                }
            }
        }
        catch
        {
            // MainModule can throw in restricted security contexts.
        }

        var fallbackPath = Path.Combine(AppContext.BaseDirectory, AppConstants.Update.PortableExecutableName);
        if (File.Exists(fallbackPath))
        {
            return Path.GetFullPath(fallbackPath);
        }

        return string.Empty;
    }

    internal static bool ValidateTargetPath(string targetExecutablePath, out string validationError)
    {
        validationError = string.Empty;
        if (string.IsNullOrWhiteSpace(targetExecutablePath))
        {
            validationError = "The current application executable path could not be determined.";
            return false;
        }

        if (!File.Exists(targetExecutablePath))
        {
            validationError = $"The target executable was not found at '{targetExecutablePath}'.";
            return false;
        }

        var targetDirectory = Path.GetDirectoryName(targetExecutablePath);
        if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
        {
            validationError = "The application installation directory could not be determined.";
            return false;
        }

        var stagingPrefix = Path.Combine(Path.GetTempPath(), AppConstants.Update.StagingPrefix);
        if (targetExecutablePath.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
        {
            validationError = "The application cannot be updated while running from a temporary update staging directory.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Generates the self-update handoff PowerShell script that waits for the parent process
    /// to exit, backs up and replaces target files, launches the update, and cleans staging.
    /// </summary>
    internal static string BuildHandoffScript(
        int parentPid,
        string sourceExecutablePath,
        string targetExecutablePath,
        string extractionRoot,
        string stagingRoot)
    {
        var quotedSource = ProcessText.Quote(Path.GetFullPath(sourceExecutablePath));
        var quotedTarget = ProcessText.Quote(Path.GetFullPath(targetExecutablePath));
        var quotedExtraction = ProcessText.Quote(Path.GetFullPath(extractionRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var quotedStaging = ProcessText.Quote(Path.GetFullPath(stagingRoot));

        return $$"""
$ErrorActionPreference = 'Stop'
$parentPid = {{parentPid}}
$sourceExe = {{quotedSource}}
$targetExe = {{quotedTarget}}
$extractionRoot = {{quotedExtraction}}
$stagingRoot = {{quotedStaging}}
$targetDir = Split-Path -Parent $targetExe
$backupExe = "$targetExe.bak"
$backupCreated = $false

# 1. Wait for running application process to terminate
if ($parentPid -gt 0) {
    try {
        $parent = Get-Process -Id $parentPid -ErrorAction SilentlyContinue
        if ($parent) {
            $exited = $parent.WaitForExit(30000)
            if (-not $exited) {
                Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
                exit 1
            }
        }
    } catch { }
}
Start-Sleep -Milliseconds 500

# 2. Verify source executable exists in staging
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    exit 2
}

# 3. Create backup of current target executable
if (Test-Path -LiteralPath $targetExe -PathType Leaf) {
    try {
        Copy-Item -LiteralPath $targetExe -Destination $backupExe -Force -ErrorAction Stop
        $backupCreated = $true
    } catch {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
        exit 3
    }
}

# 4. Copy payload files and replace target executable with retry
$copySuccess = $false
for ($attempt = 1; $attempt -le 5; $attempt++) {
    try {
        if (Test-Path -LiteralPath $extractionRoot -PathType Container) {
            Get-ChildItem -LiteralPath $extractionRoot -Recurse | ForEach-Object {
                $item = $_
                if ($item.FullName -ne $sourceExe) {
                    $relPath = $item.FullName.Substring($extractionRoot.Length).TrimStart('\', '/')
                    $destPath = Join-Path $targetDir $relPath
                    if ($item.PSIsContainer) {
                        if (-not (Test-Path -LiteralPath $destPath)) {
                            [System.IO.Directory]::CreateDirectory($destPath) | Out-Null
                        }
                    } else {
                        $destParent = Split-Path -Parent $destPath
                        if (-not (Test-Path -LiteralPath $destParent)) {
                            [System.IO.Directory]::CreateDirectory($destParent) | Out-Null
                        }
                        Copy-Item -LiteralPath $item.FullName -Destination $destPath -Force -ErrorAction Stop
                    }
                }
            }
        }

        Copy-Item -LiteralPath $sourceExe -Destination $targetExe -Force -ErrorAction Stop
        $copySuccess = $true
        break
    } catch {
        Start-Sleep -Milliseconds 500
    }
}

# 5. Handle failure: rollback to original executable and abort without relaunch
if (-not $copySuccess) {
    if ($backupCreated -and (Test-Path -LiteralPath $backupExe)) {
        try {
            Copy-Item -LiteralPath $backupExe -Destination $targetExe -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $backupExe -Force -ErrorAction SilentlyContinue
        } catch { }
    }
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    exit 4
}

# 6. Success: remove backup, launch updated application, and clean temporary staging
if ($backupCreated -and (Test-Path -LiteralPath $backupExe)) {
    Remove-Item -LiteralPath $backupExe -Force -ErrorAction SilentlyContinue
}

try {
    Start-Process -FilePath $targetExe -WorkingDirectory $targetDir
} catch { }

Start-Sleep -Milliseconds 500
Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
""";
    }

    /// <summary>
    /// Formats the PowerShell arguments for running the handoff script with elevation.
    /// </summary>
    internal static string BuildPowerShellArguments(string script)
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        return $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}";
    }
}
