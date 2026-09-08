namespace Nexora.Shared.Kernel;

/// <summary>
/// Execution boundary for external process and PowerShell script execution.
/// </summary>
public interface IProcessRunner
{
    ProcessResult Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null);

    ProcessResult RunPowerShell(string script, TimeSpan? timeout = null);

    bool StartDetachedElevated(string fileName, string arguments = "");
}
