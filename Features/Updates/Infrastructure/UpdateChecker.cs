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
        var assetName = asset.ValueKind == JsonValueKind.Object && asset.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty;
        var downloadUrl = asset.ValueKind == JsonValueKind.Object && asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() ?? string.Empty : string.Empty;
        var expectedSha256 = UpdateArchiveValidator.TryExtractSha256(changelog);
        return new UpdateInfo(!string.Equals(AppConstants.CurrentVersion, latest, StringComparison.OrdinalIgnoreCase), latest, assetName, downloadUrl, changelog, expectedSha256);
    }
}
