using System.Diagnostics;

namespace Nexora.Features.Performance.Infrastructure;

public sealed record ProcessPrioritySnapshot(
    int ProcessId,
    string? ExecutablePath,
    string ProcessName,
    ProcessPriorityClass Priority);

/// <summary>
/// Thread-safe home for process priority snapshots. All dictionary access is
/// serialized on an internal lock; the same lock object is shared with
/// <see cref="ProcessPriorityApplier"/> so scans keep the original
/// whole-scan mutual exclusion.
/// </summary>
public sealed class ProcessPrioritySnapshotStore : IProcessPrioritySnapshotStore
{
    private readonly object _sync = new();
    private readonly Dictionary<int, ProcessPrioritySnapshot> _snapshots = new();

    public object SyncRoot => _sync;

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _snapshots.Count;
            }
        }
    }

    public bool TryAdd(ProcessPrioritySnapshot snapshot)
    {
        lock (_sync)
        {
            if (_snapshots.ContainsKey(snapshot.ProcessId))
            {
                return false;
            }

            _snapshots[snapshot.ProcessId] = snapshot;
            return true;
        }
    }

    public List<ProcessPrioritySnapshot> TakeAll()
    {
        lock (_sync)
        {
            var snapshots = _snapshots.Values.ToList();
            _snapshots.Clear();
            return snapshots;
        }
    }

    /// <summary>
    /// Removes snapshots for terminated or recycled processes to prevent unbounded dictionary growth.
    /// </summary>
    /// <returns>The number of stale entries removed.</returns>
    public int PruneDeadSnapshots()
    {
        List<int>? dead = null;
        lock (_sync)
        {
            foreach (var (processId, snapshot) in _snapshots)
            {
                if (IsSnapshotStale(snapshot))
                {
                    dead ??= new List<int>();
                    dead.Add(processId);
                }
            }

            if (dead is null)
            {
                return 0;
            }

            foreach (var processId in dead)
            {
                _snapshots.Remove(processId);
            }

            return dead.Count;
        }
    }

    /// <summary>
    /// Test seam to seed snapshot entries without requiring a live process.
    /// </summary>
    public void AddSnapshotForTesting(
        int processId,
        string? executablePath,
        string processName,
        ProcessPriorityClass priority)
    {
        lock (_sync)
        {
            _snapshots[processId] = new ProcessPrioritySnapshot(processId, executablePath, processName, priority);
        }
    }

    private static bool IsSnapshotStale(ProcessPrioritySnapshot snapshot)
    {
        try
        {
            using var process = Process.GetProcessById(snapshot.ProcessId);
            var currentPath = TryGetExecutablePath(process);

            bool sameProcess;
            if (string.IsNullOrWhiteSpace(snapshot.ExecutablePath) || string.IsNullOrWhiteSpace(currentPath))
            {
                // Fall back to process name comparison if executable path access is denied.
                sameProcess = string.Equals(process.ProcessName, snapshot.ProcessName, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                sameProcess = PathsEqual(currentPath, snapshot.ExecutablePath);
            }

            return !sameProcess;
        }
        catch (ArgumentException)
        {
            // Process terminated.
            return true;
        }
        catch (InvalidOperationException)
        {
            // Process exited during query or transient state.
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access denied; keep snapshot.
            return false;
        }
    }

    internal static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    internal static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
