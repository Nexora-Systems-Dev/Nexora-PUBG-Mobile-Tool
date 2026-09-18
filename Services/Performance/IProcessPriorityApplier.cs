using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

/// <summary>
/// Applies High priority to verified GameLoop processes and restores saved priorities.
/// </summary>
public interface IProcessPriorityApplier
{
    (int Changed, int Candidates, int AlreadyHigh, int Skipped, int AccessDenied) ApplyToRunningProcesses(string? gameLoopRoot);

    OperationResult RestoreSnapshots();
}
