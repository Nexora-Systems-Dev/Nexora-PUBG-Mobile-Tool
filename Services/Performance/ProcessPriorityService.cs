using System.Diagnostics;
using Nexora.Configuration;
using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

/// <summary>
/// Sets and restores process priority for verified GameLoop emulator processes.
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
        StopMonitor();
        return RestoreSnapshots();
    }

    /// <summary>
    /// Asynchronously stops monitoring and restores saved process priorities.
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

    private (int Changed, int Candidates, int AlreadyHigh, int Skipped, int AccessDenied) ApplyToRunningProcesses(string? gameLoopRoot)
    {
        lock (_sync)
        {
            var changed = 0;
            var candidates = 0;
            var alreadyHigh = 0;
            var skipped = 0;
            var accessDenied = 0;

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

                        // Use High priority; Realtime is avoided to prevent starving system threads.
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
                        // Access denied (process owned by an elevated GameLoop instance).
                        // Count separately so the caller can report elevation guidance.
                        accessDenied++;
                        skipped++;
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            return (changed, candidates, alreadyHigh, skipped, accessDenied);
        }
    }

    private void StartMonitor(string? gameLoopRoot)
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
                    // Normal cancellation on monitor stop.
                }
            });
        }
    }

    /// <summary>
    /// Signals the monitor loop to stop without awaiting its completion.
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
                timeout.CancelAfter(AppConstants.Timeouts.MonitorStopTimeout);
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
    /// Removes snapshots for terminated or recycled processes to prevent unbounded dictionary growth.
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
    /// Test seam to seed snapshot entries without requiring a live process.
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
        // Fallback for when MainModule access is denied on elevated processes.
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
