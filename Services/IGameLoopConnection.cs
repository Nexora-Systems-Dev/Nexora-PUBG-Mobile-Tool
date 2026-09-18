using Nexora.Features.GameLoop;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

/// <summary>
/// Connection/session side of the GameLoop split: emulator reachability,
/// PUBG package selection, and the shared session state the graphics side
/// reads and applies against. Implemented by <see cref="GameLoopService"/>
/// on the same instance as <see cref="IGraphicsProfileStore"/> — the gate
/// and session are shared, so the facets must never be split across
/// instances.
/// </summary>
public interface IGameLoopConnection
{
    string? CurrentPackage { get; }

    /// <summary>
    /// Indicates whether GameLoop ADB is reachable and a supported PUBG package was found.
    /// </summary>
    bool IsAdbConnected { get; }

    bool IsConnected { get; }

    void Disconnect();

    Task<ConnectionResult> ConnectAsync(CancellationToken cancellationToken);

    Task<OperationResult> LoadVersionAsync(string packageName, CancellationToken cancellationToken);
}
