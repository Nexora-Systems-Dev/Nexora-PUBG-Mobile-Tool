using System.Text.RegularExpressions;

using Nexora.Models;

namespace Nexora.Services.Performance;

/// <summary>
/// Temporarily selects a suitable Windows power plan and restores the exact
/// plan that was active before the session. AC and battery use different,
/// deliberate policies for laptops.
/// </summary>
public sealed class PowerSessionService
{
    private readonly ProcessRunner _runner;
    private Guid? _previousPowerScheme;

    public PowerSessionService(ProcessRunner runner)
    {
        _runner = runner;
    }

    public OperationResult Apply(HardwareSnapshot hardware)
    {
        if (_previousPowerScheme is null)
        {
            var active = _runner.Run("powercfg.exe", new[] { "/getactivescheme" });
            var match = Regex.Match(active.StandardOutput, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            if (!active.Succeeded || !match.Success || !Guid.TryParse(match.Value, out var previous))
            {
                return OperationResult.Fail("Could not identify the active Windows power mode, so no power changes were made.");
            }

            _previousPowerScheme = previous;
        }

        var scheme = hardware.IsLaptop && !hardware.IsOnAcPower
            ? "SCHEME_BALANCED"
            : "SCHEME_MIN";
        var result = _runner.Run("powercfg.exe", new[] { "/setactive", scheme });
        if (!result.Succeeded)
        {
            return OperationResult.Fail($"Could not activate the Windows power policy. {GetError(result)}");
        }

        return hardware.IsLaptop && !hardware.IsOnAcPower
            ? OperationResult.Ok("Battery-safe performance session active with Windows Balanced power policy.")
            : OperationResult.Ok("High-performance Windows power policy active for GameLoop.");
    }

    public OperationResult Restore()
    {
        if (_previousPowerScheme is null)
        {
            return OperationResult.Ok("No Windows power session is active.");
        }

        var result = _runner.Run("powercfg.exe", new[] { "/setactive", _previousPowerScheme.Value.ToString() });
        if (!result.Succeeded)
        {
            return OperationResult.Fail($"Could not restore the previous Windows power mode. {GetError(result)}");
        }

        _previousPowerScheme = null;
        return OperationResult.Ok("Previous Windows power mode restored.");
    }

    private static string GetError(ProcessResult result)
    {
        var error = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        return string.IsNullOrWhiteSpace(error) ? "Unknown Windows error." : error.Trim();
    }
}
