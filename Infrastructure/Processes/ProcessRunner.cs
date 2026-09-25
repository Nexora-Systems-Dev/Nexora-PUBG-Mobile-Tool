using System.Diagnostics;
using System.Text;
using Nexora.Configuration;
using Nexora.Shared.Kernel;

namespace Nexora.Infrastructure.Processes;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}

public sealed class ProcessRunner : IProcessRunner
{
    private readonly GameLoopOptions _gameLoop;

    public ProcessRunner(GameLoopOptions? gameLoop = null)
    {
        _gameLoop = gameLoop ?? new GameLoopOptions();
    }

    private static ProcessStartInfo HiddenRedirectedStartInfo(string fileName) => new()
    {
        FileName = fileName,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        WorkingDirectory = AppContext.BaseDirectory
    };

    public ProcessResult Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        using var process = new Process { StartInfo = HiddenRedirectedStartInfo(fileName) };

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

            // U-06: drain BOTH pipes before the wait. A child emitting more
            // than the OS pipe buffer (~64 KB) would otherwise block forever
            // on a full pipe while we sit in WaitForExit, then be misreported
            // as TimedOut and killed. Event handlers plus Begin read calls
            // keep the pipes draining with no Task objects involved, so the
            // ArchitectureGuard ban on blocking task waits cannot trigger;
            // the parameterless WaitForExit after the timed wait flushes the
            // handlers before we snapshot the builders.
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) stdout.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) stderr.AppendLine(e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var waitTime = timeout ?? _gameLoop.Timeouts.DefaultProcessTimeout;
            if (!process.WaitForExit((int)waitTime.TotalMilliseconds))
            {
                try { process.Kill(true); } catch { /* Ignore if already exited. */ }
                return new ProcessResult(-1, string.Empty, "Process timed out.", true);
            }

            process.WaitForExit();

            return new ProcessResult(
                process.ExitCode,
                stdout.ToString(),
                stderr.ToString(),
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
    /// True-async twin of <see cref="Run"/>: stream drains start before the
    /// wait (so a chatty child can never deadlock a full pipe), the wait
    /// honors the linked timeout/caller token, and the tree is killed on
    /// either. Timeout keeps <see cref="Run"/>'s TimedOut result; external
    /// cancellation rethrows so callers settle it as a canceled result.
    /// </summary>
    public async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = HiddenRedirectedStartInfo(fileName), EnableRaisingEvents = true };

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

            var waitTime = timeout ?? _gameLoop.Timeouts.DefaultProcessTimeout;
            using var timeoutCts = new CancellationTokenSource(waitTime);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { /* Ignore if already exited. */ }
                if (cancellationToken.IsCancellationRequested) throw;
                return new ProcessResult(-1, string.Empty, "Process timed out.", true);
            }

            return new ProcessResult(
                process.ExitCode,
                await stdoutTask,
                await stderrTask,
                false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ProcessResult(-1, string.Empty, ex.Message, false);
        }
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
