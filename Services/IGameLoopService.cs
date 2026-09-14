using Nexora.Features.GameLoop;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

/// <summary>
/// Contract for GameLoop emulator connectivity, PUBG Mobile version detection, graphics configuration, and shadow settings.
/// </summary>
public interface IGameLoopService
{
    string? CurrentPackage { get; }

    /// <summary>
    /// Indicates whether GameLoop ADB is reachable and a supported PUBG package was found.
    /// </summary>
    bool IsGameLoopConnected { get; }

    bool IsConnected { get; }

    void Disconnect();

    Task<ConnectionResult> ConnectAsync(CancellationToken cancellationToken);

    Task<OperationResult> LoadVersionAsync(string packageName, CancellationToken cancellationToken);

    string GetGraphicsQuality();

    string GetFrameRate();

    string GetGraphicsStyle();

    Task<string> GetShadowAsync(CancellationToken cancellationToken);

    Task<OperationResult> ApplyGraphicsAsync(GraphicsSelection selection, CancellationToken cancellationToken);
}
