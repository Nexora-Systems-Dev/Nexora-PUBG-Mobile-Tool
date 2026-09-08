using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

/// <summary>
/// Applies the Windows per-application high-performance GPU preference.
/// The preference is vendor-neutral: Windows decides whether that means
/// NVIDIA, AMD, or another available adapter on the current machine.
/// </summary>
public sealed class GpuRoutingService
{
    private const string UserGpuPreferencesPath =
        @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences";

    private readonly RegistryService _registry;

    public GpuRoutingService(RegistryService? registry = null)
    {
        _registry = registry ?? new RegistryService();
    }

    public OperationResult ApplyHighPerformance(string? installPath, IEnumerable<string> executableNames)
    {
        var installDirectory = ResolveInstallDirectory(installPath);
        if (installDirectory is null)
        {
            return OperationResult.Fail("GameLoop installation path was not found.");
        }

        try
        {
            var applied = 0;
            foreach (var executableName in executableNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var executablePath = Path.Combine(installDirectory, executableName);
                if (!File.Exists(executablePath)) continue;

                var normalizedPath = Path.GetFullPath(executablePath);
                if (_registry.SetCurrentUserString(UserGpuPreferencesPath, normalizedPath, "GpuPreference=2;"))
                {
                    var actual = _registry.GetCurrentUserString(UserGpuPreferencesPath, normalizedPath);
                    if (string.Equals(actual, "GpuPreference=2;", StringComparison.OrdinalIgnoreCase))
                    {
                        applied++;
                    }
                }
            }

            return applied > 0
                ? OperationResult.Ok($"Windows high-performance GPU routing set for {applied} GameLoop executable(s).")
                : OperationResult.Fail("No GameLoop 64-bit rendering executable was found for GPU routing.");
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

    private static string? ResolveInstallDirectory(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return null;

        try
        {
            var fullPath = Path.GetFullPath(installPath.Trim());
            if (Directory.Exists(fullPath)) return fullPath;
            if (File.Exists(fullPath)) return Path.GetDirectoryName(fullPath);

            // Some GameLoop registry versions store the launcher executable
            // path even when the file is temporarily unavailable.
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
