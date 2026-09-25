using Nexora.Features.Optimizer.Application;
using Nexora.Features.Performance.Application;
using Nexora.Shared.Contracts;
using Nexora.UI.Presentation;

namespace Nexora.Features.Optimizer.Presentation;

/// <summary>
/// A profile refresh outcome: the formatted display on success, or the error
/// message to paint when hardware detection fails. The two are mutually
/// exclusive by construction.
/// </summary>
public sealed record OptimizerProfileRefresh(OptimizerDisplayModel? Display, string? ErrorMessage);

/// <summary>
/// Owns the Optimizer page's execution flow: hardware profile refreshes and
/// the long-running tool operations (temp cleanup, smart settings, boosts,
/// sessions, force close).
/// </summary>
/// <remarks>
/// Per decision <b>D1</b> the Optimizer feature is presentation-only: this
/// type holds no plan-building or execution logic of its own. Every operation
/// delegates to <see cref="IGameLoopPerformanceEngine"/>,
/// <see cref="ITempCleanupService"/>, or <see cref="IGameLoopProcessService"/>,
/// and every display state comes back through the members below rather than
/// reaching into the visual tree. Formatting lives in the testable
/// <see cref="OptimizerDisplayFormatter"/>.
/// </remarks>
public sealed class OptimizerViewModel
{
    private readonly IGameLoopPerformanceEngine _engine;
    private readonly ITempCleanupService _tempCleanup;
    private readonly IGameLoopProcessService _processService;
    private readonly IPageOperationBus _operationBus;
    private CancellationTokenSource? _toolCancellation;

    /// <summary>
    /// The in-flight profile refresh's cancellation: the shell's Cancel()
    /// pre-empts it at close/navigate-away so the 20 s detection spawn can
    /// never outlive the page. Paint-gating alone is not enough — the token
    /// travels into the engine, and a pre-empted refresh settles as a
    /// silent no-op (a read with no reader), never a throw.
    /// </summary>
    private CancellationTokenSource? _refreshCancellation;

    public OptimizerViewModel(
        IGameLoopPerformanceEngine engine,
        ITempCleanupService tempCleanup,
        IGameLoopProcessService processService,
        IPageOperationBus operationBus)
    {
        _engine = engine;
        _tempCleanup = tempCleanup;
        _processService = processService;
        _operationBus = operationBus;
    }

    /// <summary>
    /// A status line for the shell's window status bar. The page's own
    /// status/activity cells are painted by the view from each returned
    /// outcome; this event carries the same message to the window bar.
    /// </summary>
    public event Action<string, bool>? StatusChanged;

    /// <summary>The shared guard, so the page's handlers can refuse while another operation runs.</summary>
    public bool IsBusy => _operationBus.IsBusy;

    /// <summary>The engine behind the page's tool buttons. Exposed so the view's
    /// handlers can build their operations without the ViewModel duplicating
    /// the engine's vocabulary.</summary>
    public IGameLoopPerformanceEngine Engine => _engine;

    /// <summary>The temp cleanup behind the Clean Cache button.</summary>
    public ITempCleanupService TempCleanup => _tempCleanup;

    /// <summary>The process service behind the Force Close button.</summary>
    public IGameLoopProcessService ProcessService => _processService;

    /// <summary>
    /// Reloads the hardware snapshot and its recommended plan. A busy bus or a
    /// detection failure is reported through the outcome — never thrown — so
    /// the view can paint the panel or the error cell without branching on
    /// exceptions. Deliberately takes no bus slot: like the pre-extraction
    /// refresh it only refuses while busy. The shell's close-linked token
    /// (plus Cancel() below) travels into the engine: a close landing before
    /// the detection spawn starts pre-empts it now instead of at its 20 s
    /// timeout, and settles as a silent no-op.
    /// </summary>
    public async Task<OptimizerProfileRefresh?> RefreshProfileAsync(CancellationToken cancellationToken = default)
    {
        if (_operationBus.IsBusy) return null;

        // Local so the source is disposed when the call ends; the field is
        // just the live handle the shell's Cancel() pre-empts, cleared before
        // disposal so a late Cancel() no-ops instead of hitting a disposed
        // source (the ExecuteAsync ownership shape).
        using var refresh = new CancellationTokenSource();
        _refreshCancellation = refresh;

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, refresh.Token);
            var hardware = await _engine.GetHardwareSnapshotAsync(linked.Token);
            var plan = _engine.GetRecommendedPlan(hardware);
            return new OptimizerProfileRefresh(OptimizerDisplayFormatter.Format(hardware, plan), null);
        }
        catch (OperationCanceledException)
        {
            // The reader went away (close/navigate): nothing to paint, and
            // the view's own shutdown guards make a reported cancel moot.
            return null;
        }
        catch (Exception ex)
        {
            return new OptimizerProfileRefresh(null, $"Hardware detection failed: {ex.Message}");
        }
        finally
        {
            _refreshCancellation = null;
        }
    }

    /// <summary>
    /// The window-wide "one long operation at a time" core that the shell's
    /// <c>RunToolAsync</c> used to own for this page: acquire the bus, run the
    /// operation on a cancellable token, translate cancellation and failure
    /// into results, and always release. Button enablement and the
    /// status/activity cells stay in the view; the shell status line travels
    /// through <see cref="StatusChanged"/>. The spine itself lives in
    /// <see cref="OperationSpine"/>, shared with the Shortcuts page; this
    /// method keeps only this page's cancellation ownership.
    /// </summary>
    public async Task<OperationResult> ExecuteAsync(Func<CancellationToken, Task<OperationResult>> action)
    {
        // Local so the source is disposed when the call ends; the field is just
        // the live handle the shell's Cancel() pre-empts, cleared before
        // disposal so a late Cancel() no-ops instead of hitting a disposed
        // source.
        using var cancellation = new CancellationTokenSource();
        _toolCancellation = cancellation;

        try
        {
            return await OperationSpine.RunAsync(
                new OperationContext(_operationBus, StatusChanged), cancellation.Token, action);
        }
        finally
        {
            _toolCancellation = null;
        }
    }

    /// <summary>
    /// Cancels whatever optimizer work is in flight — the tool operation and
    /// the profile refresh alike. Called at window close and navigate-away so
    /// an outstanding boost or detection spawn can never outlive the shell.
    /// </summary>
    public void Cancel()
    {
        _toolCancellation?.Cancel();
        _refreshCancellation?.Cancel();
    }
}
