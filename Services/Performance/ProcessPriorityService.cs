using System.Diagnostics;

using Nexora.Models;

namespace Nexora.Services.Performance;

/// <summary>
/// Tunes only verified GameLoop emulator processes and keeps the previous
/// priority so a temporary performance session can be restored safely.
/// </summary>
public sealed class ProcessPriorityService
{
    private static readonly string[] PerformanceProcessNames =
    {
        "aow_exe", "AndroidEmulatorEn", "AndroidEmulatorEx", "AndroidEmulator", "AndroidRenderer"
    };

    private readonly object _sync = new();
    private readonly Dictionary<int, ProcessPrioritySnapshot> _snapshots = new();
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;

    public OperationResult Apply(string? gameLoopRoot)
    {
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

            foreach (var processName in PerformanceProcessNames)
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

            _monitorCancellation = new CancellationTokenSource();
            var cancellation = _monitorCancellation;
            _monitorTask = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
                try
                {
                    while (await timer.WaitForNextTickAsync(cancellation.Token))
                    {
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

    private void StopMonitor()
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

        if (monitor is not null)
        {
            try { monitor.Wait(TimeSpan.FromSeconds(2)); } catch { /* best effort during shutdown */ }
        }

        cancellation?.Dispose();
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

            return fullPath.Contains($"{Path.DirectorySeparatorChar}TxGameAssistant{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
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
        return PerformanceProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase);
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
