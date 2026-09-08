using System.Diagnostics;
using Nexora.Configuration;
using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

/// <summary>
/// Tunes only verified GameLoop emulator processes and keeps the previous
/// priority so a temporary performance session can be restored safely.
/// </summary>
public sealed class ProcessPriorityService
{
    private readonly object _sync = new();
    private readonly Dictionary<int, ProcessPrioritySnapshot> _snapshots = new();
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;

    internal int SnapshotCount
    {
        get
        {
            lock (_sync)
            {
                return _snapshots.Count;
            }
        }
    }

    public OperationResult Apply(string? gameLoopRoot)
    {
        PruneDeadSnapshots();
        var scan = ApplyToRunningProcesses(gameLoopRoot);
        StartMonitor(gameLoopRoot);

        if (scan.Candidates == 0)
        {
            return OperationResult.Ok("No running GameLoop process was found; High-priority monitoring is ready for the next emulator process.");
        }

        if (scan.Changed == 0 && scan.AlreadyHigh == 0)
        {
            return OperationResult.Fail($"GameLoop processes were found, but Windows did not allow High priority to be applied. {scan.Skipped} process(es) were skipped.");
        }

        var suffix = scan.Skipped > 0 ? $" {scan.Skipped} process(es) were skipped safely." : string.Empty;
        return OperationResult.Ok($"GameLoop runtime priority set to High for {scan.Changed + scan.AlreadyHigh}/{scan.Candidates} process(es); monitor active.{suffix}");
    }

    public OperationResult Restore()
    {
        StopMonitor();
        return RestoreSnapshots();
    }

    /// <summary>
    /// Async shutdown path: awaits the monitor loop's exit instead of
    /// blocking the caller, then restores the saved priorities.
    /// </summary>
    public async Task<OperationResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        await StopMonitorAsync(cancellationToken);
        return RestoreSnapshots();
    }

    private OperationResult RestoreSnapshots()
    {
        List<ProcessPrioritySnapshot> snapshots;
        lock (_sync)
        {
            snapshots = _snapshots.Values.ToList();
            _snapshots.Clear();
        }

        var restored = 0;
        var skipped = 0;
        foreach (var snapshot in snapshots)
        {
            try
            {
                using var process = Process.GetProcessById(snapshot.ProcessId);
                var executablePath = TryGetExecutablePath(process);
                var sameProcess = string.IsNullOrWhiteSpace(snapshot.ExecutablePath)
                    ? string.Equals(process.ProcessName, snapshot.ProcessName, StringComparison.OrdinalIgnoreCase)
                    : PathsEqual(executablePath, snapshot.ExecutablePath);
                if (!sameProcess)
                {
                    skipped++;
                    continue;
                }

                process.PriorityClass = snapshot.Priority;
                restored++;
            }
            catch (ArgumentException)
            {
                skipped++;
            }
            catch (InvalidOperationException)
            {
                skipped++;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                skipped++;
            }
        }

        if (snapshots.Count == 0)
        {
            return OperationResult.Ok("No GameLoop runtime priority changes need restoring.");
        }

        return skipped == 0
            ? OperationResult.Ok($"Restored priority for {restored} GameLoop process(es).")
            : OperationResult.Ok($"Restored priority for {restored} GameLoop process(es); {skipped} process(es) had already exited or changed.");
    }

    private (int Changed, int Candidates, int AlreadyHigh, int Skipped) ApplyToRunningProcesses(string? gameLoopRoot)
    {
        lock (_sync)
        {
            var changed = 0;
            var candidates = 0;
            var alreadyHigh = 0;
            var skipped = 0;

            foreach (var processName in AppConstants.Emulator.PerformanceProcessNames)
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        var executablePath = TryGetExecutablePath(process);
                        if (!IsGameLoopPath(executablePath, gameLoopRoot) && !IsKnownGameLoopProcess(processName))
                        {
                            skipped++;
                            continue;
                        }

                        candidates++;
                        if (!_snapshots.ContainsKey(process.Id))
                        {
                            _snapshots[process.Id] = new ProcessPrioritySnapshot(
                                process.Id,
                                executablePath,
                                process.ProcessName,
                                process.PriorityClass);
                        }

                        // High is the requested GameLoop tuning level. Realtime
                        // is intentionally never used because it can starve Windows.
                        if (process.PriorityClass != ProcessPriorityClass.High)
                        {
                            process.PriorityClass = ProcessPriorityClass.High;
                            changed++;
                        }
                        else
                        {
                            alreadyHigh++;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        skipped++;
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        skipped++;
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            return (changed, candidates, alreadyHigh, skipped);
        }
    }

    private void StartMonitor(string? gameLoopRoot)
    {
        lock (_sync)
        {
            if (_monitorTask is { IsCompleted: false }) return;

            // The previous run finished: release its source before replacing it.
            _monitorCancellation?.Dispose();
            _monitorCancellation = new CancellationTokenSource();
            var cancellation = _monitorCancellation;
            _monitorTask = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(AppConstants.Timeouts.MonitorInterval);
                try
                {
                    while (await timer.WaitForNextTickAsync(cancellation.Token))
                    {
                        PruneDeadSnapshots();
                        ApplyToRunningProcesses(gameLoopRoot);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown of the performance monitor.
                }
            });
        }
    }

    /// <summary>
    /// Best-effort synchronous stop: signals cancellation and releases the
    /// source without blocking on the monitor task. The loop observes the
    /// token and exits on its own.
    /// </summary>
    private void StopMonitor()
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
    /// Clean async stop: signals cancellation and awaits the monitor loop's
    /// exit, giving up after the centralized stop timeout so shutdown
    /// can never hang. Never blocks via <c>Task.Wait()</c>.
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
                timeout.CancelAfter(AppConstants.Timeouts.MonitorStopTimeout);
                await monitor.WaitAsync(timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Stop timeout elapsed while the loop winds down; it exits on its
            // own via the canceled monitor token, so shutdown continues.
        }
        finally
        {
            cancellation?.Dispose();
        }
    }

    /// <summary>
    /// Drops snapshots whose process has exited (or whose PID was recycled
    /// by the OS for a different process). Runs on every monitor tick so a
    /// long session cannot grow the dictionary without bound.
    /// </summary>
    /// <returns>The number of stale entries removed.</returns>
    internal int PruneDeadSnapshots()
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
    /// Test seam for the pruning contract: seeds a snapshot entry without
    /// requiring a live GameLoop process.
    /// </summary>
    internal void AddSnapshotForTesting(
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
            var sameProcess = string.IsNullOrWhiteSpace(snapshot.ExecutablePath)
                ? string.Equals(process.ProcessName, snapshot.ProcessName, StringComparison.OrdinalIgnoreCase)
                : PathsEqual(TryGetExecutablePath(process), snapshot.ExecutablePath);
            return !sameProcess;
        }
        catch (ArgumentException)
        {
            // No process with this PID: it has terminated.
            return true;
        }
        catch (InvalidOperationException)
        {
            // Transient process state; keep the entry for the next tick.
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access denied reading the process; keep the entry.
            return false;
        }
    }

    private static string? TryGetExecutablePath(Process process)
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

    private static bool IsGameLoopPath(string? executablePath, string? gameLoopRoot)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;

        try
        {
            var fullPath = Path.GetFullPath(executablePath);
            if (!string.IsNullOrWhiteSpace(gameLoopRoot))
            {
                var root = Path.GetFullPath(gameLoopRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }

            return fullPath.Contains($"{Path.DirectorySeparatorChar}{AppConstants.Emulator.InstallFolderName}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsKnownGameLoopProcess(string processName)
    {
        // These names are limited to GameLoop's 64-bit emulator/rendering
        // processes. This fallback is used only when Windows denies reading
        // MainModule.FileName for an elevated process.
        return AppConstants.Emulator.PerformanceProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string? left, string? right)
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

    private sealed record ProcessPrioritySnapshot(
        int ProcessId,
        string? ExecutablePath,
        string ProcessName,
        ProcessPriorityClass Priority);
}
