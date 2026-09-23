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

    /// <summary>Formats the six summary cells of the page's summary bar.</summary>
    public static GraphicsSummaryDisplay FormatSummary(
        string? version,
        string? quality,
        string? frameRate,
        string? style,
        bool? shadowEnabled,
        string? adb) => new(
            Version: Cell(version),
            Quality: Cell(quality),
            Fps: Cell(frameRate),
            Style: Cell(style),
            Shadow: FormatShadow(shadowEnabled),
            Adb: Cell(adb));

    /// <summary>
    /// A summary value, or the placeholder when it is blank. Whitespace-only is
    /// treated as unset, matching how an unread profile surfaces today, so a
    /// missing read-back can never look like a deliberate setting.
    /// </summary>
    private static string Cell(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Placeholder : value;
}
