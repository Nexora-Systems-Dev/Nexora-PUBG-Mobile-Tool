namespace Nexora.Features.GameLoop;

public sealed record PubgVersion(string PackageName, string DisplayName);

public sealed record ConnectionResult(
    bool Success,
    string Message,
    IReadOnlyList<PubgVersion> InstalledVersions);

public sealed record GraphicsSelection(
    string Quality,
    string FrameRate,
    string Style,
    bool EnableShadow,
    bool EnableKoreanFullHd);
