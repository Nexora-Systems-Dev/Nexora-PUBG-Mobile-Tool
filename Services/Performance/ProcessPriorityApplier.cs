using System.Diagnostics;
using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

/// <summary>
/// Applies High priority to verified GameLoop processes and restores saved priorities.
/// </summary>
public sealed class ProcessPriorityApplier : IProcessPriorityApplier
{
    private readonly IProcessPrioritySnapshotStore _store;
    private readonly IGameLoopProcessService _processes;

    public ProcessPriorityApplier(IProcessPrioritySnapshotStore store, IGameLoopProcessService processes)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
    }

    public (int Changed, int Candidates, int AlreadyHigh, int Skipped, int AccessDenied) ApplyToRunningProcesses(string? gameLoopRoot)
    {
        lock (_store.SyncRoot)
        {
            var changed = 0;
            var candidates = 0;
            var alreadyHigh = 0;
            var skipped = 0;
            var accessDenied = 0;

            // Candidates come pre-verified from the process service (install-path
            // check with a safe-name fallback for inaccessible paths), so every
            // process here is a candidate — no per-process trust decision left.
            foreach (var process in _processes.FindGameLoopProcesses(gameLoopRoot))
            {
                try
                {
                    var executablePath = ProcessPrioritySnapshotStore.TryGetExecutablePath(process);

                    candidates++;
                    _store.TryAdd(new ProcessPrioritySnapshot(
                        process.Id,
                        executablePath,
                        process.ProcessName,
                        process.PriorityClass));

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

            return (changed, candidates, alreadyHigh, skipped, accessDenied);
        }
    }

    public OperationResult RestoreSnapshots()
    {
        var snapshots = _store.TakeAll();

        var restored = 0;
        var skipped = 0;
        foreach (var snapshot in snapshots)
        {
            try
            {
                using var process = Process.GetProcessById(snapshot.ProcessId);
                var executablePath = ProcessPrioritySnapshotStore.TryGetExecutablePath(process);
                var sameProcess = string.IsNullOrWhiteSpace(snapshot.ExecutablePath)
                    ? string.Equals(process.ProcessName, snapshot.ProcessName, StringComparison.OrdinalIgnoreCase)
                    : ProcessPrioritySnapshotStore.PathsEqual(executablePath, snapshot.ExecutablePath);
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
}
