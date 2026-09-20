using Nexora.Features.Graphics.Domain;
namespace Nexora.Features.GameLoop.Domain;

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
    bool EnableKoreanFullHd)
{
    /// <summary>
    /// Fallback selection when no UI option is checked. The three names must
    /// exist in the <see cref="PubgVersionCatalog"/> value maps; both toggles
    /// default off. Drift surfaces at apply time via TryResolveGraphicsValues.
    /// </summary>
    public static GraphicsSelection Defaults { get; } = new("Smooth", "Low", "Classic", false, false);
}
