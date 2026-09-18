using System.Diagnostics;
using Nexora.Configuration;

namespace Nexora.Shared.Infrastructure;

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
    /// Resolves the GameLoop root folder from the registry or running processes.
    /// Tier-0 is the validated CustomInstallRoot override, then registry,
    /// running processes, and finally a ProgramFiles traversal.
    /// </summary>
    public string? GetRoot()
    {
        var customRoot = GetValidatedCustomRoot();
        if (customRoot is not null)
        {
            return customRoot;
        }

        var fromRegistry = GetRootFromRegistry();
        if (fromRegistry is not null)
        {
            return fromRegistry;
        }

        // Fall back to active emulator process paths via the shared enumerator
        // (single GetProcessesByName home — see GameLoopProcessEnumerator).
        foreach (var process in GameLoopProcessEnumerator.EnumerateByNames(_emulator.Emulator.RunningCheckProcessNames))
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

        // Last-resort heuristic: standard ProgramFiles roots.
        foreach (var programFiles in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
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
        var customRoot = GetValidatedCustomRoot();
        if (customRoot is not null)
        {
            var fromCustom = NormalizeUiPath(Path.Combine(customRoot, "UI")) ?? NormalizeUiPath(customRoot);
            if (fromCustom is not null) return fromCustom;
        }

        var registryPath = _registry.GetLocalString(_gameLoop.Registry.ValueInstallPath, _gameLoop.Registry.BranchUI);
        var fromRegistry = NormalizeUiPath(registryPath);
        if (fromRegistry is not null) return fromRegistry;

        foreach (var process in GameLoopProcessEnumerator.EnumerateByNames(_emulator.Emulator.RunningCheckProcessNames))
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

        foreach (var programFiles in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
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
        var customRoot = GetValidatedCustomRoot();
        if (customRoot is not null)
        {
            var customMarket = Path.Combine(customRoot, _gameLoop.Registry.BranchAppMarket);
            if (Directory.Exists(customMarket))
            {
                return Path.GetFullPath(customMarket);
            }
        }

        var marketInstallPath = _registry.GetLocalString(_gameLoop.Registry.ValueInstallPath);
        if (!string.IsNullOrWhiteSpace(marketInstallPath) && Directory.Exists(marketInstallPath))
        {
            return marketInstallPath;
        }

        if (_registry.GetLocalString(_gameLoop.Registry.ValueInstallPath, _gameLoop.Registry.BranchUI) is { } ui)
        {
            try
            {
                var derived = Path.Combine(Directory.GetParent(ui)?.FullName ?? string.Empty, _gameLoop.Registry.BranchAppMarket);
                if (!string.IsNullOrWhiteSpace(derived) && Directory.Exists(derived))
                {
                    return Path.GetFullPath(derived);
                }
            }
            catch
            {
                // Ignore invalid registry path formats.
            }
        }

        return null;
    }

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
