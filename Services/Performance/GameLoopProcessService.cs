using System.Diagnostics;
using Nexora.Configuration;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

/// <summary>
/// Discovers, validates, and terminates GameLoop emulator processes safely.
/// </summary>
public sealed class GameLoopProcessService
{
    private readonly ProcessRunner _runner;
    private readonly RegistryService _registry;

    public GameLoopProcessService(ProcessRunner runner, RegistryService registry)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Asynchronously terminates all running GameLoop emulator processes with cancellation support.
    /// </summary>
    public Task<OperationResult> KillGameLoopProcessesAsync(CancellationToken cancellationToken) =>
        Task.Run(() => KillGameLoopProcesses(cancellationToken), cancellationToken);

    /// <summary>
    /// Terminates all running GameLoop emulator processes using taskkill.exe.
    /// Throws OperationCanceledException if the cancellation token is triggered.
    /// </summary>
    public OperationResult KillGameLoopProcesses(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gameLoopRoot = GetGameLoopRoot();
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
                    AppConstants.Timeouts.TaskkillTimeout);

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
    /// Scans the system for processes matching GameLoop emulator image names and verifies their file paths.
    /// </summary>
    public List<Process> FindGameLoopProcesses(string? gameLoopRoot = null)
    {
        gameLoopRoot ??= GetGameLoopRoot();
        var candidates = new List<Process>();

        foreach (var imageName in AppConstants.Emulator.ProcessImageNames)
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(imageName)))
            {
                var isGameLoopProcess = false;
                try
                {
                    var executablePath = process.MainModule?.FileName;
                    isGameLoopProcess = IsGameLoopPath(executablePath, gameLoopRoot) ||
                        (string.IsNullOrWhiteSpace(executablePath) && AppConstants.Emulator.SafeFallbackImageNames.Contains(imageName, StringComparer.OrdinalIgnoreCase));
                }
                catch
                {
                    // The app runs elevated, but protected processes can still
                    // deny path access. Only use the fallback for emulator-only
                    // names; never terminate a generic Windows process by name.
                    isGameLoopProcess = AppConstants.Emulator.SafeFallbackImageNames.Contains(imageName, StringComparer.OrdinalIgnoreCase);
                }

                if (isGameLoopProcess)
                {
                    candidates.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }
        }

        return candidates
            .GroupBy(process => process.Id)
            .Select(group => group.First())
            .ToList();
    }

    /// <summary>
    /// Resolves the root folder of the GameLoop installation from the registry.
    /// </summary>
    public string? GetGameLoopRoot()
    {
        var installPath = _registry.GetLocalString(AppConstants.Registry.ValueInstallPath, AppConstants.Registry.BranchUI);
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }

        try
        {
            return Directory.GetParent(Path.GetFullPath(installPath))?.FullName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Determines whether an executable path belongs to the GameLoop installation directory.
    /// </summary>
    public static bool IsGameLoopPath(string? executablePath, string? gameLoopRoot)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(executablePath);
            if (!string.IsNullOrWhiteSpace(gameLoopRoot))
            {
                var root = Path.GetFullPath(gameLoopRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return fullPath.Contains($"{Path.DirectorySeparatorChar}{AppConstants.Emulator.InstallFolderName}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
