namespace Nexora.Shared.Kernel;
using Nexora.Infrastructure.Processes;

/// <summary>
/// Execution boundary for external processes and PowerShell scripts.
/// </summary>
public interface IProcessRunner
{
    ProcessResult Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null);

    ProcessResult RunPowerShell(string script, TimeSpan? timeout = null);

    bool StartDetachedElevated(string fileName, string arguments = "");
}
