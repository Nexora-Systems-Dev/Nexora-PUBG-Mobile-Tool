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

    /// <summary>
    /// Guards the check-then-act pairs on <see cref="_previousPowerScheme"/>
    /// only — never held across a process spawn. The stated equivalent of the
    /// priority store's SyncRoot: that store is not in this service's
    /// constructor graph, and widening the ctor at every call site for a
    /// shared monitor buys nothing over a private one with the same shape.
    /// </summary>
    private readonly object _sync = new();

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
        var capture = CaptureActiveScheme();
        if (capture.Failure is not null) return capture.Failure;

        var batterySafe = hardware.IsLaptop && !hardware.IsOnAcPower;
        var scheme = batterySafe ? BalancedSchemeAlias : HighPerformanceSchemeAlias;
        var result = _runner.Run("powercfg.exe", new[] { "/setactive", scheme });
        if (!result.Succeeded)
        {
            return OperationResult.Fail($"Could not activate the Windows power policy. {GetError(result)}");
        }

        // A close-path Restore may have consumed the capture mid-flight
        // (restoring the still-original scheme and nulling the field) while
        // this setactive was in flight: re-arm so the restore value is never
        // lost while the system sits modified. The next restore then brings
        // the system back instead of reporting "no session".
        lock (_sync)
        {
            _previousPowerScheme ??= capture.Previous;
        }

        return batterySafe
            ? OperationResult.Ok("Battery-safe performance session active with Windows Balanced power policy.")
            : OperationResult.Ok("High-performance Windows power policy active for GameLoop.");
    }

    /// <summary>
    /// Records the scheme to restore at session close; a present value means
    /// a scheme is already recorded (or was just captured successfully).
    /// Returns the failure, if any, and the captured scheme for the caller to
    /// re-arm if a racing restore consumed it mid-apply.
    /// </summary>
    private (OperationResult? Failure, Guid Previous) CaptureActiveScheme()
    {
        lock (_sync)
        {
            if (_previousPowerScheme is not null) return (null, _previousPowerScheme.Value);
        }

        var active = _runner.Run("powercfg.exe", new[] { "/getactivescheme" });
        var match = PowerSchemeGuidRegex.Match(active.StandardOutput);
        if (!active.Succeeded || !match.Success || !Guid.TryParse(match.Value, out var previous))
        {
            return (OperationResult.Fail("Could not identify the active Windows power mode, so no power changes were made."), default);
        }

        lock (_sync)
        {
            _previousPowerScheme ??= previous;
            return (null, _previousPowerScheme.Value);
        }
    }

    public OperationResult Restore()
    {
        Guid previous;
        lock (_sync)
        {
            if (_previousPowerScheme is null)
            {
                return OperationResult.Ok("No Windows power session is active.");
            }

            previous = _previousPowerScheme.Value;
        }

        var result = _runner.Run("powercfg.exe", new[] { "/setactive", previous.ToString() });
        if (!result.Succeeded)
        {
            return OperationResult.Fail($"Could not restore the previous Windows power mode. {GetError(result)}");
        }

        // Null only what this call restored: a racing Apply that re-armed a
        // capture in the meantime keeps its value for the next restore
        // instead of being silently dropped.
        lock (_sync)
        {
            if (_previousPowerScheme == previous) _previousPowerScheme = null;
        }

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
