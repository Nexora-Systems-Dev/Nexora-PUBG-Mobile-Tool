using Nexora.Features.GameLoop;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

/// <summary>
/// Graphics-profile side of the GameLoop split: reading the current PUBG
/// Mobile profile out of the Unreal save and applying a new selection.
/// Implemented by <see cref="GameLoopService"/> on the same instance as
/// <see cref="IGameLoopConnection"/> — the gate and session are shared,
/// so the facets must never be split across instances.
/// </summary>
public interface IGraphicsProfileStore
{
    string? GetGraphicsQuality();

    string? GetFrameRate();

    string? GetGraphicsStyle();

    Task<string?> GetShadowAsync(CancellationToken cancellationToken);

    Task<OperationResult> ApplyGraphicsAsync(GraphicsSelection selection, CancellationToken cancellationToken);
}
