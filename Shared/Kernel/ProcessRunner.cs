using System.Diagnostics;
using Nexora.Configuration;

namespace Nexora.Shared.Kernel;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}

public sealed class ProcessRunner : IProcessRunner
{
    public ProcessResult Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                return new ProcessResult(-1, string.Empty, "Process did not start.", false);
            }

            var waitTime = timeout ?? AppConstants.Timeouts.DefaultProcessTimeout;
            if (!process.WaitForExit((int)waitTime.TotalMilliseconds))
            {
                try { process.Kill(true); } catch { /* Ignore if already exited. */ }
                return new ProcessResult(-1, string.Empty, "Process timed out.", true);
            }

            return new ProcessResult(
                process.ExitCode,
                process.StandardOutput.ReadToEnd(),
                process.StandardError.ReadToEnd(),
                false);
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, string.Empty, ex.Message, false);
        }
    }

    public ProcessResult RunPowerShell(string script, TimeSpan? timeout = null)
    {
        return Run(
            "powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script },
            timeout);
    }

    /// <summary>
    /// Launches a detached external process with Windows administrator elevation.
    /// </summary>
    public bool StartDetachedElevated(string fileName, string? arguments = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                startInfo.Arguments = arguments;
            }

            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch
        {
            return false;
        }
    }
}
