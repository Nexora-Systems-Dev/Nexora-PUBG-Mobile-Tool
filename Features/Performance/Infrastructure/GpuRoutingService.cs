using Nexora.Configuration;
using Nexora.Shared.Kernel;
using Nexora.Infrastructure.Registry;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Performance.Infrastructure;

/// <summary>
/// Configures Windows per-application high-performance GPU preferences for GameLoop executables.
/// </summary>
public sealed class GpuRoutingService
{
    private const string UserGpuPreferencesPath =
        @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences";

    private readonly IUserRegistry _registry;
    private readonly EmulatorOptions _emulator;

    public GpuRoutingService(IUserRegistry registry, EmulatorOptions? emulator = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _emulator = emulator ?? new EmulatorOptions();
    }

    public OperationResult ApplyHighPerformance(string? installPath, IEnumerable<string> executableNames)
    {
        var installDirectory = ResolveInstallDirectory(installPath);
        if (installDirectory is null)
        {
            return OperationResult.Fail("GameLoop installation path was not found.");
        }

        // Fail-closed like the Defender exclusion bar: a future caller must
        // never route an arbitrary directory into UserGpuPreferences.
        if (!IsTrustedInstallPath(installDirectory, _emulator))
        {
            return OperationResult.Fail("The resolved GameLoop path is outside the expected installation directory.");
        }

        try
        {
            var applied = 0;
            var alreadyConfigured = 0;
            foreach (var executableName in executableNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                switch (RouteExecutable(installDirectory, executableName))
                {
                    case RoutingOutcome.Applied: applied++; break;
                    case RoutingOutcome.AlreadyConfigured: alreadyConfigured++; break;
                }
            }

            return BuildRoutingResult(applied, alreadyConfigured);
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult.Fail("Windows GPU routing requires administrator access.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Windows GPU routing could not be applied: {ex.Message}");
        }
    }

    /// <summary>
    /// Routes one executable into the high-performance GPU preference, reading the
    /// value back so a denied or dropped write is never reported as applied.
    /// </summary>
    private RoutingOutcome RouteExecutable(string installDirectory, string executableName)
    {
        var executablePath = Path.Combine(installDirectory, executableName);
        if (!File.Exists(executablePath)) return RoutingOutcome.Unchanged;

        var normalizedPath = Path.GetFullPath(executablePath);
        if (IsHighPerformancePreference(_registry.GetCurrentUserString(UserGpuPreferencesPath, normalizedPath)))
        {
            return RoutingOutcome.AlreadyConfigured;
        }

        if (!_registry.SetCurrentUserString(UserGpuPreferencesPath, normalizedPath, "GpuPreference=2;"))
        {
            return RoutingOutcome.Unchanged;
        }

        return IsHighPerformancePreference(_registry.GetCurrentUserString(UserGpuPreferencesPath, normalizedPath))
            ? RoutingOutcome.Applied
            : RoutingOutcome.Unchanged;
    }

    /// <summary>Three-way completion: applied, already configured, or nothing routable found.</summary>
    private static OperationResult BuildRoutingResult(int applied, int alreadyConfigured)
    {
        if (applied > 0)
        {
            return OperationResult.Ok($"Windows high-performance GPU routing set for {applied} GameLoop executable(s).");
        }

        if (alreadyConfigured > 0)
        {
            return OperationResult.Skip($"GPU routing already configured for {alreadyConfigured} GameLoop executable(s); no changes were needed.");
        }

        return OperationResult.Fail("No GameLoop 64-bit rendering executable was found for GPU routing.");
    }

    /// <summary>Counters tracked separately from the write loop so the result reflects every executable.</summary>
    private enum RoutingOutcome { Unchanged, AlreadyConfigured, Applied }

    internal static bool IsHighPerformancePreference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        // Tolerate older builds writing "GpuPreference=2" without the trailing semicolon.
        return string.Equals(value.Trim().TrimEnd(';'), "GpuPreference=2", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates that <paramref name="installDirectory"/> contains the expected
    /// GameLoop installation folder name as a distinct directory segment —
    /// the same fail-closed bar as the Defender exclusion trust check.
    /// </summary>
    internal static bool IsTrustedInstallPath(string? installDirectory, EmulatorOptions emulator)
    {
        if (string.IsNullOrWhiteSpace(installDirectory)) return false;

        try
        {
            var fullPath = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var segments = fullPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Any(segment => string.Equals(segment, (emulator ?? throw new ArgumentNullException(nameof(emulator))).Emulator.InstallFolderName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveInstallDirectory(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return null;

        try
        {
            var fullPath = Path.GetFullPath(installPath.Trim());
            if (Directory.Exists(fullPath)) return fullPath;
            if (File.Exists(fullPath)) return Path.GetDirectoryName(fullPath);

            // Handle registry entries that store an executable path rather than a directory.
            return Path.HasExtension(fullPath)
                ? Path.GetDirectoryName(fullPath)
                : fullPath;
        }
        catch
        {
            return null;
        }
    }
}
