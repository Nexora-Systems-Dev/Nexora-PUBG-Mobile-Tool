using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.GameLoop.Infrastructure;
namespace Nexora.Features.Graphics.Domain;

/// <summary>
/// Canonical catalog of supported PUBG Mobile packages and the UE4 save-value
/// mappings for graphics quality, frame rate, and style selections.
/// </summary>
public static class PubgVersionCatalog
{
    /// <summary>
    /// Package name of PUBG Mobile KR, the only version with the optional 1080p path.
    /// </summary>
    public const string KoreanPackage = "com.pubg.krmobile";

    /// <summary>
    /// Whether a package is the Korean build, which alone exposes the 1080p
    /// workflow. Every gate funnels through here so the toggle, the summary and
    /// the apply payload can never disagree about which version is loaded.
    /// </summary>
    public static bool IsKoreanPackage(string? packageName) =>
        string.Equals(packageName, KoreanPackage, StringComparison.OrdinalIgnoreCase);

    public static readonly IReadOnlyDictionary<string, string> PubgVersions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["com.tencent.ig"] = "PUBG Mobile Global",
            ["com.vng.pubgmobile"] = "PUBG Mobile VN",
            ["com.rekoo.pubgm"] = "PUBG Mobile TW",
            [KoreanPackage] = "PUBG Mobile KR",
            ["com.pubg.imobile"] = "Battlegrounds Mobile India"
        };

    private static readonly IReadOnlyDictionary<string, byte> QualityValues =
        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["Smooth"] = 0x01,
            ["Balanced"] = 0x02,
            ["HD"] = 0x03,
            ["HDR"] = 0x04,
            ["Ultra HDR"] = 0x05,
            ["Extreme HDR"] = 0x06
        };

    private static readonly IReadOnlyDictionary<string, byte> FrameRateValues =
        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["Low"] = 0x02,
            ["Medium"] = 0x03,
            ["High"] = 0x04,
            ["Ultra"] = 0x05,
            ["Extreme"] = 0x06,
            ["Extreme+"] = 0x07,
            ["Ultra Extreme"] = 0x08
        };

    private static readonly IReadOnlyDictionary<string, byte> StyleValues =
        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["Classic"] = 0x01,
            ["Colorful"] = 0x02,
            ["Realistic"] = 0x03,
            ["Soft"] = 0x04,
            ["Movie"] = 0x06
        };

    public static bool TryResolveGraphicsValues(
        GraphicsSelection selection,
        out byte qualityByte,
        out byte fpsByte,
        out byte styleByte)
    {
        fpsByte = 0;
        styleByte = 0;

        return QualityValues.TryGetValue(selection.Quality, out qualityByte) &&
               FrameRateValues.TryGetValue(selection.FrameRate, out fpsByte) &&
               StyleValues.TryGetValue(selection.Style, out styleByte);
    }

    /// <summary>
    /// Single-name lookups into the canonical save-value maps, so live
    /// verification and diagnostics never carry their own drifting copies.
    /// </summary>
    public static bool TryGetQualityValue(string name, out byte value) =>
        QualityValues.TryGetValue(name, out value);

    public static bool TryGetFrameRateValue(string name, out byte value) =>
        FrameRateValues.TryGetValue(name, out value);

    public static bool TryGetStyleValue(string name, out byte value) =>
        StyleValues.TryGetValue(name, out value);

    /// <summary>
    /// Maps a save byte to its display name, or null when the byte is not a known value.
    /// Unknown bytes must never masquerade as a confident setting.
    /// </summary>
    public static string? QualityName(byte value) => value switch
    {
        0x01 => "Smooth",
        0x02 => "Balanced",
        0x03 => "HD",
        0x04 => "HDR",
        0x05 => "Ultra HDR",
        0x06 => "Extreme HDR",
        _ => null
    };

    /// <summary>
    /// Maps a save byte to its display name, or null when the byte is not a known value.
    /// Unknown bytes must never masquerade as a confident setting.
    /// </summary>
    public static string? FrameRateName(byte value) => value switch
    {
        0x02 => "Low",
        0x03 => "Medium",
        0x04 => "High",
        0x05 => "Ultra",
        0x06 => "Extreme",
        0x07 => "Extreme+",
        0x08 => "Ultra Extreme",
        _ => null
    };

    /// <summary>
    /// Maps a save byte to its display name, or null when the byte is not a known value.
    /// Unknown bytes must never masquerade as a confident setting.
    /// </summary>
    public static string? StyleName(byte value) => value switch
    {
        0x01 => "Classic",
        0x02 => "Colorful",
        0x03 => "Realistic",
        0x04 => "Soft",
        0x06 => "Movie",
        _ => null
    };
}
