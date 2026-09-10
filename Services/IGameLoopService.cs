using Nexora.Features.GameLoop;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

/// <summary>
/// Contract orchestrating GameLoop emulator connectivity, PUBG Mobile version detection,
/// graphics configuration updates, and shadow tuning.
/// </summary>
public interface IGameLoopService
{
    string? CurrentPackage { get; }

    /// <summary>
    /// True when GameLoop ADB is available and at least one supported PUBG package was found.
    /// This is intentionally independent from loading the graphics profile.
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
