using Nexora.Configuration;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Nexora.Infrastructure.Processes;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Security.Infrastructure;

/// <summary>
/// Verifies Windows Defender service state and configures exclusions
/// for the GameLoop emulator installation directory.
/// </summary>
public sealed class DefenderExclusionService
{
    private readonly IProcessRunner _runner;
    private readonly IGameLoopProcessService _processService;
    private readonly EmulatorOptions _emulator;

    public DefenderExclusionService(IProcessRunner runner, IGameLoopProcessService processService, EmulatorOptions? emulator = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _emulator = emulator ?? new EmulatorOptions();
    }

    /// <summary>
    /// Checks whether Windows Defender is active and adds the verified GameLoop install directory to exclusions.
    /// </summary>
    public OperationResult AddDefenderExclusion()
    {
        var (defenderState, querySucceeded) = QueryDefenderState();
        if (ShouldSkipDefender(defenderState, querySucceeded))
        {
            return OperationResult.Skip("Windows Defender is disabled or unavailable; exclusion skipped.");
        }

        // Use only the registry-sourced path under HKLM to avoid path spoofing.
        var gameLoopPath = _processService.GetGameLoopRootFromRegistry();
        if (string.IsNullOrWhiteSpace(gameLoopPath))
        {
            return OperationResult.Fail("GameLoop installation path was not found in the registry.");
        }

        if (!IsTrustedGameLoopPath(gameLoopPath, _emulator))
        {
            return OperationResult.Fail("The resolved GameLoop path is outside the expected installation directory.");
        }

        var script = $"Add-MpPreference -ExclusionPath {ProcessText.Quote(gameLoopPath)} -Force";
        var result = _runner.RunPowerShell(script);

        return result.Succeeded
            ? OperationResult.Ok("GameLoop optimizer exclusion applied.")
            : OperationResult.Fail($"Could not update the Windows Defender exclusion. {ProcessText.GetError(result)}");
    }

    /// <summary>
    /// Probes the WinDefend service once, reporting its Status|StartType pair and
    /// whether the query itself succeeded — an unavailable service is a skip, not a failure.
    /// </summary>
    private (string State, bool Succeeded) QueryDefenderState()
    {
        var result = _runner.RunPowerShell(
            "$service = Get-Service -Name WinDefend -ErrorAction SilentlyContinue; " +
            "if ($null -eq $service) { 'Unavailable' } else { \"$($service.Status)|$($service.StartType)\" }");
        return (result.StandardOutput.Trim(), result.Succeeded);
    }

    /// <summary>
    /// Determines whether the Defender exclusion step must be skipped
    /// because the WinDefend service is stopped, disabled, or unavailable.
    /// </summary>
    public static bool ShouldSkipDefender(string? defenderState, bool querySucceeded)
    {
        if (!querySucceeded) return true;
        if (string.IsNullOrWhiteSpace(defenderState)) return true;

        return defenderState.Contains("Stopped", StringComparison.OrdinalIgnoreCase) ||
            defenderState.Contains("Disabled", StringComparison.OrdinalIgnoreCase) ||
            defenderState.Contains("Unavailable", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates that <paramref name="resolvedPath"/> contains the expected
    /// GameLoop installation folder name as a distinct directory segment.
    /// </summary>
    internal static bool IsTrustedGameLoopPath(string resolvedPath, EmulatorOptions emulator)
    {
        if (string.IsNullOrWhiteSpace(resolvedPath)) return false;

        try
        {
            var fullPath = Path.GetFullPath(resolvedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var segments = fullPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Any(segment => string.Equals(segment, (emulator ?? throw new ArgumentNullException(nameof(emulator))).Emulator.InstallFolderName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
