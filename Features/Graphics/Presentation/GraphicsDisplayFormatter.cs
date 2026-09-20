namespace Nexora.Features.Graphics.Presentation;

/// <summary>
/// Display strings for the Graphics page summary bar.
/// </summary>
public sealed record GraphicsSummaryDisplay(
    string Version,
    string Quality,
    string Fps,
    string Style,
    string Shadow,
    string Adb);

/// <summary>
/// Formats Graphics page state into user-facing display strings, keeping the
/// "unknown never masquerades as a confident setting" rule that
/// PubgVersionCatalog enforces on the save side: every unread or unset value
/// renders as the placeholder.
/// </summary>
public static class GraphicsDisplayFormatter
{
    /// <summary>
    /// Shown when a summary value is unknown, so a missing read-back can never
    /// look like a deliberate setting.
    /// </summary>
    public const string Placeholder = "—";

    /// <summary>Connect button label flips between its two fixed captions.</summary>
    public static string FormatConnectLabel(bool isAdbConnected) =>
        isAdbConnected ? "DISCONNECT" : "CONNECT";

    /// <summary>
    /// Shadow toggle tristate: <c>true</c> enabled, <c>false</c> disabled,
    /// <c>null</c> neither checked (the pre-connection state) → placeholder.
    /// </summary>
    public static string FormatShadow(bool? shadowEnabled) => shadowEnabled switch
    {
        true => "Enabled",
        false => "Disabled",
        null => Placeholder
    };

    /// <summary>
    /// Formats the six summary cells. Whitespace-only strings are treated as
    /// unset, matching how an unread profile surfaces today.
    /// </summary>
    public static GraphicsSummaryDisplay FormatSummary(
        string? version,
        string? quality,
        string? frameRate,
        string? style,
        bool? shadowEnabled,
        string? adb) => new(
            Version: string.IsNullOrWhiteSpace(version) ? Placeholder : version,
            Quality: string.IsNullOrWhiteSpace(quality) ? Placeholder : quality,
            Fps: string.IsNullOrWhiteSpace(frameRate) ? Placeholder : frameRate,
            Style: string.IsNullOrWhiteSpace(style) ? Placeholder : style,
            Shadow: FormatShadow(shadowEnabled),
            Adb: string.IsNullOrWhiteSpace(adb) ? Placeholder : adb);
}
