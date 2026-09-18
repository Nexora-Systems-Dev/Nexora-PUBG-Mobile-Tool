using System.IO.Compression;
using System.Net.Http;
using Nexora.Configuration;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

public sealed class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly IProcessRunner _runner;
    private readonly UpdateChecker _checker;
    private readonly UpdateOptions _updates;
    private readonly string? _currentExecutablePath;
    private readonly string? _stagingBaseDirectory;
    private readonly Func<string, bool>? _signatureCheck;

    public UpdateService(IProcessRunner runner, HttpClient? httpClient = null, string? currentExecutablePath = null, string? stagingBaseDirectory = null, UpdateOptions? updates = null, Func<string, bool>? signatureCheck = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _updates = updates ?? new UpdateOptions();
        _httpClient = httpClient ?? new HttpClient { Timeout = _updates.HttpTimeout };
        _checker = new UpdateChecker(_httpClient, _updates);
        _currentExecutablePath = currentExecutablePath;
        _stagingBaseDirectory = stagingBaseDirectory;
        _signatureCheck = signatureCheck;
    }

    private string GetStagingBaseDirectory() => _stagingBaseDirectory ?? Path.GetTempPath();

    public Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default) =>
        _checker.CheckAsync(cancellationToken);

    public async Task<OperationResult> DownloadAndLaunchAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        var requestFailure = ValidateUpdateRequest(update);
        if (requestFailure is not null) return requestFailure;

        var targetExecutablePath = GetCurrentExecutablePath();
        if (!ValidateTargetPath(targetExecutablePath, out var targetError, _updates, GetStagingBaseDirectory()))
        {
            return OperationResult.Fail(targetError);
        }

        // Clean up orphaned staging directories from previous runs.
        StagingDirectoryGC.PurgeStaleStagingDirectories(GetStagingBaseDirectory(), updates: _updates);

        // Use a unique directory to prevent local pre-creation attacks in the staging base.
        var stagingRoot = Path.Combine(GetStagingBaseDirectory(), _updates.StagingPrefix + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(stagingRoot, _updates.ArchiveFileName);
        var extractionRoot = Path.Combine(stagingRoot, _updates.ExtractionFolderName);

        var stagedSuccessfully = false;
        try
        {
            var (stageFailure, executablePath) = await StageUpdateArchiveAsync(update, archivePath, extractionRoot, cancellationToken);
            if (stageFailure is not null) return stageFailure;

            var launchFailure = LaunchHandoff(executablePath!, targetExecutablePath, extractionRoot, stagingRoot);
            if (launchFailure is not null) return launchFailure;

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
                StagingDirectoryGC.TryDeleteDirectory(stagingRoot);
            }
        }
    }

    /// <summary>
    /// Rejects missing or untrusted update metadata before any filesystem work.
    /// </summary>
    /// <returns>A failure result when the request is invalid; null when it can proceed.</returns>
    private static OperationResult? ValidateUpdateRequest(UpdateInfo update)
    {
        if (!update.Available || string.IsNullOrWhiteSpace(update.DownloadUrl) || string.IsNullOrWhiteSpace(update.AssetName)) return OperationResult.Fail("No downloadable update was found.");
        if (!IsTrustedDownloadUrl(update.DownloadUrl)) return OperationResult.Fail("The update download address was rejected.");
        return null;
    }

    /// <summary>
    /// Downloads the update archive, extracts it safely, and verifies the executable.
    /// </summary>
    /// <returns>The staging failure (if any) and the verified executable path.</returns>
    private async Task<(OperationResult? Failure, string? ExecutablePath)> StageUpdateArchiveAsync(UpdateInfo update, string archivePath, string extractionRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(extractionRoot);
        await using (var source = await _httpClient.GetStreamAsync(update.DownloadUrl, cancellationToken))
        await using (var destination = File.Create(archivePath)) await source.CopyToAsync(destination, cancellationToken);
        using var archive = ZipFile.OpenRead(archivePath);
        var executableEntry = UpdateArchiveValidator.FindExpectedExecutable(archive, update.LatestVersion, _updates);
        if (executableEntry is null) return (OperationResult.Fail("The update archive does not contain the expected Nexora executable."), null);
        UpdateArchiveValidator.ExtractEntriesSafely(archive, extractionRoot, cancellationToken);
        var executablePath = UpdateArchiveValidator.GetSafeExtractionPath(extractionRoot, executableEntry.FullName);
        if (executablePath is null || !File.Exists(executablePath)) return (OperationResult.Fail("The extracted update executable was not found."), null);
        if (!UpdateArchiveValidator.VerifyExecutableIntegrity(executablePath, update.ExpectedSha256, out var integrityError, _updates, _signatureCheck))
        {
            return (OperationResult.Fail(integrityError), null);
        }

        return (null, executablePath);
    }

    /// <summary>
    /// Builds the restart handoff script and launches it in an elevated shell.
    /// </summary>
    /// <returns>A failure result when the handoff could not start; null on success.</returns>
    private OperationResult? LaunchHandoff(string executablePath, string targetExecutablePath, string extractionRoot, string stagingRoot)
    {
        var handoffScript = UpdateHandoffBuilder.BuildHandoffScript(
            Environment.ProcessId,
            executablePath,
            targetExecutablePath,
            extractionRoot,
            stagingRoot);
        var handoffArguments = UpdateHandoffBuilder.BuildPowerShellArguments(handoffScript);

        if (!_runner.StartDetachedElevated("powershell.exe", handoffArguments))
        {
            return OperationResult.Fail("The update handoff process could not be started.");
        }

        return null;
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

    /// <summary>
    /// Reports whether a process path belongs to a test/dotnet host and must
    /// never be treated as the application executable. Blank paths report
    /// true so they are skipped the same way.
    /// </summary>
    internal static bool IsTestHostPath(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return true;
        }

        var fileName = Path.GetFileNameWithoutExtension(processPath);
        return string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "testhost", StringComparison.OrdinalIgnoreCase);
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
        if (!string.IsNullOrWhiteSpace(processPath) && !IsTestHostPath(processPath))
        {
            return Path.GetFullPath(processPath);
        }

        try
        {
            var mainModulePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(mainModulePath) && !IsTestHostPath(mainModulePath))
            {
                return Path.GetFullPath(mainModulePath);
            }
        }
        catch
        {
            // MainModule can throw in restricted security contexts.
        }

        var fallbackPath = Path.Combine(AppContext.BaseDirectory, _updates.PortableExecutableName);
        if (File.Exists(fallbackPath))
        {
            return Path.GetFullPath(fallbackPath);
        }

        return string.Empty;
    }

    internal static bool ValidateTargetPath(string targetExecutablePath, out string validationError, UpdateOptions? updates = null, string? stagingBaseDirectory = null)
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

        // Guard against the staging base the update flow actually uses, not just %TEMP%.
        var stagingBase = stagingBaseDirectory ?? Path.GetTempPath();
        var stagingPrefix = Path.Combine(stagingBase, (updates ?? new UpdateOptions()).StagingPrefix);
        if (targetExecutablePath.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
        {
            validationError = "The application cannot be updated while running from a temporary update staging directory.";
            return false;
        }

        return true;
    }
}
