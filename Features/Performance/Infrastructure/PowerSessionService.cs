using System.Text.RegularExpressions;

using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Nexora.Infrastructure.Processes;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Performance.Infrastructure;

/// <summary>
/// Manages temporary Windows power plan switching and restores the original scheme on session close.
/// </summary>
public sealed class PowerSessionService
{
    private readonly IProcessRunner _runner;
    private Guid? _previousPowerScheme;

    private const string BalancedSchemeAlias = "SCHEME_BALANCED";
    private const string HighPerformanceSchemeAlias = "SCHEME_MIN";

    private static readonly Regex PowerSchemeGuidRegex = new(
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    public PowerSessionService(IProcessRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public OperationResult Apply(HardwareSnapshot hardware)
    {
        if (_previousPowerScheme is null)
        {
            var active = _runner.Run("powercfg.exe", new[] { "/getactivescheme" });
            var match = PowerSchemeGuidRegex.Match(active.StandardOutput);
            if (!active.Succeeded || !match.Success || !Guid.TryParse(match.Value, out var previous))
            {
                return OperationResult.Fail("Could not identify the active Windows power mode, so no power changes were made.");
            }

            _previousPowerScheme = previous;
        }

        var batterySafe = hardware.IsLaptop && !hardware.IsOnAcPower;
        var scheme = batterySafe ? BalancedSchemeAlias : HighPerformanceSchemeAlias;
        var result = _runner.Run("powercfg.exe", new[] { "/setactive", scheme });
        if (!result.Succeeded)
        {
            return OperationResult.Fail($"Could not activate the Windows power policy. {GetError(result)}");
        }

        return batterySafe
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
