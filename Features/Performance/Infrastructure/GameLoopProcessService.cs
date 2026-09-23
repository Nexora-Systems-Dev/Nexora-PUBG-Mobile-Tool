using System.Diagnostics;
using Nexora.Configuration;
using Nexora.Shared.Kernel;
using Nexora.Features.Performance.Application;
using Nexora.Infrastructure.GameLoop;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Performance.Infrastructure;

/// <summary>
/// Discovers, validates, and terminates GameLoop emulator processes.
/// </summary>
public sealed class GameLoopProcessService : IGameLoopProcessService
{
    private readonly IProcessRunner _runner;
    private readonly IGameLoopPathResolver _paths;
    private readonly EmulatorOptions _emulator;
    private readonly GameLoopOptions _gameLoop;

    public GameLoopProcessService(IProcessRunner runner, IGameLoopPathResolver pathResolver, EmulatorOptions? emulator = null, GameLoopOptions? gameLoop = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _paths = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _emulator = emulator ?? new EmulatorOptions();
        _gameLoop = gameLoop ?? new GameLoopOptions();
    }

    /// <summary>
    /// Terminates running GameLoop emulator processes using taskkill.
    /// </summary>
    public OperationResult KillGameLoopProcesses(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gameLoopRoot = _paths.GetRoot();
        var candidates = FindGameLoopProcesses(gameLoopRoot);
        if (candidates.Count == 0)
        {
            return OperationResult.Ok("No GameLoop processes were found.");
        }

        var ended = 0;
        foreach (var process in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = _runner.Run(
                    AppConstants.Tools.TaskkillFileName,
                    new[] { "/PID", process.Id.ToString(), "/T", "/F" },
                    _gameLoop.Timeouts.TaskkillTimeout);

                if (result.Succeeded)
                {
                    ended++;
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        var remaining = FindGameLoopProcesses(gameLoopRoot);
        if (remaining.Count > 0)
        {
            var names = string.Join(", ", remaining.Select(process => $"{process.ProcessName}.exe").Distinct(StringComparer.OrdinalIgnoreCase));
            foreach (var process in remaining)
            {
                process.Dispose();
            }

            return OperationResult.Fail($"GameLoop force close could not end: {names}.");
        }

        return OperationResult.Ok($"GameLoop force close completed. {ended} process(es) ended.");
    }

    /// <summary>
    /// Finds processes matching GameLoop emulator image names and verifies their file paths.
    /// </summary>
    public List<Process> FindGameLoopProcesses(string? gameLoopRoot = null)
    {
        gameLoopRoot ??= _paths.GetRoot();

        var candidates = new List<Process>();
        foreach (var process in GameLoopProcessEnumerator.EnumerateByNames(_emulator.Emulator.ProcessImageNames))
        {
            // A process that exits mid-scan has no readable name and is dropped here.
            if (ReadImageName(process) is { } imageName && IsGameLoopProcess(process, imageName, gameLoopRoot))
            {
                candidates.Add(process);
            }
            else
            {
                process.Dispose();
            }
        }

        return DeduplicateByProcessId(candidates);
    }

    /// <summary>The image name with its extension, or null when the process exited mid-scan.</summary>
    private static string? ReadImageName(Process process)
    {
        try
        {
            return process.ProcessName + ".exe";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Verifies a candidate image lives under the GameLoop root. Path access is
    /// denied on protected processes, so a blank path falls back to the known
    /// safe image-name list rather than rejecting a genuine emulator process.
    /// </summary>
    private bool IsGameLoopProcess(Process process, string imageName, string? gameLoopRoot)
    {
        try
        {
            var executablePath = process.MainModule?.FileName;
            return _paths.IsGameLoopPath(executablePath, gameLoopRoot) ||
                (string.IsNullOrWhiteSpace(executablePath) && _emulator.Emulator.SafeFallbackImageNames.Contains(imageName, StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            return _emulator.Emulator.SafeFallbackImageNames.Contains(imageName, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Enumerating can yield the same PID more than once; the first occurrence
    /// wins and duplicates are disposed so no handle leaks out of the scan.
    /// </summary>
    private static List<Process> DeduplicateByProcessId(List<Process> candidates)
    {
        var unique = new List<Process>();
        var seenIds = new HashSet<int>();
        foreach (var process in candidates)
        {
            if (seenIds.Add(process.Id))
            {
                unique.Add(process);
            }
            else
            {
                process.Dispose();
            }
        }

        return unique;
    }

    /// <summary>
    /// Resolves the GameLoop root folder using only the registry to avoid path spoofing.
    /// </summary>
    public string? GetGameLoopRootFromRegistry() => _paths.GetRootFromRegistry();

    /// <summary>
    /// Resolves the GameLoop root folder from the registry or running processes.
    /// </summary>
    public string? GetGameLoopRoot() => _paths.GetRoot();

    /// <summary>
    /// Resolves the directory containing GameLoop 64-bit executables.
    /// </summary>
    public string? GetGameLoopUiPath() => _paths.GetUiPath();
}
