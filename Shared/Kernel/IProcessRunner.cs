namespace Nexora.Shared.Kernel;
using Nexora.Infrastructure.Processes;

/// <summary>
/// Execution boundary for external processes and PowerShell scripts.
/// </summary>
public interface IProcessRunner
{
    ProcessResult Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null);

    /// <summary>
    /// True-async twin of <see cref="Run"/> for UI-context callers: the wait
    /// honors the token (killing the process tree on cancel) and never blocks
    /// the calling thread. A timeout keeps the historical TimedOut result;
    /// external cancellation rethrows <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, TimeSpan? timeout, CancellationToken cancellationToken);

    ProcessResult RunPowerShell(string script, TimeSpan? timeout = null);

    bool StartDetachedElevated(string fileName, string arguments = "");
}
