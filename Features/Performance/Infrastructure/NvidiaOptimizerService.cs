using System.Text;
using System.Xml.Linq;
using Nexora.Configuration;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Nexora.Features.Updates.Infrastructure;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Performance.Infrastructure;

/// <summary>
/// Detects NVIDIA adapters and applies GameLoop profile settings via NVIDIA Profile Inspector.
/// </summary>
public sealed class NvidiaOptimizerService
{
    private readonly IProcessRunner _runner;
    private readonly string _assetRoot;
    private readonly IGameLoopProcessService _processService;
    private readonly EmulatorOptions _emulator;
    private readonly GameLoopOptions _gameLoop;
    private readonly UpdateOptions _updates;

    public NvidiaOptimizerService(IProcessRunner runner, IGameLoopProcessService processService, string? assetRoot = null, EmulatorOptions? emulator = null, GameLoopOptions? gameLoop = null, UpdateOptions? updates = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _emulator = emulator ?? new EmulatorOptions();
        _gameLoop = gameLoop ?? new GameLoopOptions();
        _updates = updates ?? new UpdateOptions();
        _assetRoot = assetRoot ?? Path.Combine(AppContext.BaseDirectory, _emulator.Assets.DirectoryName);
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

        var assetsFailure = EnsureOptimizerAssets(out var profilePath, out var inspectorPath, out var installPath);
        if (assetsFailure is not null) return assetsFailure;

        try
        {
            var targets = ResolveProfileTargets(installPath);
            if (targets.Count == 0)
            {
                return OperationResult.Fail("No GameLoop emulator executable was found for the NVIDIA profile.");
            }

            var document = LoadAndCustomizeProfile(profilePath, targets);
            return ImportProfileWithInspector(document, inspectorPath, targets.Count);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"NVIDIA optimization failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies the bundled profile, the inspector binary, and the GameLoop install path exist.
    /// </summary>
    /// <returns>A failure result when anything is missing; null when all assets resolve.</returns>
    private OperationResult? EnsureOptimizerAssets(out string profilePath, out string inspectorPath, out string installPath)
    {
        profilePath = Path.Combine(_assetRoot, _emulator.Assets.NvidiaProfileFileName);
        inspectorPath = Path.Combine(_assetRoot, _emulator.Assets.NvidiaInspectorFileName);
        installPath = _processService.GetGameLoopUiPath() ?? string.Empty;

        if (!File.Exists(profilePath) || !File.Exists(inspectorPath) || string.IsNullOrWhiteSpace(installPath))
        {
            return OperationResult.Fail("NVIDIA optimizer assets or GameLoop path were not found.");
        }

        return null;
    }

    /// <summary>
    /// Loads the bundled profile and retargets it at the installed emulator executables.
    /// </summary>
    private static XDocument LoadAndCustomizeProfile(string profilePath, List<string> targets)
    {
        var document = XDocument.Load(profilePath, LoadOptions.PreserveWhitespace);

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

        return document;
    }

    /// <summary>
    /// Writes the customized profile into a GC-managed staging tree and imports it via Profile Inspector.
    /// The staging directory carries the shared staging prefix so a crashed
    /// import is still collected by the startup sweep instead of littering %TEMP%.
    /// </summary>
    private OperationResult ImportProfileWithInspector(XDocument document, string inspectorPath, int targetCount)
    {
        // Stage under an isolated tree (never a bare file in %TEMP%) to avoid
        // locking the bundled asset and to stay visible to StagingDirectoryGC.
        var stagingRoot = Path.Combine(Path.GetTempPath(), _updates.StagingPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        var importPath = Path.Combine(stagingRoot, _emulator.Assets.NvidiaProfileFileName);
        try
        {
            using (var writer = new StreamWriter(importPath, false, Encoding.Unicode))
            {
                document.Save(writer);
            }

            var result = _runner.Run(inspectorPath, new[] { importPath, "-silent" }, _gameLoop.Timeouts.NvidiaImportTimeout);
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? string.Empty
                : $" {result.StandardError.Trim()}";

            return result.Succeeded
                ? OperationResult.Ok($"NVIDIA optimization applied for {targetCount} executable(s).")
                : OperationResult.Fail($"NVIDIA Profile Inspector could not apply the profile.{detail}");
        }
        finally
        {
            try { File.Delete(importPath); } catch { /* Best-effort cleanup */ }
            StagingDirectoryGC.TryDeleteDirectory(stagingRoot);
        }
    }

    public static GpuVendor ClassifyGpuProvider(string? providerText) =>
        GpuVendorClassifier.Classify(providerText);

    internal static string VendorSkipMessage(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Intel => "Intel GPU detected; NVIDIA profile skipped. DirectX GPU routing still applies.",
        GpuVendor.Amd => "AMD GPU detected; NVIDIA profile skipped. DirectX GPU routing still applies.",
        _ => "No NVIDIA GPU detected; NVIDIA profile skipped."
    };

    /// <summary>
    /// Resolves installed emulator executables for the NVIDIA profile from
    /// the configured image names, in preference order. Paths keep their
    /// on-disk case: any case-insensitive handling downstream must compare
    /// with <see cref="StringComparison.OrdinalIgnoreCase"/> rather than
    /// lowercasing paths, which would conflate distinct spellings.
    /// </summary>
    private List<string> ResolveProfileTargets(string installPath)
    {
        var targets = new List<string>();
        foreach (var fileName in _emulator.Emulator.NvidiaProfileImageNames)
        {
            var fullPath = Path.Combine(installPath, fileName);
            if (File.Exists(fullPath))
            {
                targets.Add(Path.GetFullPath(fullPath));
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
