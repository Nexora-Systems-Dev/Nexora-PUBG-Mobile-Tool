using Nexora.Configuration;
using Nexora.Features.GameLoop;
using Nexora.Features.Performance;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

/// <summary>
/// Configures GameLoop emulator registry settings, hardware-guided smart settings,
/// and Image File Execution Options (IFEO) CPU priorities.
/// </summary>
public sealed class GameLoopRegistryOptimizer
{
    private readonly IUserRegistry _userRegistry;
    private readonly IMachineRegistry _machineRegistry;
    private readonly GpuRoutingService _gpuRouting;
    private readonly IGameLoopProcessService _processService;
    private readonly GameLoopOptions _gameLoop;
    private readonly EmulatorOptions _emulator;

    public GameLoopRegistryOptimizer(IUserRegistry userRegistry, IMachineRegistry machineRegistry, IGameLoopProcessService processService, GpuRoutingService? gpuRouting = null, GameLoopOptions? gameLoop = null, EmulatorOptions? emulator = null)
    {
        _userRegistry = userRegistry ?? throw new ArgumentNullException(nameof(userRegistry));
        _machineRegistry = machineRegistry ?? throw new ArgumentNullException(nameof(machineRegistry));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _gpuRouting = gpuRouting ?? new GpuRoutingService(userRegistry, emulator);
        _gameLoop = gameLoop ?? new GameLoopOptions();
        _emulator = emulator ?? new EmulatorOptions();
    }

    /// <summary>
    /// Applies hardware-guided smart settings to GameLoop emulator registry keys.
    /// </summary>
    public OperationResult ApplySmartSettings(HardwareSnapshot hardware, OptimizerPlan plan)
    {
        try
        {
            var settings = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["VSyncEnabled"] = hardware.RefreshRateHz < 89 ? 1 : 0,
                ["GraphicsCardEnabled"] = 1,
                ["SetGraphicsCard"] = 1,
                ["VMDPI"] = plan.ContentScale == 1 ? 240 : 480,
                ["FxaaQuality"] = plan.FxaaQuality,
                ["LocalShaderCacheEnabled"] = plan.EnableLocalShaderCache ? 1 : 0,
                ["ShaderCacheEnabled"] = plan.EnableGlobalShaderCache ? 1 : 0,
                ["RenderOptimizeEnabled"] = 1,
                [_gameLoop.Registry.ValueAdbDisable] = 0,
                ["VMMemorySizeInMB"] = plan.EmulatorMemoryMb,
                ["VMCpuCount"] = plan.EmulatorCpuCores
            };

            foreach (var setting in settings)
            {
                if (!SetUserRegistryDword(setting.Key, setting.Value) || _userRegistry.GetUserDword(setting.Key) != setting.Value)
                {
                    return OperationResult.Fail($"Could not verify GameLoop setting: {setting.Key}.");
                }
            }

            // Enable local shader cache; leave global cache disabled for compatibility.
            var isLowEndProfile = plan.ContentScale == 1 && plan.FxaaQuality == 0;
            if (!ApplyRenderScaleAndQuality(plan.ContentScale, isLowEndProfile))
            {
                return OperationResult.Fail("Could not verify the PUBG render profile settings.");
            }

            return OperationResult.Ok($"Smart settings applied for {plan.Tier} hardware. 120 FPS mode is ready when supported by GameLoop/PUBG.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Smart settings could not finish: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies GPU routing, CPU priority IFEO keys, and compatibility flags to GameLoop executables.
    /// </summary>
    public OperationResult OptimizeGameLoopRegistry()
    {
        var installPath = _processService.GetGameLoopUiPath();
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return OperationResult.Fail("GameLoop installation path was not found.");
        }

        var registryKeys = _emulator.Emulator.RegistryImageNames;
        var gpuRouting = _gpuRouting.ApplyHighPerformance(installPath, registryKeys);

        var registryChanged = false;
        var cpuPriorityFailure = TryApplyCpuPriorityKeys(registryKeys, gpuRouting, ref registryChanged);
        if (cpuPriorityFailure is not null) return cpuPriorityFailure;

        var appCompatFailure = TryApplyAppCompatFlags(installPath, registryKeys, ref registryChanged);
        if (appCompatFailure is not null) return appCompatFailure;

        if (!gpuRouting.Success)
        {
            return OperationResult.Fail("GameLoop registry optimization applied, but GPU routing was not confirmed: " + gpuRouting.Message);
        }

        if (!registryChanged && gpuRouting.IsSkipped)
        {
            return OperationResult.Skip("GameLoop registry and GPU routing already configured; no changes were needed.");
        }

        return OperationResult.Ok(gpuRouting.Message + " GameLoop registry optimization applied.");
    }

    /// <summary>
    /// Writes the CPU priority IFEO keys, preserving the elevation guidance
    /// when registry access is denied now that registry failures throw.
    /// </summary>
    /// <returns>A failure result when elevation is missing; null to continue.</returns>
    private OperationResult? TryApplyCpuPriorityKeys(string[] registryKeys, OperationResult gpuRouting, ref bool registryChanged)
    {
        try
        {
            return ApplyCpuPriorityKeys(registryKeys, gpuRouting, ref registryChanged);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
        {
            return gpuRouting.Success
                ? OperationResult.Fail("GPU routing was applied, but CPU priority optimization requires administrator privileges.")
                : OperationResult.Fail("GameLoop registry optimization requires administrator privileges.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"GameLoop registry optimization could not finish: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the CPU priority IFEO keys for each emulator executable.
    /// </summary>
    /// <returns>A failure result when elevation is missing; null to continue.</returns>
    private OperationResult? ApplyCpuPriorityKeys(string[] registryKeys, OperationResult gpuRouting, ref bool registryChanged)
    {
        foreach (var key in registryKeys)
        {
            var subKey = $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{key}\PerfOptions";
            var current = _machineRegistry.GetLocalMachineDword(subKey, "CpuPriorityClass");
            if (current != 3)
            {
                registryChanged = true;
            }

            if (!_machineRegistry.SetLocalMachineDword(subKey, "CpuPriorityClass", 3))
            {
                return gpuRouting.Success
                    ? OperationResult.Fail("GPU routing was applied, but CPU priority optimization requires administrator privileges.")
                    : OperationResult.Fail("GameLoop registry optimization requires administrator privileges.");
            }
        }

        return null;
    }

    /// <summary>
    /// Writes the compatibility flags, preserving the completion message
    /// when registry access fails now that registry failures throw.
    /// </summary>
    /// <returns>A failure result when a flag cannot be written; null to continue.</returns>
    private OperationResult? TryApplyAppCompatFlags(string installPath, string[] registryKeys, ref bool registryChanged)
    {
        try
        {
            return ApplyAppCompatFlags(installPath, registryKeys, ref registryChanged);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
        {
            return OperationResult.Fail("GameLoop registry optimization could not be completed.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"GameLoop registry optimization could not be completed: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the fullscreen-optimization and high-DPI compatibility flags for each emulator executable.
    /// </summary>
    /// <returns>A failure result when a flag cannot be written; null to continue.</returns>
    private OperationResult? ApplyAppCompatFlags(string installPath, string[] registryKeys, ref bool registryChanged)
    {
        const string appCompatSubKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        foreach (var key in registryKeys)
        {
            var targetPath = Path.Combine(installPath, key);
            var current = _userRegistry.GetCurrentUserString(appCompatSubKey, targetPath);
            if (!string.Equals(current, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE", StringComparison.Ordinal))
            {
                registryChanged = true;
            }

            if (!_userRegistry.SetCurrentUserString(appCompatSubKey, targetPath, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE"))
            {
                return OperationResult.Fail("GameLoop registry optimization could not be completed.");
            }
        }

        return null;
    }

    /// <summary>
    /// Applies content scaling and render quality registry settings across all supported PUBG Mobile version keys.
    /// </summary>
    public bool ApplyRenderScaleAndQuality(int contentScaleMultiplier, bool isLowEndProfile)
    {
        var success = true;
        foreach (var version in PubgVersionCatalog.PubgVersions.Keys)
        {
            var contentScaleKey = $"{version}_ContentScale";
            var renderQualityKey = $"{version}_RenderQuality";
            var fpsLevelKey = $"{version}_FPSLevel";

            if (_userRegistry.GetUserDword(contentScaleKey) is not null)
            {
                success &= SetUserRegistryDword(contentScaleKey, contentScaleMultiplier) &&
                           _userRegistry.GetUserDword(contentScaleKey) == contentScaleMultiplier;
            }

            if (_userRegistry.GetUserDword(fpsLevelKey) is not null)
            {
                success &= SetUserRegistryDword(fpsLevelKey, 0) &&
                           _userRegistry.GetUserDword(fpsLevelKey) == 0;
            }

            if (_userRegistry.GetUserDword(renderQualityKey) is not null)
            {
                var quality = isLowEndProfile || contentScaleMultiplier == 1 ? 2 : contentScaleMultiplier;
                success &= SetUserRegistryDword(renderQualityKey, quality) &&
                           _userRegistry.GetUserDword(renderQualityKey) == quality;
            }
        }

        return success;
    }

    private bool SetUserRegistryDword(string name, int value) => _userRegistry.SetUserDword(name, value);
}
