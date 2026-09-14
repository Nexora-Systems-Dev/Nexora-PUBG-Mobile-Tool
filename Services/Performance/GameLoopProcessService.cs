using System.Diagnostics;
using Nexora.Configuration;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

/// <summary>
/// Discovers, validates, and terminates GameLoop emulator processes.
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
    /// Terminates running GameLoop emulator processes asynchronously.
    /// </summary>
    public Task<OperationResult> KillGameLoopProcessesAsync(CancellationToken cancellationToken) =>
        Task.Run(() => KillGameLoopProcesses(cancellationToken), cancellationToken);

    /// <summary>
    /// Terminates running GameLoop emulator processes using taskkill.
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
    /// Finds processes matching GameLoop emulator image names and verifies their file paths.
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
                    // Process path access can be denied on protected processes; fall back to known safe image names.
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
    public string? GetGameLoopRootFromRegistry()
    {
        var installPath = _registry.GetLocalString(AppConstants.Registry.ValueInstallPath, AppConstants.Registry.BranchUI);
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }

        try
        {
            var root = Directory.GetParent(Path.GetFullPath(installPath))?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                return root;
            }
        }
        catch
        {
            // Ignore invalid registry path formats.
        }

        return null;
    }

    /// <summary>
    /// Resolves the GameLoop root folder from the registry or running processes.
    /// </summary>
    public string? GetGameLoopRoot()
    {
        var installPath = _registry.GetLocalString(AppConstants.Registry.ValueInstallPath, AppConstants.Registry.BranchUI);
        if (!string.IsNullOrWhiteSpace(installPath))
        {
            try
            {
                var root = Directory.GetParent(Path.GetFullPath(installPath))?.FullName;
                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                {
                    return root;
                }
            }
            catch
            {
                // Ignore invalid path formats.
            }
        }

        // Fall back to active emulator process paths.
        foreach (var name in AppConstants.Emulator.RunningCheckProcessNames)
        {
            try
            {
                var procs = Process.GetProcessesByName(name);
                foreach (var process in procs)
                {
                    try
                    {
                        var modPath = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(modPath))
                        {
                            var uiDir = Path.GetDirectoryName(modPath);
                            if (!string.IsNullOrWhiteSpace(uiDir))
                            {
                                var parent = Directory.GetParent(uiDir)?.FullName;
                                if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                                {
                                    return parent;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore protected processes.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                // Ignore enumeration errors.
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the directory containing GameLoop 64-bit executables.
    /// </summary>
    public string? GetGameLoopUiPath()
    {
        var registryPath = _registry.GetLocalString(AppConstants.Registry.ValueInstallPath, AppConstants.Registry.BranchUI);
        var fromRegistry = NormalizeUiPath(registryPath);
        if (fromRegistry is not null) return fromRegistry;

        foreach (var name in AppConstants.Emulator.RunningCheckProcessNames)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        var processDirectory = Path.GetDirectoryName(process.MainModule?.FileName);
                        var fromProcess = NormalizeUiPath(processDirectory);
                        if (fromProcess is not null) return fromProcess;
                    }
                    catch
                    {
                        // Ignore protected processes.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                // Ignore enumeration errors.
            }
        }

        foreach (var programFiles in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var standardPath = NormalizeUiPath(Path.Combine(programFiles, AppConstants.Emulator.InstallFolderName, "UI"));
            if (standardPath is not null) return standardPath;
        }

        return null;
    }

    private static string? NormalizeUiPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            var fullPath = Path.GetFullPath(path.Trim());
            if (File.Exists(fullPath)) fullPath = Path.GetDirectoryName(fullPath)!;
            if (!Directory.Exists(fullPath)) return null;

            var uiPath = Path.GetFileName(fullPath).Equals("UI", StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : Path.Combine(fullPath, "UI");
            return Directory.Exists(uiPath) ? uiPath : fullPath;
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
