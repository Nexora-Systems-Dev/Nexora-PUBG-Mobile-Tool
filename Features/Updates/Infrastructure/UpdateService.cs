using System.IO.Compression;
using System.Net;
using System.Net.Http;
using Nexora.Configuration;
using Nexora.Shared.Kernel;
using Nexora.Features.Updates.Application;
using Nexora.Features.Updates.Domain;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Updates.Infrastructure;

public sealed class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly IProcessRunner _runner;
    private readonly UpdateChecker _checker;
    private readonly UpdateOptions _updates;
    private readonly string? _currentExecutablePath;
    private readonly string? _stagingBaseDirectory;
    private readonly Func<string, bool>? _signatureCheck;
    private readonly bool _httpClientInjected;

    public UpdateService(IProcessRunner runner, HttpClient? httpClient = null, string? currentExecutablePath = null, string? stagingBaseDirectory = null, UpdateOptions? updates = null, Func<string, bool>? signatureCheck = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _updates = updates ?? new UpdateOptions();
        _httpClientInjected = httpClient is not null;
        _httpClient = httpClient ?? new HttpClient { Timeout = _updates.HttpTimeout };
        _checker = new UpdateChecker(_httpClient, _updates);
        _currentExecutablePath = currentExecutablePath;
        _stagingBaseDirectory = stagingBaseDirectory;
        _signatureCheck = signatureCheck;
    }

    private string GetStagingBaseDirectory() => _stagingBaseDirectory ?? Path.GetTempPath();

    /// <summary>
    /// Purges orphaned staging trees from previous runs, then builds a fresh
    /// unique tree so a pre-created directory in the staging base can never be reused.
    /// </summary>
    private (string StagingRoot, string ArchivePath, string ExtractionRoot) PrepareStagingTree()
    {
        var stagingBase = GetStagingBaseDirectory();
        StagingDirectoryGC.PurgeStaleStagingDirectories(stagingBase, updates: _updates);
        var stagingRoot = Path.Combine(stagingBase, _updates.StagingPrefix + Guid.NewGuid().ToString("N"));
        return (stagingRoot, Path.Combine(stagingRoot, _updates.ArchiveFileName), Path.Combine(stagingRoot, _updates.ExtractionFolderName));
    }

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

        var (stagingRoot, archivePath, extractionRoot) = PrepareStagingTree();

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
        if (!update.Available) return OperationResult.Fail("No downloadable update was found.");
        if (string.IsNullOrWhiteSpace(update.DownloadUrl) || string.IsNullOrWhiteSpace(update.AssetName))
        {
            return OperationResult.Fail("The latest release published no downloadable update archive.");
        }
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
        var downloadFailure = await DownloadArchiveWithTrustedRedirectsAsync(update.DownloadUrl, archivePath, cancellationToken);
        if (downloadFailure is not null) return (downloadFailure, null);
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

        PruneExtractionToAllowlist(extractionRoot, executablePath);
        return (null, executablePath);
    }

    /// <summary>
    /// Deletes every extracted entry except the verified executable and the
    /// release notes the packaging script ships beside it
    /// (<c>RELEASE-README.txt</c>), so nothing else can ride the handoff into
    /// the install directory. Leftovers stay in staging and die with it.
    /// </summary>
    private static void PruneExtractionToAllowlist(string extractionRoot, string executablePath)
    {
        var keepExecutable = Path.GetFullPath(executablePath);
        var keepReadme = Path.GetFullPath(Path.Combine(extractionRoot, "RELEASE-README.txt"));
        foreach (var file in Directory.EnumerateFiles(extractionRoot, "*", SearchOption.AllDirectories).ToList())
        {
            var fullPath = Path.GetFullPath(file);
            if (fullPath.Equals(keepExecutable, StringComparison.OrdinalIgnoreCase) ||
                fullPath.Equals(keepReadme, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Delete(fullPath);
        }

        foreach (var directory in Directory.EnumerateDirectories(extractionRoot, "*", SearchOption.AllDirectories).ToList().OrderByDescending(static path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }

    /// <summary>
    /// Maximum redirect hops followed for an update download. GitHub release
    /// downloads settle in one or two hops; five is the conventional
    /// manual-walk ceiling and keeps the whole fetch inside one HttpTimeout.
    /// </summary>
    private const int MaxDownloadRedirects = 5;

    /// <summary>
    /// Streams the update archive to <paramref name="archivePath"/>, validating
    /// every redirect hop with <see cref="IsTrustedDownloadUrl"/> before
    /// requesting it. Fails closed on untrusted hops, loops, hop exhaustion,
    /// and redirect responses without a Location header.
    /// </summary>
    /// <returns>A failure result when the download was refused or the chain was
    /// invalid; null after the archive has been fully written.</returns>
    private async Task<OperationResult?> DownloadArchiveWithTrustedRedirectsAsync(string downloadUrl, string archivePath, CancellationToken cancellationToken)
    {
        // One deadline for the whole fetch: all hops plus the body share a
        // single HttpTimeout instead of each hop getting its own, so a long
        // chain can never stretch the download into minutes. The linked token
        // also carries the caller's cancellation into every stalled hop.
        using var budgetSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budgetSource.CancelAfter(_updates.HttpTimeout);
        var budget = budgetSource.Token;

        // The shared client keeps its default handler (auto-redirect on) for
        // the releases feed. The archive fetch must observe each hop to
        // validate it, so default-constructed transports download through a
        // local client with auto-redirect off. Caller-injected clients (the
        // test seam) are used as-is: stub handlers surface responses without
        // following redirects, which keeps the manual walk — and the per-hop
        // trust check — genuine on that path too.
        using HttpClient? noRedirectClient = _httpClientInjected
            ? null
            : new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        var fetchClient = noRedirectClient ?? _httpClient;

        var current = new Uri(downloadUrl, UriKind.Absolute);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { current.AbsoluteUri };
        for (var hop = 0; ; hop++)
        {
            budget.ThrowIfCancellationRequested();
            if (!IsTrustedDownloadUrl(current.AbsoluteUri))
            {
                return OperationResult.Fail("The update download address was rejected.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            using var response = await fetchClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget);
            if (!IsRedirect(response.StatusCode))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(budget);
                await using var destination = File.Create(archivePath);
                await source.CopyToAsync(destination, budget);
                return null;
            }

            if (hop >= MaxDownloadRedirects)
            {
                throw new InvalidOperationException($"The update download was redirected too many times (>{MaxDownloadRedirects}).");
            }

            var location = response.Headers.Location ?? throw new InvalidOperationException("The update download redirect is missing its target address.");
            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (!visited.Add(next.AbsoluteUri))
            {
                throw new InvalidOperationException("The update download was redirected in a loop.");
            }

            current = next;
        }
    }

    /// <summary>Reports whether a status code is a GET-followable redirect.</summary>
    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently // 301
            or HttpStatusCode.Found // 302
            or HttpStatusCode.SeeOther // 303
            or HttpStatusCode.TemporaryRedirect // 307
            or HttpStatusCode.PermanentRedirect; // 308

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
        if (!string.IsNullOrWhiteSpace(_currentExecutablePath)) return Path.GetFullPath(_currentExecutablePath);

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && !IsTestHostPath(processPath)) return Path.GetFullPath(processPath);

        var mainModulePath = TryGetMainModulePath();
        if (!string.IsNullOrEmpty(mainModulePath)) return mainModulePath;

        var fallbackPath = Path.Combine(AppContext.BaseDirectory, _updates.PortableExecutableName);
        return File.Exists(fallbackPath) ? Path.GetFullPath(fallbackPath) : string.Empty;
    }

    /// <summary>Best-effort module probe: MainModule can throw in restricted security contexts.</summary>
    private static string? TryGetMainModulePath()
    {
        try
        {
            var mainModulePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(mainModulePath) && !IsTestHostPath(mainModulePath)
                ? Path.GetFullPath(mainModulePath)
                : null;
        }
        catch
        {
            return null;
        }
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
        if (IsRunningFromStaging(targetExecutablePath, stagingBaseDirectory, updates))
        {
            validationError = "The application cannot be updated while running from a temporary update staging directory.";
            return false;
        }

        return true;
    }

    /// <summary>Guards the staging base the update flow actually uses, not just %TEMP%.</summary>
    private static bool IsRunningFromStaging(string targetExecutablePath, string? stagingBaseDirectory, UpdateOptions? updates)
    {
        var stagingBase = stagingBaseDirectory ?? Path.GetTempPath();
        var stagingPrefix = Path.Combine(stagingBase, (updates ?? new UpdateOptions()).StagingPrefix);
        return targetExecutablePath.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
