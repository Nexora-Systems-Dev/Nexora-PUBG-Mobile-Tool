using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Nexora.Configuration;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

public sealed record UpdateInfo(bool Available, string LatestVersion, string AssetName, string DownloadUrl, string ChangeLog);

public sealed class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly IProcessRunner _runner;

    public UpdateService(IProcessRunner? runner = null, HttpClient? httpClient = null)
    {
        _runner = runner ?? new ProcessRunner();
        _httpClient = httpClient ?? new HttpClient { Timeout = AppConstants.Update.HttpTimeout };
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
        return new UpdateInfo(!string.Equals(AppConstants.CurrentVersion, latest, StringComparison.OrdinalIgnoreCase), latest, assetName, downloadUrl, changelog);
    }

    public async Task<OperationResult> DownloadAndLaunchAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        if (!update.Available || string.IsNullOrWhiteSpace(update.DownloadUrl) || string.IsNullOrWhiteSpace(update.AssetName)) return OperationResult.Fail("No downloadable update was found.");
        if (!IsTrustedDownloadUrl(update.DownloadUrl)) return OperationResult.Fail("The update download address was rejected.");
        // Sweep staging trees orphaned by a previously killed or crashed run
        // before creating a new one. Best-effort: never affects the result.
        PurgeStaleStagingDirectories();
        // A unique staging directory per attempt prevents a local process from
        // pre-planting files at a predictable %TEMP%/NexoraUpdate path and
        // getting them executed with elevation.
        var stagingRoot = Path.Combine(Path.GetTempPath(), AppConstants.Update.StagingPrefix + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(stagingRoot, AppConstants.Update.ArchiveFileName);
        var extractionRoot = Path.Combine(stagingRoot, AppConstants.Update.ExtractionFolderName);
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
            if (!_runner.StartDetachedElevated(executablePath))
            {
                return OperationResult.Fail("The update installer could not be started.");
            }
            return OperationResult.Ok("Update downloaded. The installer is starting.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Update failed: {ex.Message}");
        }
        finally
        {
            // Best-effort sweep of the whole staging tree (archive plus
            // extracted files) on every branch. On success the just-launched
            // installer keeps a lock on its own executable, so locked files
            // are left for a later stale sweep instead of breaking the
            // running installer — everything deletable is still removed.
            TryDeleteDirectory(stagingRoot);
        }
    }

    /// <summary>
    /// Removes staging trees older than <paramref name="maxAge"/> whose names
    /// carry <see cref="AppConstants.Update.StagingPrefix"/>. Returns how many
    /// trees were fully removed. Never throws: every filesystem step is
    /// guarded so cleanup can neither crash the caller nor alter its result.
    /// Trees still in use stay untouched — recent ones by age, locked ones
    /// because only fully removed trees are counted.
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
                // Best-effort only: skip whatever cannot be inspected or removed.
            }
        }

        return removed;
    }

    private static bool IsTrustedDownloadUrl(string downloadUrl)
    {
        return Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

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

    /// <summary>
    /// Backward-compatible test and internal seam delegating to <see cref="FileUtilities.TryDeleteDirectory"/>.
    /// </summary>
    internal static void TryDeleteDirectory(string path) => FileUtilities.TryDeleteDirectory(path);
}
