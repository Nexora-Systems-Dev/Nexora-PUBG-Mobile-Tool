using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Nexora.Models;

namespace Nexora.Services;

public sealed record UpdateInfo(bool Available, string LatestVersion, string AssetName, string DownloadUrl, string ChangeLog);

public sealed class UpdateService
{
    private const string CurrentVersion = "v1.0.9";
    private const string ReleaseUrl = "https://api.github.com/repos/mohammad-emad-dev/Nexora-PUBG-Mobile-Tool/releases/latest";
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(12) };

    public async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseUrl);
        request.Headers.UserAgent.ParseAdd("Nexora-PUBG-Mobile-Tool");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return new UpdateInfo(false, CurrentVersion, string.Empty, string.Empty, string.Empty);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var latest = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? CurrentVersion : CurrentVersion;
        var changelog = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;
        var asset = root.TryGetProperty("assets", out var assets)
            ? assets.EnumerateArray().FirstOrDefault(candidate =>
                candidate.TryGetProperty("name", out var name) &&
                name.GetString()?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
            : default;
        var assetName = asset.ValueKind == JsonValueKind.Object && asset.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty;
        var downloadUrl = asset.ValueKind == JsonValueKind.Object && asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() ?? string.Empty : string.Empty;
        return new UpdateInfo(!string.Equals(CurrentVersion, latest, StringComparison.OrdinalIgnoreCase), latest, assetName, downloadUrl, changelog);
    }

    public async Task<OperationResult> DownloadAndLaunchAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        if (!update.Available || string.IsNullOrWhiteSpace(update.DownloadUrl) || string.IsNullOrWhiteSpace(update.AssetName)) return OperationResult.Fail("No downloadable update was found.");
        var archivePath = Path.Combine(Path.GetTempPath(), update.AssetName);
        try
        {
            await using (var source = await _httpClient.GetStreamAsync(update.DownloadUrl, cancellationToken))
            await using (var destination = File.Create(archivePath)) await source.CopyToAsync(destination, cancellationToken);
            using var archive = ZipFile.OpenRead(archivePath);
            var executable = archive.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (executable is null) return OperationResult.Fail("The update archive does not contain an executable.");
            var extractionRoot = Path.Combine(Path.GetTempPath(), "NexoraUpdate");
            Directory.CreateDirectory(extractionRoot);
            archive.ExtractToDirectory(extractionRoot, overwriteFiles: true);
            var executablePath = Path.Combine(extractionRoot, executable.FullName.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(executablePath)) return OperationResult.Fail("The extracted update executable was not found.");
            Process.Start(new ProcessStartInfo { FileName = executablePath, UseShellExecute = true, Verb = "runas" });
            return OperationResult.Ok("Update downloaded. The installer is starting.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Update failed: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch { }
        }
    }
}
