using System.ComponentModel;
using Nexora.Features.GameLoop.Application;
using Nexora.Shared.Contracts;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.Graphics.Application;
using Nexora.Features.Graphics.Domain;
using Nexora.UI.Presentation;

namespace Nexora.Features.Graphics.Presentation;

/// <summary>
/// Owns the Graphics page's connection orchestration and apply flow: which
/// transport state the page is in, which versions the device holds, and how a
/// selection reaches the GameLoop profile.
/// </summary>
/// <remarks>
/// The page's controls stay in the view; this is the seam they delegate to, and
/// it reports back through the events below rather than reaching into the visual
/// tree. <see cref="ConnectionStateChanged"/> is the composite payload the shell
/// subscribes to for the title-bar pill and sidebar indicator (D5), because the
/// phase and its message must always be painted as one unit.
/// </remarks>
public sealed class GraphicsViewModel : INotifyPropertyChanged
{
    private readonly IGameLoopConnection _connection;
    private readonly IGraphicsSettingsService _graphics;
    private readonly IAdbClient _adb;
    private readonly IPageOperationBus _operationBus;
    private CancellationTokenSource? _connectionCancellation;

    public GraphicsViewModel(
        IGameLoopConnection connection,
        IGraphicsSettingsService graphics,
        IAdbClient adb,
        IPageOperationBus operationBus)
    {
        _connection = connection;
        _graphics = graphics;
        _adb = adb;
        _operationBus = operationBus;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// The connection phase plus the message that explains it. The shell paints
    /// the pill/sidebar from this; the page paints its own dot, detail line and
    /// summary from the same event so the two can never disagree.
    /// </summary>
    public event Action<ConnectionState, string>? ConnectionStateChanged;

    /// <summary>
    /// A status line for the page's status bar. The shell's own statuses reach
    /// that same surface through the view, so the bar still has one writer per
    /// message even though it is now hosted inside the page.
    /// </summary>
    public event Action<string, bool>? StatusChanged;

    /// <summary>
    /// A freshly read profile, or null when nothing has been read. The view
    /// repaints the selection controls from this; a null or unrecognized value
    /// must never render as a confident setting.
    /// </summary>
    public event Action<GraphicsCurrentSettings?>? SettingsLoaded;

    /// <summary>
    /// The page's busy-visual toggle. This is the enablement signal only — the
    /// guard itself is the shared <see cref="IPageOperationBus"/> — so the
    /// sidebar's refresh button, which the shell owns, stays in step with the
    /// page's apply button.
    /// </summary>
    public event Action<bool>? BusyVisualChanged;

    private ConnectionState _connectionState = ConnectionState.Disconnected;

    /// <summary>
    /// The coarse connection phase, for any subscriber that only needs the
    /// phase and not its message.
    /// </summary>
    public ConnectionState ConnectionState
    {
        get => _connectionState;
        private set
        {
            if (_connectionState == value) return;
            _connectionState = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConnectionState)));
        }
    }

    private IReadOnlyList<PubgVersion> _installedVersions = Array.Empty<PubgVersion>();

    /// <summary>
    /// The versions the connect found on the device. The shell seeds its
    /// shortcut list from this because cross-page wiring belongs to the shell,
    /// not to the feature.
    /// </summary>
    public IReadOnlyList<PubgVersion> InstalledVersions
    {
        get => _installedVersions;
        private set
        {
            _installedVersions = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InstalledVersions)));
        }
    }

    /// <summary>The shared guard, so the page's handlers can refuse while another operation runs.</summary>
    public bool IsBusy => _operationBus.IsBusy;

    /// <summary>A transport is up, which is what makes the profile readable.</summary>
    public bool IsAdbConnected => _connection.IsAdbConnected;

    /// <summary>A version is loaded, which is what makes apply available.</summary>
    public bool IsVersionLoaded => _connection.IsConnected;

    /// <summary>
    /// The Korean build alone exposes the 1080p workflow. This is the page's
    /// single gate for it, so the toggle, the summary and the apply payload can
    /// never disagree about which version is loaded.
    /// </summary>
    public bool IsKoreanVersion => PubgVersionCatalog.IsKoreanPackage(_connection.CurrentPackage);

    /// <summary>
    /// Connects to GameLoop, or disconnects when a transport is already up. The
    /// refusal while busy and the disconnect-without-acquiring mirror the
    /// single-button behavior the page has always had.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_operationBus.IsBusy) return;

        if (_connection.IsAdbConnected)
        {
            await DisconnectAsync();
            return;
        }

        if (!_operationBus.TryAcquire()) return;

        CancelConnection();
        _connectionCancellation = new CancellationTokenSource();
        // Captured once: the field may be nulled by a disconnect/close that
        // lands mid-connect, and no later line may re-read it across an await.
        var connectionToken = _connectionCancellation.Token;
        BusyVisualChanged?.Invoke(true);
        StatusChanged?.Invoke("Connecting to GameLoop...", false);

        ConnectionResult result;
        try
        {
            // Progress posts to the captured UI context and reuses the existing
            // status event — no new event types, no new thread coupling.
            var progress = new Progress<string>(message => StatusChanged?.Invoke(message, false));
            result = await _connection.ConnectAsync(connectionToken, progress);
        }
        catch (OperationCanceledException)
        {
            result = new ConnectionResult(false, "Connection canceled.", Array.Empty<PubgVersion>());
        }
        catch (Exception ex)
        {
            result = new ConnectionResult(false, ex.Message, Array.Empty<PubgVersion>());
        }
        finally
        {
            BusyVisualChanged?.Invoke(false);
            _operationBus.Release();
        }

        await RaiseConnectOutcomeAsync(result, connectionToken);
    }

    /// <summary>
    /// Paints the outcome a connect produced: the shell's shortcut list gets the
    /// discovered versions and the pill gets its phase. A connect that reached a
    /// version also reads the profile, so that arm does more than a state lookup.
    /// The token is the connect's own, passed as a parameter — never re-read
    /// from the field — and every paint below re-checks liveness first: a
    /// disconnect landing mid-load leaves the Disconnected paint standing and
    /// no stale versions or profile ever reach the tree.
    /// </summary>
    private async Task RaiseConnectOutcomeAsync(ConnectionResult result, CancellationToken connectionToken)
    {
        if (!result.Success)
        {
            InstalledVersions = result.InstalledVersions;
            RaiseConnectionState(ConnectionState.Failed, result.Message);
            return;
        }

        if (_connection.IsConnected)
        {
            var current = await _graphics.LoadCurrentAsync(connectionToken);
            if (!_connection.IsConnected) return;
            InstalledVersions = result.InstalledVersions;
            SettingsLoaded?.Invoke(current);
        }

        var state = _connection.IsConnected ? ConnectionState.FullyConnected
            : _connection.IsAdbConnected ? ConnectionState.TransportConnected
            : ConnectionState.AwaitingVersion;
        RaiseConnectionState(state, result.Message);
    }

    /// <summary>
    /// Tears the transport down and stops adb. Genuinely async: the taskkill
    /// wait runs off-thread, bounded by the configured kill timeout, so the
    /// caller thread is free immediately. CancellationToken.None — not the
    /// just-canceled connect token — because best-effort teardown must still
    /// run; it never throws. Report goes through the same state event as a
    /// connect so the disconnected paint is identical from either direction.
    /// </summary>
    public async Task DisconnectAsync()
    {
        CancelConnection();
        _connection.Disconnect();
        await _adb.StopAdbAsync(CancellationToken.None);
        RaiseConnectionState(ConnectionState.Disconnected, "Disconnected from GameLoop.");
    }

    /// <summary>
    /// Loads a chosen version's profile. A version already loaded, or a busy
    /// bus, is a no-op — the latter is what keeps a double-click from stacking a
    /// second load on the first.
    /// </summary>
    public async Task LoadVersionAsync(PubgVersion version)
    {
        if (_connection.IsConnected) return;
        if (!_operationBus.TryAcquire()) return;

        StatusChanged?.Invoke($"Loading {version.DisplayName}...", false);
        var cancellationToken = _connectionCancellation?.Token ?? CancellationToken.None;
        try
        {
            var result = await _connection.LoadVersionAsync(version.PackageName, cancellationToken);
            if (result.Success)
            {
                var current = await _graphics.LoadCurrentAsync(cancellationToken);
                SettingsLoaded?.Invoke(current);
                RaiseConnectionState(ConnectionState.FullyConnected, result.Message);
            }
            else
            {
                StatusChanged?.Invoke(result.Message, true);
            }
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke("Loading version settings was canceled.", false);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Could not load {version.DisplayName}: {ex.Message}", true);
        }
        finally
        {
            _operationBus.Release();
        }
    }

    /// <summary>
    /// Pushes a selection to the GameLoop profile. The store throws on
    /// cancellation and that is deliberately preserved: the caller renders the
    /// canceled message verbatim instead of the store inventing one.
    /// </summary>
    public async Task<OperationResult> ApplyAsync(GraphicsSelection selection)
    {
        if (!_operationBus.TryAcquire()) return OperationResult.Fail("Another operation is already running.");

        BusyVisualChanged?.Invoke(true);
        StatusChanged?.Invoke("Applying graphics settings...", false);
        var cancellationToken = _connectionCancellation?.Token ?? CancellationToken.None;
        try
        {
            return await _graphics.ApplyAsync(selection, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Skip("Graphics application was canceled.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
        finally
        {
            BusyVisualChanged?.Invoke(false);
            _operationBus.Release();
        }
    }

    /// <summary>
    /// Cancels whatever connection work is in flight. Called at window close so
    /// an outstanding load can never outlive the shell.
    /// </summary>
    public void Cancel() => CancelConnection();

    private void CancelConnection()
    {
        var cancellation = _connectionCancellation;
        _connectionCancellation = null;
        if (cancellation is null) return;
        try
        {
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void RaiseConnectionState(ConnectionState state, string message)
    {
        ConnectionState = state;
        ConnectionStateChanged?.Invoke(state, message);
    }
}