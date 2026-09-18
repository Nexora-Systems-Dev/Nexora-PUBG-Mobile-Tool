using Nexora.Features.GameLoop;
using Nexora.Services.Performance;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

/// <summary>
/// Coordinates GameLoop emulator connectivity, PUBG Mobile version detection,
/// and graphics settings persistence in Unreal Engine 4 save files.
/// Delegates to <see cref="GameLoopConnector"/>, <see cref="SaveProfileReader"/>,
/// and <see cref="GraphicsSettingsApplier"/> sharing a single <see cref="GameLoopSession"/>.
/// Implements both <see cref="IGameLoopConnection"/> and
/// <see cref="IGraphicsProfileStore"/> on one instance: the gate below and
/// the session above are shared by both facets, so splitting them across
/// instances would publish mismatched buffer/package pairs.
/// </summary>
public sealed class GameLoopService : IGameLoopConnection, IGraphicsProfileStore
{
    private readonly GameLoopSession _session = new();
    private readonly GameLoopConnector _connector;
    private readonly SaveProfileReader _reader;
    private readonly GraphicsSettingsApplier _applier;

    /// <summary>
    /// Serializes the three async session writers (Connect / LoadVersion /
    /// ApplyGraphics). The session's buffer/package pair is written
    /// non-atomically and the collaborators share working files plus an
    /// in-place edited save buffer, so overlapping writers can publish
    /// mismatched pairs or cross-contaminate buffers; the gate makes each
    /// writer run to completion before the next starts. Deliberately minimal:
    /// read-only accessors stay outside (worst case is a transient stale
    /// display value), and <see cref="Disconnect"/> stays outside too — it is
    /// synchronous, the UI only calls it when no gated operation is in flight
    /// (cooperative <c>_isBusy</c> guard plus cancel-first ordering), and a
    /// blocking wait here could deadlock a UI-thread caller against a
    /// context-captured continuation. Programmatic callers must therefore not
    /// race <see cref="Disconnect"/> against the gated methods. The gate is
    /// non-reentrant: it is acquired exactly once per public entry point, and
    /// the connector's internal Connect → LoadVersion flow stays below it and
    /// never re-acquires.
    /// </summary>
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public GameLoopService(IUserRegistry registry, IAdbClient adb, GameLoopWorkingStorage storage, IFileSystem fileSystem, IGameLoopProcessService processService)
    {
        _connector = new GameLoopConnector(registry, adb, storage, fileSystem, _session, processService);
        _reader = new SaveProfileReader(adb, storage, fileSystem, _session);
        _applier = new GraphicsSettingsApplier(adb, storage, fileSystem, _session);
    }

    public string? CurrentPackage => _session.CurrentPackage;

    /// <summary>
    /// Indicates whether ADB is connected and a supported PUBG package is detected.
    /// </summary>
    public bool IsAdbConnected => _session.IsAdbConnected;

    public bool IsConnected => _session.IsConnected;

    /// <summary>
    /// Resets connection state and clears in-memory save data.
    /// </summary>
    public void Disconnect() => _session.Reset();

    public async Task<ConnectionResult> ConnectAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            return await _connector.ConnectAsync(cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<OperationResult> LoadVersionAsync(string packageName, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            return await _connector.LoadVersionAsync(packageName, cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public string? GetGraphicsQuality() => _reader.GetGraphicsQuality();

    public string? GetFrameRate() => _reader.GetFrameRate();

    public string? GetGraphicsStyle() => _reader.GetGraphicsStyle();

    public Task<string?> GetShadowAsync(CancellationToken cancellationToken) =>
        _reader.GetShadowAsync(cancellationToken);

    public async Task<OperationResult> ApplyGraphicsAsync(GraphicsSelection selection, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            return await _applier.ApplyGraphicsAsync(selection, cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }
}
