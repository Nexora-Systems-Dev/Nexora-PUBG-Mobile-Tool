using System.Text;
using System.Xml.Linq;
using Nexora.Configuration;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

/// <summary>
/// Detects NVIDIA display adapters and applies optimized GameLoop profile settings
/// using NVIDIA Profile Inspector without modifying original assets.
/// </summary>
public sealed class NvidiaOptimizerService
{
    private readonly ProcessRunner _runner;
    private readonly RegistryService _registry;
    private readonly string _assetRoot;
    private readonly GameLoopProcessService _processService;

    public NvidiaOptimizerService(ProcessRunner runner, RegistryService registry, string? assetRoot = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _assetRoot = assetRoot ?? Path.Combine(AppContext.BaseDirectory, AppConstants.Assets.DirectoryName);
        _processService = new GameLoopProcessService(_runner, _registry);
    }

    /// <summary>
    /// Applies high-performance profile settings for NVIDIA graphics cards if present.
    /// </summary>
    public OperationResult OptimizeForNvidia()
    {
        var provider = ReadWmiText("(Get-CimInstance Win32_VideoController | ForEach-Object { $_.AdapterCompatibility; $_.Name }) -join ' | '");
        if (!provider.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Ok("NVIDIA optimization was not needed.");
        }

        var profilePath = Path.Combine(_assetRoot, AppConstants.Assets.NvidiaProfileFileName);
        var inspectorPath = Path.Combine(_assetRoot, AppConstants.Assets.NvidiaInspectorFileName);
        var installPath = _processService.GetGameLoopUiPath();

        if (!File.Exists(profilePath) || !File.Exists(inspectorPath) || string.IsNullOrWhiteSpace(installPath))
        {
            return OperationResult.Fail("NVIDIA optimizer assets or GameLoop path were not found.");
        }

        try
        {
            var document = XDocument.Load(profilePath, LoadOptions.PreserveWhitespace);
            var targetExecutablePath = Path.Combine(installPath, "androidemulatoren.exe").ToLowerInvariant();

            var profileName = document.Descendants("ProfileName").FirstOrDefault();
            var executable = document.Descendants("Executeables").Elements("string").FirstOrDefault();
            if (profileName is not null) profileName.Value = targetExecutablePath;
            if (executable is not null) executable.Value = targetExecutablePath;

            var fxaaSetting = document.Descendants("ProfileSetting")
                .FirstOrDefault(node => node.Element("SettingNameInfo")?.Value == "Enable FXAA")?
                .Element("SettingValue");
            if (fxaaSetting is not null) fxaaSetting.Value = "1";

            // Save to an isolated temporary file to avoid locking the bundled asset.
            var importPath = Path.Combine(Path.GetTempPath(), $"Nexora-{Guid.NewGuid():N}.nip");
            try
            {
                using (var writer = new StreamWriter(importPath, false, Encoding.Unicode))
                {
                    document.Save(writer);
                }

                var result = _runner.Run(inspectorPath, new[] { importPath, "-silent" }, AppConstants.Timeouts.NvidiaImportTimeout);
                var detail = string.IsNullOrWhiteSpace(result.StandardError)
                    ? string.Empty
                    : $" {result.StandardError.Trim()}";

                return result.Succeeded
                    ? OperationResult.Ok("NVIDIA optimization applied.")
                    : OperationResult.Fail($"NVIDIA Profile Inspector could not apply the profile.{detail}");
            }
            finally
            {
                try { File.Delete(importPath); } catch { /* best-effort cleanup of temporary file */ }
            }
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"NVIDIA optimization failed: {ex.Message}");
        }
    }

    private string ReadWmiText(string expression)
    {
        var result = _runner.RunPowerShell($"$value = {expression}; if ($null -ne $value) {{ [Console]::Write($value) }}");
        return result.StandardOutput.Trim();
    }
}
