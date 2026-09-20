using System.Diagnostics;
using Nexora.Shared.Kernel;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Performance.Infrastructure;

/// <summary>
/// Sets and restores process priority for verified GameLoop emulator processes.
/// The applier, monitor, and snapshot store are composed via DI against small
/// interfaces, sharing a single snapshot store.
/// </summary>
public sealed class ProcessPriorityService
{
    private readonly IProcessPrioritySnapshotStore _store;
    private readonly IProcessPriorityApplier _applier;
    private readonly IProcessPriorityMonitor _monitor;

    public ProcessPriorityService(
        IProcessPrioritySnapshotStore store,
        IProcessPriorityApplier applier,
        IProcessPriorityMonitor monitor)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _applier = applier ?? throw new ArgumentNullException(nameof(applier));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
    }

    internal int SnapshotCount => _store.Count;

    internal int PruneDeadSnapshots() => _store.PruneDeadSnapshots();

    public OperationResult Apply(string? gameLoopRoot)
    {
        _store.PruneDeadSnapshots();
        var scan = _applier.ApplyToRunningProcesses(gameLoopRoot);
        _monitor.Start(gameLoopRoot);

        if (scan.Candidates == 0)
        {
            return OperationResult.Skip("GameLoop is not running; high-priority monitoring armed for the next emulator launch.");
        }

        if (scan.Changed == 0 && scan.AlreadyHigh == 0)
        {
            if (scan.AccessDenied > 0)
            {
                return OperationResult.Fail($"Access denied by Windows for {scan.AccessDenied} GameLoop process(es); run as administrator, then boost again. {scan.Skipped} process(es) skipped safely.");
            }

            return OperationResult.Fail($"GameLoop processes were found, but Windows did not allow High priority to be applied. {scan.Skipped} process(es) were skipped.");
        }

        var suffix = scan.Skipped > 0 ? $" {scan.Skipped} process(es) were skipped safely." : string.Empty;
        if (scan.AccessDenied > 0)
        {
            suffix += $" {scan.AccessDenied} process(es) need administrator access.";
        }

        return OperationResult.Ok($"GameLoop runtime priority set to High for {scan.Changed + scan.AlreadyHigh}/{scan.Candidates} process(es); monitor active.{suffix}");
    }

    public OperationResult Restore()
    {
        _monitor.Stop();
        return _applier.RestoreSnapshots();
    }

    /// <summary>
    /// Asynchronously stops monitoring and restores saved process priorities.
    /// </summary>
    public async Task<OperationResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        await _monitor.StopMonitorAsync(cancellationToken);
        return _applier.RestoreSnapshots();
    }

    /// <summary>
    /// Signals cancellation and awaits monitor loop termination up to the configured stop timeout.
    /// </summary>
    public Task StopMonitorAsync(CancellationToken cancellationToken = default) =>
        _monitor.StopMonitorAsync(cancellationToken);

    /// <summary>
    /// Test seam to seed snapshot entries without requiring a live process.
    /// </summary>
    internal void AddSnapshotForTesting(
        int processId,
        string? executablePath,
        string processName,
        ProcessPriorityClass priority) =>
        _store.AddSnapshotForTesting(processId, executablePath, processName, priority);
}
