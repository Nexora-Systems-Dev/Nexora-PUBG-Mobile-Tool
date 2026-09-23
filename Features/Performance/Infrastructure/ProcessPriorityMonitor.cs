using Nexora.Configuration;

namespace Nexora.Features.Performance.Infrastructure;

/// <summary>
/// Keeps High priority applied to GameLoop processes across emulator launches
/// via a periodic background monitor loop.
/// </summary>
public sealed class ProcessPriorityMonitor : IProcessPriorityMonitor
{
    private readonly object _sync = new();
    private readonly IProcessPrioritySnapshotStore _store;
    private readonly IProcessPriorityApplier _applier;
    private readonly GameLoopOptions _gameLoop;
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;

    public ProcessPriorityMonitor(IProcessPrioritySnapshotStore store, IProcessPriorityApplier applier, GameLoopOptions? gameLoop = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _applier = applier ?? throw new ArgumentNullException(nameof(applier));
        _gameLoop = gameLoop ?? new GameLoopOptions();
    }

    public void Start(string? gameLoopRoot)
    {
        lock (_sync)
        {
            if (_monitorTask is { IsCompleted: false }) return;

            // Release the previous cancellation source before creating a new one.
            _monitorCancellation?.Dispose();
            _monitorCancellation = new CancellationTokenSource();
            var cancellation = _monitorCancellation;
            _monitorTask = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(_gameLoop.Timeouts.MonitorInterval);
                try
                {
                    while (await timer.WaitForNextTickAsync(cancellation.Token))
                    {
                        _store.PruneDeadSnapshots();
                        _applier.ApplyToRunningProcesses(gameLoopRoot);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation on monitor stop.
                }
            });
        }
    }

    /// <summary>
    /// Signals the monitor loop to stop without awaiting its completion.
    /// </summary>
    public void Stop()
    {
        var (_, cancellation) = CancelAndClearLoop();
        cancellation?.Dispose();
    }

    /// <summary>
    /// Signals cancellation and awaits monitor loop termination up to the configured stop timeout.
    /// </summary>
    public async Task StopMonitorAsync(CancellationToken cancellationToken = default)
    {
        var (monitor, cancellation) = CancelAndClearLoop();

        try
        {
            if (monitor is not null)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_gameLoop.Timeouts.MonitorStopTimeout);
                await monitor.WaitAsync(timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The stop timeout elapsed; allow shutdown to continue.
        }
        finally
        {
            cancellation?.Dispose();
        }
    }

    /// <summary>
    /// Cancels the running loop and clears both fields under the monitor lock,
    /// returning the handles the caller needs to finish the stop outside it.
    /// Disposing stays with the caller so the awaiting path releases the
    /// cancellation source only after the loop has actually terminated.
    /// </summary>
    private (Task? Monitor, CancellationTokenSource? Cancellation) CancelAndClearLoop()
    {
        lock (_sync)
        {
            var cancellation = _monitorCancellation;
            cancellation?.Cancel();
            var monitor = _monitorTask;
            _monitorTask = null;
            _monitorCancellation = null;
            return (monitor, cancellation);
        }
    }
}
