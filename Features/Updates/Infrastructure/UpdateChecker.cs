using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Nexora.Configuration;
using Nexora.Features.Updates.Domain;

namespace Nexora.Features.Updates.Infrastructure;

/// <summary>
/// Checks the GitHub releases feed for a newer Nexora version over HTTP.
/// </summary>
public sealed class UpdateChecker
{
    private readonly HttpClient _httpClient;
    private readonly UpdateOptions _updates;

    public UpdateChecker(HttpClient httpClient, UpdateOptions? updates = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _updates = updates ?? new UpdateOptions();
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _updates.ReleasesUrl);
        request.Headers.UserAgent.ParseAdd(_updates.UserAgent);
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
        var assetName = ReadAssetString(asset, "name");
        var downloadUrl = ReadAssetString(asset, "browser_download_url");
        var expectedSha256 = UpdateArchiveValidator.TryExtractSha256(changelog);
        return new UpdateInfo(IsNewerThan(AppConstants.CurrentVersion, latest), latest, assetName, downloadUrl, changelog, expectedSha256);
    }

    /// <summary>
    /// Reports whether the feed tag names a release newer than the running
    /// build. Both tags must parse as vX.Y.Z; anything unparsable fails
    /// closed, so garbage never prompts and a stale feed never downgrades.
    /// </summary>
    internal static bool IsNewerThan(string currentVersion, string latestVersion) =>
        TryParseReleaseVersion(currentVersion, out var current) &&
        TryParseReleaseVersion(latestVersion, out var latest) &&
        latest.CompareTo(current) > 0;

    /// <summary>
    /// Parses a release tag such as "v1.3.0": surrounding whitespace ignored,
    /// one optional leading v/V, then exactly three non-negative integer parts.
    /// </summary>
    private static bool TryParseReleaseVersion(string? version, out Version parsed)
    {
        parsed = new Version(0, 0, 0);
        var text = version?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text[1..];
        var parts = text.Split('.');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        parsed = new Version(major, minor, patch);
        return true;
    }

    /// <summary>Reads a string property from a possibly-absent release asset element.</summary>
    private static string ReadAssetString(JsonElement asset, string propertyName) =>
        asset.ValueKind == JsonValueKind.Object && asset.TryGetProperty(propertyName, out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
