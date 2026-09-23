using System.Diagnostics;
using Nexora.Configuration;
using Nexora.Infrastructure.Registry;

namespace Nexora.Infrastructure.GameLoop;

/// <summary>
/// Resolves GameLoop emulator installation paths using the
/// custom-override → registry → running-process → ProgramFiles discovery pipeline.
/// Fails loudly with null when nothing resolves — never guesses a drive.
/// </summary>
public sealed class GameLoopPathResolver : IGameLoopPathResolver
{
    private readonly IMachineRegistry _registry;
    private readonly GameLoopOptions _gameLoop;
    private readonly EmulatorOptions _emulator;

    public GameLoopPathResolver(IMachineRegistry registry, GameLoopOptions? gameLoop = null, EmulatorOptions? emulator = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _gameLoop = gameLoop ?? new GameLoopOptions();
        _emulator = emulator ?? new EmulatorOptions();
    }

    /// <summary>
    /// Resolves the GameLoop root folder using only the registry to avoid path spoofing.
    /// </summary>
    public string? GetRootFromRegistry()
    {
        var installPath = _registry.GetLocalString(_gameLoop.Registry.ValueInstallPath, _gameLoop.Registry.BranchUI);
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
    /// Resolves the GameLoop root folder. Tier-0 is the validated
    /// CustomInstallRoot override, then registry, running processes, and
    /// finally a ProgramFiles traversal.
    /// </summary>
    public string? GetRoot() =>
        GetValidatedCustomRoot()
            ?? GetRootFromRegistry()
            ?? GetRootFromRunningProcesses()
            ?? GetRootFromProgramFiles();

    // Tier: the parent of a running emulator executable's directory — the
    // install root it was actually launched from.
    private string? GetRootFromRunningProcesses()
    {
        foreach (var executableDir in EnumerateRunningProcessDirectories())
        {
            var parent = TryGetParent(executableDir);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                return parent;
            }
        }

        return null;
    }

    // Tier: last-resort heuristic over the standard ProgramFiles roots.
    private string? GetRootFromProgramFiles()
    {
        foreach (var programFiles in StandardProgramFilesRoots())
        {
            try
            {
                var candidate = Path.Combine(programFiles, _emulator.Emulator.InstallFolderName);
                if (Directory.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
                // Ignore invalid ProgramFiles paths.
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the directory containing GameLoop 64-bit executables.
    /// </summary>
    public string? GetUiPath()
    {
        if (GetValidatedCustomRoot() is { } customRoot)
        {
            var fromCustom = NormalizeUiPath(Path.Combine(customRoot, "UI")) ?? NormalizeUiPath(customRoot);
            if (fromCustom is not null) return fromCustom;
        }

        var registryPath = _registry.GetLocalString(_gameLoop.Registry.ValueInstallPath, _gameLoop.Registry.BranchUI);
        if (NormalizeUiPath(registryPath) is { } fromRegistry) return fromRegistry;

        return GetUiPathFromRunningProcesses() ?? GetUiPathFromProgramFiles();
    }

    // Tier: the directory of a running emulator executable, normalized to UI.
    private string? GetUiPathFromRunningProcesses() =>
        EnumerateRunningProcessDirectories()
            .Select(NormalizeUiPath)
            .FirstOrDefault(path => path is not null);

    // Tier: each standard ProgramFiles root, looking for <install>\UI.
    private string? GetUiPathFromProgramFiles()
    {
        foreach (var programFiles in StandardProgramFilesRoots())
        {
            var standardPath = NormalizeUiPath(Path.Combine(programFiles, _emulator.Emulator.InstallFolderName, "UI"));
            if (standardPath is not null) return standardPath;
        }

        return null;
    }

    /// <summary>
    /// Resolves the GameLoop AppMarket directory: custom override first, then the
    /// registry AppMarket branch, deriving it from the UI branch when absent.
    /// Derived paths are returned only when they exist on disk — never a guess.
    /// </summary>
    public string? GetAppMarketPath()
    {
        var custom = MarketUnder(GetValidatedCustomRoot());
        if (custom is not null) return custom;

        var marketInstallPath = _registry.GetLocalString(_gameLoop.Registry.ValueInstallPath);
        if (!string.IsNullOrWhiteSpace(marketInstallPath) && Directory.Exists(marketInstallPath))
        {
            return marketInstallPath;
        }

        var ui = _registry.GetLocalString(_gameLoop.Registry.ValueInstallPath, _gameLoop.Registry.BranchUI);
        return ui is null ? null : MarketUnder(TryGetParent(ui));
    }

    /// <summary>
    /// The AppMarket folder under an install root, or null when the root is
    /// blank or holds no such folder. Raw registry roots can be malformed, so
    /// a bad path resolves to null instead of throwing.
    /// </summary>
    private string? MarketUnder(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;

        try
        {
            var market = Path.Combine(root, _gameLoop.Registry.BranchAppMarket);
            return Directory.Exists(market) ? Path.GetFullPath(market) : null;
        }
        catch
        {
            // Ignore invalid registry path formats.
            return null;
        }
    }

    /// <summary>
    /// Directories of running emulator executables, via the shared enumerator
    /// (single <c>Process.GetProcessesByName</c> home — see
    /// <see cref="GameLoopProcessEnumerator"/>). Protected processes that deny
    /// module access are skipped; every handle is disposed after reading.
    /// </summary>
    private IEnumerable<string> EnumerateRunningProcessDirectories()
    {
        foreach (var process in GameLoopProcessEnumerator.EnumerateByNames(_emulator.Emulator.RunningCheckProcessNames))
        {
            var directory = TryGetProcessDirectory(process);
            process.Dispose();
            if (directory is not null) yield return directory;
        }
    }

    /// <summary>
    /// The directory of a process's main module, or null when the process
    /// denies module access (protected or cross-architecture processes).
    /// </summary>
    private static string? TryGetProcessDirectory(Process process)
    {
        try
        {
            var modulePath = process.MainModule?.FileName;
            return string.IsNullOrWhiteSpace(modulePath) ? null : Path.GetDirectoryName(modulePath);
        }
        catch
        {
            // Ignore protected processes.
            return null;
        }
    }

    /// <summary>
    /// The parent directory of a path, or null when the path is malformed.
    /// </summary>
    private static string? TryGetParent(string path)
    {
        try
        {
            return Directory.GetParent(path)?.FullName;
        }
        catch
        {
            // Ignore invalid path formats.
            return null;
        }
    }

    /// <summary>
    /// The standard per-machine install roots, de-duplicated with blank
    /// entries filtered out (the sequence can legitimately be empty).
    /// </summary>
    private static IEnumerable<string> StandardProgramFilesRoots() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
    }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the validated custom install root, or null when unset or missing on disk.
    /// Explicit user override wins over every automatic tier, but a stale
    /// (uninstalled/moved) value never resolves — callers fall through loudly.
    /// </summary>
    private string? GetValidatedCustomRoot()
    {
        var custom = _emulator.Emulator.CustomInstallRoot;
        if (string.IsNullOrWhiteSpace(custom))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(custom.Trim());
            return Directory.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
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
    public bool IsGameLoopPath(string? executablePath, string? gameLoopRoot)
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

            return fullPath.Contains($"{Path.DirectorySeparatorChar}{_emulator.Emulator.InstallFolderName}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
