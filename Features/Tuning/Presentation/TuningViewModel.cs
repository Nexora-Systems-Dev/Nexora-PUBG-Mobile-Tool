using System.ComponentModel;
using Nexora.Features.Performance.Application;
using Nexora.Features.Tuning.Application;
using Nexora.Features.Tuning.Domain;
using Nexora.Shared.Contracts;
using Nexora.UI.Presentation;

namespace Nexora.Features.Tuning.Presentation;

/// <summary>
/// Owns the Tuning page's load/apply/end-task flow: the current GameLoop
/// user-hive values, the running-state guard that gates Apply, and the
/// loading/label state the view paints.
/// </summary>
/// <remarks>
/// The page's controls stay in the view; this is the seam they delegate to,
/// and it reports back through the events below rather than reaching into the
/// visual tree. The "close GameLoop before applying" guard itself lives in
/// <see cref="IEmulatorSettingsService"/>; this layer only surfaces it.
/// </remarks>
public sealed class TuningViewModel : INotifyPropertyChanged
{
    private readonly IEmulatorSettingsService _tuning;
    private readonly IGameLoopProcessService _processService;
    private readonly IPageOperationBus _operationBus;
    private CancellationTokenSource? _toolCancellation;

    public TuningViewModel(
        IEmulatorSettingsService tuning,
        IGameLoopProcessService processService,
        IPageOperationBus operationBus)
    {
        _tuning = tuning;
        _processService = processService;
        _operationBus = operationBus;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// A freshly loaded tuning state. The view repaints every control from
    /// this; a null load (busy, canceled, failed) leaves the controls
    /// untouched and the failure is reported through
    /// <see cref="StatusChanged"/> instead.
    /// </summary>
    public event Action<EmulatorTuningState>? StateLoaded;

    /// <summary>
    /// The page's loading-bar toggle. This is the visual signal only — the
    /// guard itself is the shared <see cref="IPageOperationBus"/> — so the
    /// status text and the Apply button stay in step with the refresh.
    /// </summary>
    public event Action<bool>? LoadingChanged;

    /// <summary>
    /// The page's busy-visual toggle around Apply and End Task. The guard is
    /// still the bus; this only tells the view when to dim its buttons.
    /// </summary>
    public event Action<bool>? BusyVisualChanged;

    /// <summary>
    /// A status line for the page's status cell. The shell forwards it to the
    /// window status bar, so the bar keeps one writer per message even though
    /// the surface lives on another page.
    /// </summary>
    public event Action<string, bool>? StatusChanged;

    private EmulatorTuningState? _currentState;

    /// <summary>The last loaded state, for any subscriber that needs it without a refresh.</summary>
    public EmulatorTuningState? CurrentState
    {
        get => _currentState;
        private set
        {
            _currentState = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentState)));
        }
    }

    /// <summary>The shared guard, so the page's handlers can refuse while another operation runs.</summary>
    public bool IsBusy => _operationBus.IsBusy;

    /// <summary>
    /// Reloads the emulator's current settings. A second click while busy is a
    /// no-op, so rapid navigation cannot stack hardware scans on each other.
    /// </summary>
    public async Task RefreshAsync()
    {
        // Cooperative guard: the refresh owns the operation bus for its
        // duration, so a second Tuning click cannot stack another hardware
        // scan on the first. Apply/End Task are blocked by the same bus, so
        // only the status text + accent bar show the busy state.
        if (!_operationBus.TryAcquire()) return;
        LoadingChanged?.Invoke(true);
        StatusChanged?.Invoke("Loading emulator settings...", false);

        _toolCancellation = new CancellationTokenSource();
        EmulatorTuningState state;
        try
        {
            // The hardware scan runs off-thread, so this token genuinely
            // pre-empts a stuck scan and hands control back to the user at
            // 15 s worst case, instead of freezing the UI for the full CIM
            // timeout. The bound is linked onto the shared tool source, so a
            // window-close Cancel() pre-empts the scan immediately rather than
            // waiting for the timeout to expire.
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(_toolCancellation.Token);
            bound.CancelAfter(TimeSpan.FromSeconds(15));
            state = await _tuning.LoadAsync(bound.Token);
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke("Loading emulator settings timed out.", true);
            return;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Could not load emulator settings: {ex.Message}", true);
            return;
        }
        finally
        {
            LoadingChanged?.Invoke(false);
            _operationBus.Release();
            CancelAndDisposeTool();
        }

        CurrentState = state;
        StateLoaded?.Invoke(state);
        if (state.IsGameLoopRunning)
        {
            StatusChanged?.Invoke("GameLoop is running. End its tasks, then apply.", true);
        }
        else
        {
            StatusChanged?.Invoke("Current settings loaded. Adjust, then apply.", false);
        }
    }

    /// <summary>
    /// Pushes a user-chosen selection into the GameLoop user hive. The service
    /// refuses while GameLoop runs; that refusal is rendered verbatim.
    /// </summary>
    public Task<OperationResult> ApplyAsync(EmulatorTuningSelection selection) =>
        ExecuteToolAsync("Applying emulator settings...", "Emulator tuning was canceled.",
            token => _tuning.ApplyAsync(selection, token));

    /// <summary>
    /// Ends every running GameLoop process so a subsequent Apply can write.
    /// Runs off-thread; the caller refreshes afterwards to repaint the guard.
    /// </summary>
    public Task<OperationResult> EndTaskAsync() =>
        ExecuteToolAsync("Ending GameLoop tasks...", "Operation canceled.",
            token => Task.Run(() => _processService.KillGameLoopProcesses(token)));

    /// <summary>
    /// The shared acquire → run → release core of Apply and End Task: the bus
    /// slot, the busy visual, the per-operation cancellation source the shell's
    /// <see cref="Cancel()"/> pre-empts, and the outcome → status translation.
    /// Every outcome — success, service refusal, cancellation, failure — is
    /// reported through <see cref="StatusChanged"/> and returned unchanged.
    /// </summary>
    private async Task<OperationResult> ExecuteToolAsync(
        string statusMessage,
        string canceledMessage,
        Func<CancellationToken, Task<OperationResult>> action)
    {
        if (!_operationBus.TryAcquire()) return OperationResult.Fail("Another operation is already running.");
        BusyVisualChanged?.Invoke(true);
        StatusChanged?.Invoke(statusMessage, false);

        _toolCancellation = new CancellationTokenSource();
        try
        {
            var result = await action(_toolCancellation.Token);
            StatusChanged?.Invoke(result.Message, !result.Success);
            return result;
        }
        catch (OperationCanceledException)
        {
            var canceled = OperationResult.Skip(canceledMessage);
            StatusChanged?.Invoke(canceled.Message, false);
            return canceled;
        }
        catch (Exception ex)
        {
            var failed = OperationResult.Fail(ex.Message);
            StatusChanged?.Invoke(failed.Message, true);
            return failed;
        }
        finally
        {
            BusyVisualChanged?.Invoke(false);
            _operationBus.Release();
            CancelAndDisposeTool();
        }
    }

    /// <summary>
    /// Cancels whatever tuning operation is in flight. Called at window close
    /// so an outstanding refresh, apply or end-task can never outlive the
    /// shell: a scan in flight at close is preempted now, not at its timeout.
    /// </summary>
    public void Cancel() => CancelAndDisposeTool();

    private void CancelAndDisposeTool()
    {
        var cancellation = _toolCancellation;
        _toolCancellation = null;
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

    /// <summary>The CPU slider's label. Presentation truth only — no system calls.</summary>
    public static string FormatCpuLabel(int cores) =>
        FormattableString.Invariant($"{cores} core{(cores == 1 ? string.Empty : "s")}");

    /// <summary>The memory slider's label. Presentation truth only — no system calls.</summary>
    public static string FormatMemoryLabel(int megabytes) =>
        FormattableString.Invariant($"{megabytes} MB");
}
