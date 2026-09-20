using System.Diagnostics;

namespace Nexora.Features.Performance.Infrastructure;

/// <summary>
/// Thread-safe home for process priority snapshots shared by the applier
/// and the monitor. Registered as a singleton so the whole trio observes
/// the same snapshot state.
/// </summary>
public interface IProcessPrioritySnapshotStore
{
    object SyncRoot { get; }

    int Count { get; }

    bool TryAdd(ProcessPrioritySnapshot snapshot);

    List<ProcessPrioritySnapshot> TakeAll();

    /// <summary>
    /// Removes snapshots for terminated or recycled processes to prevent unbounded dictionary growth.
    /// </summary>
    /// <returns>The number of stale entries removed.</returns>
    int PruneDeadSnapshots();

    /// <summary>
    /// Test seam to seed snapshot entries without requiring a live process.
    /// </summary>
    void AddSnapshotForTesting(
        int processId,
        string? executablePath,
        string processName,
        ProcessPriorityClass priority);
}
