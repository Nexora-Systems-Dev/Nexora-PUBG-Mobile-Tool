using Nexora.Configuration;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

/// <summary>
/// Verifies Windows Defender service state and configures process/folder exclusions
/// for the GameLoop emulator installation directory.
/// </summary>
public sealed class DefenderExclusionService
{
    private readonly ProcessRunner _runner;
    private readonly RegistryService _registry;
    private readonly GameLoopProcessService _processService;

    public DefenderExclusionService(ProcessRunner runner, RegistryService registry)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _processService = new GameLoopProcessService(_runner, _registry);
    }

    /// <summary>
    /// Checks if Windows Defender is active and adds the GameLoop install directory to the Defender exclusion list.
    /// </summary>
    public OperationResult AddDefenderExclusion()
    {
        var defenderService = _runner.RunPowerShell(
            "$service = Get-Service -Name WinDefend -ErrorAction SilentlyContinue; " +
            "if ($null -eq $service) { 'Unavailable' } else { \"$($service.Status)|$($service.StartType)\" }");

        var defenderState = defenderService.StandardOutput.Trim();
        if (!defenderService.Succeeded ||
            defenderState.Contains("Stopped", StringComparison.OrdinalIgnoreCase) ||
            defenderState.Contains("Disabled", StringComparison.OrdinalIgnoreCase) ||
            defenderState.Contains("Unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Ok("Windows Defender is disabled or unavailable; exclusion skipped.");
        }

        var gameLoopPath = _processService.GetGameLoopRoot();
        if (string.IsNullOrWhiteSpace(gameLoopPath))
        {
            return OperationResult.Fail("GameLoop installation path was not found.");
        }

        var script = $"Add-MpPreference -ExclusionPath {ProcessText.Quote(gameLoopPath)} -Force";
        var result = _runner.RunPowerShell(script);

        return result.Succeeded
            ? OperationResult.Ok("GameLoop optimizer exclusion applied.")
            : OperationResult.Fail($"Could not update the Windows Defender exclusion. {ProcessText.GetError(result)}");
    }
}
