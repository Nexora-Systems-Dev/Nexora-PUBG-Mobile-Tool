using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.Graphics.Domain;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Graphics.Application;

/// <summary>
/// Thin orchestration over <see cref="IGraphicsProfileStore"/>: one read-back
/// and one apply for the Graphics page, with no re-implementation of the ADB
/// push that lives behind the store.
/// </summary>
public sealed class GraphicsSettingsService : IGraphicsSettingsService
{
    private readonly IGraphicsProfileStore _graphics;
    private readonly IGameLoopConnection _connection;

    public GraphicsSettingsService(IGraphicsProfileStore graphics, IGameLoopConnection connection)
    {
        _graphics = graphics;
        _connection = connection;
    }

    /// <summary>
    /// Reads the four profile values and derives the Korean-version flag. The
    /// comparison against <see cref="PubgVersionCatalog.KoreanPackage"/> is the
    /// single place the 1080p path is gated, so the page and the summary cannot
    /// disagree about which version is loaded.
    /// </summary>
    public async Task<GraphicsCurrentSettings?> LoadCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (!_connection.IsAdbConnected) return null;

        var shadow = await _graphics.GetShadowAsync(cancellationToken);
        return new GraphicsCurrentSettings(
            Quality: _graphics.GetGraphicsQuality(),
            FrameRate: _graphics.GetFrameRate(),
            Style: _graphics.GetGraphicsStyle(),
            ShadowEnabled: ParseShadow(shadow),
            IsKoreanVersion: string.Equals(
                _connection.CurrentPackage,
                PubgVersionCatalog.KoreanPackage,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Saves the save-side "Enable"/"Disable" marker to the toggle tristate.
    /// An unrecognized value stays null rather than masquerading as a choice.
    /// </summary>
    private static bool? ParseShadow(string? shadow)
    {
        if (string.Equals(shadow, "Enable", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(shadow, "Disable", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    public Task<OperationResult> ApplyAsync(GraphicsSelection selection, CancellationToken cancellationToken = default) =>
        _graphics.ApplyGraphicsAsync(selection, cancellationToken);
}
