using Nexora.Configuration;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Nexora.Infrastructure.Processes;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Security.Infrastructure;

/// <summary>
/// Verifies Windows Defender service state and adds or removes exclusions
/// for the GameLoop emulator installation directory. Only an exclusion this
/// instance added itself is ever removed.
/// </summary>
public sealed class DefenderExclusionService
{
    private readonly IProcessRunner _runner;
    private readonly IGameLoopProcessService _processService;
    private readonly EmulatorOptions _emulator;

    // The exclusion paths this service instance created, compared like Windows
    // paths (case-insensitively). It is the whole of the removal mandate: only
    // entries here are ever passed to Remove-MpPreference, so an exclusion the
    // user or another tool already had can never be torn down by our cleanup.
    private readonly HashSet<string> _addedExclusionPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _exclusionGate = new();

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

        // Foreign-exclusion guard: a path the user or another tool already
        // excluded is left exactly as-is and claims no ownership, so this
        // session's teardown can never remove an exclusion it did not add.
        if (IsPathAlreadyExcluded(gameLoopPath))
        {
            return OperationResult.Skip("The GameLoop directory is already excluded from Windows Defender; the existing exclusion was left untouched.");
        }

        var script = $"Add-MpPreference -ExclusionPath {ProcessText.Quote(gameLoopPath)} -Force";
        var result = _runner.RunPowerShell(script);
        if (!result.Succeeded)
        {
            return OperationResult.Fail($"Could not update the Windows Defender exclusion. {ProcessText.GetError(result)}");
        }

        // Ownership is recorded only after a successful add, so the set names
        // exactly the exclusions this service instance created.
        lock (_exclusionGate)
        {
            _addedExclusionPaths.Add(gameLoopPath);
        }

        return OperationResult.Ok("GameLoop optimizer exclusion applied.");
    }

    /// <summary>
    /// Removes every Defender exclusion path this service instance added, and
    /// no others. Best-effort by design: each failure settles as a Failed
    /// result rather than an exception, so the teardown path that awaits this
    /// can never throw on close. The add path's trust guard is re-applied per
    /// path so removal can only ever target a GameLoop install directory.
    /// </summary>
    public Task<OperationResult> RemoveDefenderExclusionAsync(CancellationToken cancellationToken = default)
    {
        // Mirrors RestorePerformanceSessionAsync: a canceled teardown refuses to
        // start new system writes instead of racing the window close.
        cancellationToken.ThrowIfCancellationRequested();

        string[] owned;
        lock (_exclusionGate)
        {
            owned = _addedExclusionPaths.ToArray();
        }

        if (owned.Length == 0)
        {
            return Task.FromResult(OperationResult.Skip(
                "No Windows Defender exclusion was added by Nexora this session; nothing to remove."));
        }

        var removed = 0;
        var failures = new List<string>();
        foreach (var path in owned)
        {
            if (!IsTrustedGameLoopPath(path, _emulator))
            {
                failures.Add($"{path} is outside the expected installation directory and was left in place.");
                continue;
            }

            var script = $"Remove-MpPreference -ExclusionPath {ProcessText.Quote(path)}";
            var result = _runner.RunPowerShell(script);
            if (result.Succeeded)
            {
                removed++;
                lock (_exclusionGate)
                {
                    _addedExclusionPaths.Remove(path);
                }
            }
            else
            {
                // The path stays owned so a later teardown retries it; the
                // manual undo documented in the README covers a close that
                // never gets a second attempt.
                failures.Add(ProcessText.GetError(result));
            }
        }

        return failures.Count == 0
            ? Task.FromResult(OperationResult.Ok(
                $"Removed {removed} Windows Defender exclusion path(s) added by Nexora."))
            : Task.FromResult(OperationResult.Fail(
                $"Could not remove {failures.Count} Windows Defender exclusion path(s). {string.Join(" ", failures)}"));
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
    /// Reports whether <paramref name="resolvedPath"/> is already on Defender's
    /// exclusion list. A probe that itself fails reports false so the add path
    /// still runs: Get-MpPreference and Add-MpPreference share the WinDefend
    /// module and the same privileges, so a failing probe alongside a
    /// succeeding add is not a reachable state.
    /// </summary>
    private bool IsPathAlreadyExcluded(string resolvedPath)
    {
        var result = _runner.RunPowerShell("(Get-MpPreference).ExclusionPath");
        return result.Succeeded && IsPathInExclusionList(resolvedPath, result.StandardOutput);
    }

    /// <summary>
    /// Matches a path against the newline-separated ExclusionPath output of
    /// Get-MpPreference. Both sides are normalized before comparing, so a
    /// trailing separator or a case difference cannot hide an existing
    /// exclusion and trigger a duplicate add.
    /// </summary>
    internal static bool IsPathInExclusionList(string resolvedPath, string preferenceOutput)
    {
        if (string.IsNullOrWhiteSpace(resolvedPath) || string.IsNullOrWhiteSpace(preferenceOutput))
        {
            return false;
        }

        var target = NormalizePath(resolvedPath);
        if (target is null)
        {
            return false;
        }

        foreach (var raw in preferenceOutput.Split('\n'))
        {
            var line = raw.Trim('\r', ' ', '"');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var candidate = NormalizePath(line);
            if (candidate is not null && string.Equals(target, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Collapse separators and case so unlike spellings of one path still match.</summary>
    private static string? NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            // Unparseable input is not an exclusion we recognize.
            return null;
        }
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
