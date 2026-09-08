using Nexora.Configuration;
using Nexora.Features.Performance;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

/// <summary>
/// Configures GameLoop emulator registry settings, hardware-guided smart settings,
/// and Windows Image File Execution Options (IFEO) CPU priorities.
/// </summary>
public sealed class GameLoopRegistryOptimizer
{
    private readonly ProcessRunner _runner;
    private readonly RegistryService _registry;
    private readonly GpuRoutingService _gpuRouting;
    private readonly GameLoopProcessService _processService;

    public GameLoopRegistryOptimizer(ProcessRunner runner, RegistryService registry, GpuRoutingService? gpuRouting = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _gpuRouting = gpuRouting ?? new GpuRoutingService();
        _processService = new GameLoopProcessService(_runner, _registry);
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
                [AppConstants.Registry.ValueAdbDisable] = 0,
                ["VMMemorySizeInMB"] = plan.EmulatorMemoryMb,
                ["VMCpuCount"] = plan.EmulatorCpuCores
            };

            foreach (var setting in settings)
            {
                if (!SetUserRegistryDword(setting.Key, setting.Value) || _registry.GetUserDword(setting.Key) != setting.Value)
                {
                    return OperationResult.Fail($"Could not verify GameLoop setting: {setting.Key}.");
                }
            }

            // Keep the local shader cache to avoid rebuilding assets during play.
            // Force-global cache can produce approximate frames, so it stays off
            // as the compatibility-first default for every GPU vendor.
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
    /// Applies high-performance GPU routing, CPU priority IFEO registry keys, and AppCompatFlags to GameLoop executables.
    /// </summary>
    public OperationResult OptimizeGameLoopRegistry()
    {
        var installPath = _processService.GetGameLoopUiPath();
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return OperationResult.Fail("GameLoop installation path was not found.");
        }

        var registryKeys = AppConstants.Emulator.RegistryImageNames;
        var gpuRouting = _gpuRouting.ApplyHighPerformance(installPath, registryKeys);

        foreach (var key in registryKeys)
        {
            var subKey = $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{key}\PerfOptions";
            if (!_registry.SetLocalMachineDword(subKey, "CpuPriorityClass", 3))
            {
                return gpuRouting.Success
                    ? OperationResult.Fail("GPU routing was applied, but CPU priority optimization requires administrator privileges.")
                    : OperationResult.Fail("GameLoop registry optimization requires administrator privileges.");
            }
        }

        foreach (var key in registryKeys)
        {
            const string appCompatSubKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
            var targetPath = Path.Combine(installPath, key);
            if (!_registry.SetCurrentUserString(appCompatSubKey, targetPath, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE"))
            {
                return OperationResult.Fail("GameLoop registry optimization could not be completed.");
            }
        }

        return gpuRouting.Success
            ? OperationResult.Ok(gpuRouting.Message + " GameLoop registry optimization applied.")
            : OperationResult.Fail("GameLoop registry optimization applied, but GPU routing was not confirmed: " + gpuRouting.Message);
    }

    /// <summary>
    /// Applies content scaling and render quality registry settings across all supported PUBG Mobile version keys.
    /// </summary>
    public bool ApplyRenderScaleAndQuality(int contentScaleMultiplier, bool isLowEndProfile)
    {
        var success = true;
        foreach (var version in GameLoopService.PubgVersions.Keys)
        {
            var contentScaleKey = $"{version}_ContentScale";
            var renderQualityKey = $"{version}_RenderQuality";
            var fpsLevelKey = $"{version}_FPSLevel";

            if (_registry.GetUserDword(contentScaleKey) is not null)
            {
                success &= SetUserRegistryDword(contentScaleKey, contentScaleMultiplier) &&
                           _registry.GetUserDword(contentScaleKey) == contentScaleMultiplier;
            }

            if (_registry.GetUserDword(fpsLevelKey) is not null)
            {
                success &= SetUserRegistryDword(fpsLevelKey, 0) &&
                           _registry.GetUserDword(fpsLevelKey) == 0;
            }

            if (_registry.GetUserDword(renderQualityKey) is not null)
            {
                var quality = isLowEndProfile || contentScaleMultiplier == 1 ? 2 : contentScaleMultiplier;
                success &= SetUserRegistryDword(renderQualityKey, quality) &&
                           _registry.GetUserDword(renderQualityKey) == quality;
            }
        }

        return success;
    }

    private bool SetUserRegistryDword(string name, int value) => _registry.SetUserDword(name, value);
}
