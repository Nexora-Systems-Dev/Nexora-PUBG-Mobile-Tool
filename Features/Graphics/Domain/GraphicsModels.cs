namespace Nexora.Features.Graphics.Domain;

/// <summary>
/// Read-back of the current PUBG Mobile graphics profile from the Unreal save,
/// plus the derived Korean-version flag. Quality/frame rate/style are the
/// display names <see cref="PubgVersionCatalog"/> maps from save bytes, so a
/// null means "unrecognized or unread" and must never render as a confident
/// setting. Shadow is tristate for the same reason: null is the pre-connection
/// state where neither toggle is checked.
/// </summary>
public sealed record GraphicsCurrentSettings(
    string? Quality,
    string? FrameRate,
    string? Style,
    bool? ShadowEnabled,
    bool IsKoreanVersion);

/// <summary>
/// Coarse connection phases the Graphics page reports to the shell so the
/// sidebar pill and title-bar indicator stay in step with page-local visuals.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Failed,
    AwaitingVersion,
    TransportConnected,
    FullyConnected,
}
