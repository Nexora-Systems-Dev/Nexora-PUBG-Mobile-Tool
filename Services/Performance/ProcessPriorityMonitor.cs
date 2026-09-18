using Nexora.Configuration;

namespace Nexora.Services.Performance;

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
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            cancellation = _monitorCancellation;
            cancellation?.Cancel();
            _monitorTask = null;
            _monitorCancellation = null;
        }

        cancellation?.Dispose();
    }

    /// <summary>
    /// Signals cancellation and awaits monitor loop termination up to the configured stop timeout.
    /// </summary>
    public async Task StopMonitorAsync(CancellationToken cancellationToken = default)
    {
        Task? monitor;
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            cancellation = _monitorCancellation;
            cancellation?.Cancel();
            monitor = _monitorTask;
            _monitorTask = null;
            _monitorCancellation = null;
        }

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
}
