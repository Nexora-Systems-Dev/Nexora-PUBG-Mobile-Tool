namespace Nexora.Features.Updates.Domain;

public sealed record UpdateInfo(
    bool Available,
    string LatestVersion,
    string AssetName,
    string DownloadUrl,
    string ChangeLog,
    string? ExpectedSha256 = null);
