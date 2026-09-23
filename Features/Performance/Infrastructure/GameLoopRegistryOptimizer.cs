using Nexora.Configuration;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.GameLoop.Infrastructure;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Nexora.Features.Graphics.Domain;
using Nexora.Infrastructure.Registry;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Performance.Infrastructure;

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

    private const string AppCompatLayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    // Flags the emulator needs: disable fullscreen optimizations and mark the app high-DPI aware.
    private const string AppCompatFlags = "~ DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE";

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
            foreach (var setting in BuildSmartSettings(hardware, plan))
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
    /// The DWORD table the emulator reads at launch, keyed by GameLoop value name.
    /// </summary>
    private Dictionary<string, int> BuildSmartSettings(HardwareSnapshot hardware, OptimizerPlan plan) =>
        new(StringComparer.Ordinal)
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

        return BuildOptimizationResult(gpuRouting, registryChanged);
    }

    /// <summary>
    /// Three-way completion: downgrade to a skip when nothing changed, and surface
    /// a routing failure last so a partial success keeps its own diagnosis.
    /// </summary>
    private static OperationResult BuildOptimizationResult(OperationResult gpuRouting, bool registryChanged)
    {
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
            return CpuPriorityFailure(gpuRouting);
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
                return CpuPriorityFailure(gpuRouting);
            }
        }

        return null;
    }

    /// <summary>The elevation message keeps what already succeeded so a partial result is not reported as a total failure.</summary>
    private static OperationResult CpuPriorityFailure(OperationResult gpuRouting) =>
        gpuRouting.Success
            ? OperationResult.Fail("GPU routing was applied, but CPU priority optimization requires administrator privileges.")
            : OperationResult.Fail("GameLoop registry optimization requires administrator privileges.");

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
        foreach (var key in registryKeys)
        {
            var targetPath = Path.Combine(installPath, key);
            var current = _userRegistry.GetCurrentUserString(AppCompatLayersKey, targetPath);
            if (!string.Equals(current, AppCompatFlags, StringComparison.Ordinal))
            {
                registryChanged = true;
            }

            if (!_userRegistry.SetCurrentUserString(AppCompatLayersKey, targetPath, AppCompatFlags))
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
            success &= TrySetExistingDword($"{version}_ContentScale", contentScaleMultiplier);
            success &= TrySetExistingDword($"{version}_FPSLevel", 0);
            success &= TrySetExistingDword($"{version}_RenderQuality", ChooseRenderQuality(contentScaleMultiplier, isLowEndProfile));
        }

        return success;
    }

    /// <summary>
    /// Writes a value only where the game already tracks the setting, reading it
    /// back so a denied write can never be reported as success.
    /// </summary>
    private bool TrySetExistingDword(string name, int value)
    {
        if (_userRegistry.GetUserDword(name) is null) return true;
        return SetUserRegistryDword(name, value) && _userRegistry.GetUserDword(name) == value;
    }

    /// <summary>Low-end profiles pin render quality at 2; others follow the content scale.</summary>
    private static int ChooseRenderQuality(int contentScaleMultiplier, bool isLowEndProfile) =>
        isLowEndProfile || contentScaleMultiplier == 1 ? 2 : contentScaleMultiplier;

    private bool SetUserRegistryDword(string name, int value) => _userRegistry.SetUserDword(name, value);
}
