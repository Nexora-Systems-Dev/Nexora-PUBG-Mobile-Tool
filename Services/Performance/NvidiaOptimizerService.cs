using System.Text;
using System.Xml.Linq;
using Nexora.Configuration;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

/// <summary>
/// Detected GPU vendor from a Windows video-controller description string.
/// </summary>
public enum GpuVendor
{
    Nvidia,
    Intel,
    Amd,
    Unknown
}

/// <summary>
/// Detects NVIDIA adapters and applies GameLoop profile settings via NVIDIA Profile Inspector.
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
    /// Applies high-performance profile settings if an NVIDIA GPU is present.
    /// </summary>
    public OperationResult OptimizeForNvidia()
    {
        var provider = ReadWmiText("(Get-CimInstance Win32_VideoController | ForEach-Object { $_.AdapterCompatibility; $_.Name }) -join ' | '");
        var vendor = ClassifyGpuProvider(provider);
        if (vendor != GpuVendor.Nvidia)
        {
            return OperationResult.Skip(VendorSkipMessage(vendor));
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
            var targets = ResolveProfileTargets(installPath);
            if (targets.Count == 0)
            {
                return OperationResult.Fail("No GameLoop emulator executable was found for the NVIDIA profile.");
            }

            var profileName = document.Descendants("ProfileName").FirstOrDefault();
            if (profileName is not null) profileName.Value = targets[0];

            var executables = document.Descendants("Executeables").FirstOrDefault();
            if (executables is not null)
            {
                executables.RemoveAll();
                foreach (var target in targets)
                {
                    executables.Add(new XElement("string", target));
                }
            }

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
                    ? OperationResult.Ok($"NVIDIA optimization applied for {targets.Count} executable(s).")
                    : OperationResult.Fail($"NVIDIA Profile Inspector could not apply the profile.{detail}");
            }
            finally
            {
                try { File.Delete(importPath); } catch { /* Best-effort cleanup */ }
            }
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"NVIDIA optimization failed: {ex.Message}");
        }
    }

    public static GpuVendor ClassifyGpuProvider(string? providerText)
    {
        if (string.IsNullOrWhiteSpace(providerText)) return GpuVendor.Unknown;
        if (providerText.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Nvidia;
        if (providerText.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Intel;
        if (providerText.Contains("AMD", StringComparison.OrdinalIgnoreCase)
            || providerText.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
            || providerText.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase))
        {
            return GpuVendor.Amd;
        }

        return GpuVendor.Unknown;
    }

    internal static string VendorSkipMessage(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Intel => "Intel GPU detected; NVIDIA profile skipped. DirectX GPU routing still applies.",
        GpuVendor.Amd => "AMD GPU detected; NVIDIA profile skipped. DirectX GPU routing still applies.",
        _ => "No NVIDIA GPU detected; NVIDIA profile skipped."
    };

    private static List<string> ResolveProfileTargets(string installPath)
    {
        // NVIDIA profiles address the main emulator and game processes.
        // AndroidRenderer.exe is covered by GPU routing and IFEO instead.
        var preferred = new[] { "AndroidEmulatorEx.exe", "AndroidEmulatorEn.exe", "AndroidEmulator.exe", "aow_exe.exe" };
        var targets = new List<string>();
        foreach (var fileName in preferred)
        {
            var fullPath = Path.Combine(installPath, fileName);
            if (File.Exists(fullPath))
            {
                targets.Add(Path.GetFullPath(fullPath).ToLowerInvariant());
            }
        }

        return targets;
    }

    private string ReadWmiText(string expression)
    {
        var result = _runner.RunPowerShell($"$value = {expression}; if ($null -ne $value) {{ [Console]::Write($value) }}");
        return result.StandardOutput.Trim();
    }
}
