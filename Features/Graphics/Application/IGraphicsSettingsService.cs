using Nexora.Features.GameLoop.Domain;
using Nexora.Features.Graphics.Domain;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Graphics.Application;

/// <summary>
/// Read-back and apply of the GameLoop graphics profile, giving the Graphics
/// page one call instead of four store round-trips plus a connection lookup.
/// </summary>
/// <remarks>
/// The apply path delegates to <see cref="IGraphicsProfileStore"/>; the ADB
/// push, .sav patch, and process launch stay in GraphicsSettingsApplier behind
/// that store. <see cref="ApplyAsync"/> lets <see cref="OperationCanceledException"/>
/// propagate so the caller can render the cancellation message verbatim.
/// </remarks>
public interface IGraphicsSettingsService
{
    /// <summary>
    /// Reads the current profile, or null when no transport is connected and
    /// there is therefore nothing to read.
    /// </summary>
    Task<GraphicsCurrentSettings?> LoadCurrentAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> ApplyAsync(GraphicsSelection selection, CancellationToken cancellationToken = default);
}
