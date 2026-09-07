using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;
using System.Xml.Linq;
using Nexora.Models;
using Nexora.Services.Performance;

namespace Nexora.Services;

public sealed class WindowsToolsService : IGameLoopPerformanceEngine
{
    private static readonly string[] GameLoopProcesses =
    {
        "aow_exe.exe", "AndroidEmulatorEn.exe", "AndroidEmulator.exe", "AndroidEmulatorEx.exe",
        "AndroidRenderer.exe", "TBSWebRenderer.exe", "syzs_dl_svr.exe", "AppMarket.exe",
        "QMEmulatorService.exe", "GameLoader.exe", "TSettingCenter.exe", "Auxillary.exe",
        "TP3Helper.exe", "GameDownload.exe", "TInst.exe", "TxGaDcc.exe"
    };

    private static readonly HashSet<string> SafeFallbackProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "aow_exe.exe", "AndroidEmulatorEn.exe", "AndroidEmulator.exe", "AndroidEmulatorEx.exe",
        "AndroidRenderer.exe", "TBSWebRenderer.exe", "syzs_dl_svr.exe", "QMEmulatorService.exe",
        "GameLoader.exe", "TP3Helper.exe", "GameDownload.exe"
    };

    private readonly ProcessRunner _runner;
    private readonly RegistryService _registry;
    private readonly IpadLayoutService _ipadLayout;
    private readonly HardwareDetectionService _hardwareDetection;
    private readonly PerformancePlanBuilder _planBuilder;
    private readonly GpuRoutingService _gpuRouting;
    private readonly ProcessPriorityService _processPriority;
    private readonly PowerSessionService _powerSession;
    private readonly string _assetRoot;

    public WindowsToolsService(ProcessRunner runner, RegistryService registry)
    {
        _runner = runner;
        _registry = registry;
        _ipadLayout = new IpadLayoutService(registry);
        _hardwareDetection = new HardwareDetectionService(runner);
        _planBuilder = new PerformancePlanBuilder();
        _gpuRouting = new GpuRoutingService();
        _processPriority = new ProcessPriorityService();
        _powerSession = new PowerSessionService(runner);
        _assetRoot = Path.Combine(AppContext.BaseDirectory, "Assets");
    }

    public HardwareSnapshot GetHardwareSnapshot() => _hardwareDetection.GetSnapshot();

    public OptimizerPlan GetRecommendedPlan() => _planBuilder.Build(GetHardwareSnapshot());

    public OptimizerPlan GetRecommendedPlan(HardwareSnapshot hardware) => _planBuilder.Build(hardware);

    public OperationResult ApplyPerformanceSession()
    {
        var hardware = GetHardwareSnapshot();
        var gameLoopRoot = GetGameLoopRoot();
        var report = PerformanceExecutionReport.Create(
            ("Power policy", _powerSession.Apply(hardware)),
            ("GameLoop runtime priority", _processPriority.Apply(gameLoopRoot)));

        return report.ToOperationResult(
            "Performance Session active: Windows power policy and GameLoop runtime priority tuned.",
            "Performance Session completed with issues.");
    }

    public OperationResult RestorePerformanceSession()
    {
        var report = PerformanceExecutionReport.Create(
            ("Power policy restore", _powerSession.Restore()),
            ("GameLoop runtime priority restore", _processPriority.Restore()));

        return report.ToOperationResult(
            "Previous Windows power mode and GameLoop priorities restored.",
            "Performance Session restore completed with issues.");
    }

    public OperationResult CleanTemp()
    {
        try
        {
            var skipped = new List<string>();
            ClearChildren(Path.GetTempPath(), skipped);
            ClearChildren(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), skipped);
            ClearChildren(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch"), skipped);

            var installPath = _registry.GetLocalString("InstallPath", "UI");
            if (!string.IsNullOrWhiteSpace(installPath))
            {
                ClearChildren(Path.Combine(installPath, "ShaderCache"), skipped);
            }

            return skipped.Count == 0
                ? OperationResult.Ok("Temporary files cleaned.")
                : OperationResult.Ok($"Temporary files cleaned. {skipped.Count} protected or in-use location(s) were skipped.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Temp cleaner could not finish: {ex.Message}");
        }
    }

    public OperationResult ApplySmartSettings()
    {
        try
        {
            var hardware = GetHardwareSnapshot();
            var plan = _planBuilder.Build(hardware);

            var settings = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["VSyncEnabled"] = hardware.RefreshRateHz < 89 ? 1 : 0,
                ["GraphicsCardEnabled"] = 1,
                ["SetGraphicsCard"] = 1,
                ["VMDPI"] = plan.ContentScale == 1 ? 240 : 480,
                ["FxaaQuality"] = plan.FxaaQuality,
                ["LocalShaderCacheEnabled"] = plan.EnableLocalShaderCache ? 1 : 0,
                ["ShaderCacheEnabled"] = plan.EnableGlobalShaderCache ? 1 : 0,
                ["RenderOptimizeEnabled"] = 1,
                ["AdbDisable"] = 0,
                ["VMMemorySizeInMB"] = plan.EmulatorMemoryMb,
                ["VMCpuCount"] = plan.EmulatorCpuCores
            };

            foreach (var setting in settings)
            {
                if (!Set(setting.Key, setting.Value) || _registry.GetUserDword(setting.Key) != setting.Value)
                {
                    return OperationResult.Fail($"Could not verify GameLoop setting: {setting.Key}.");
                }
            }

            // Keep the local shader cache to avoid rebuilding assets during play.
            // Force-global cache can produce approximate frames, so it stays off
            // as the compatibility-first default for every GPU vendor.
            if (!MakeScale(plan.ContentScale, plan.ContentScale == 1 && plan.FxaaQuality == 0))
            {
                return OperationResult.Fail("Could not verify the PUBG render profile settings.");
            }

            // Let GameLoop keep its selected/automatic rendering mode. Forcing a
            // single API globally can break otherwise compatible Intel, AMD, or
            // NVIDIA driver combinations.
            return OperationResult.Ok($"Smart settings applied for {plan.Tier} hardware. 120 FPS mode is ready when supported by GameLoop/PUBG.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Smart settings could not finish: {ex.Message}");
        }
    }

    public OperationResult AddDefenderExclusion()
    {
        var defenderService = _runner.RunPowerShell(
            "$service = Get-Service -Name WinDefend -ErrorAction SilentlyContinue; " +
            "if ($null -eq $service) { 'Unavailable' } else { \"$($service.Status)|$($service.StartType)\" }");
        var defenderState = defenderService.StandardOutput.Trim();
        if (!defenderService.Succeeded || defenderState.Contains("Stopped", StringComparison.OrdinalIgnoreCase) ||
            defenderState.Contains("Disabled", StringComparison.OrdinalIgnoreCase) ||
            defenderState.Contains("Unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Ok("Windows Defender is disabled or unavailable; exclusion skipped.");
        }

        var installPath = _registry.GetLocalString("InstallPath") ?? _registry.GetLocalString("InstallPath", "UI");
        var gameLoopPath = string.IsNullOrWhiteSpace(installPath) ? null : Path.GetDirectoryName(installPath);
        if (string.IsNullOrWhiteSpace(gameLoopPath))
        {
            return OperationResult.Fail("GameLoop installation path was not found.");
        }

        var script = $"Add-MpPreference -ExclusionPath {PowerShellQuote(gameLoopPath)} -Force";
        var result = _runner.RunPowerShell(script);
        return result.Succeeded
            ? OperationResult.Ok("GameLoop optimizer exclusion applied.")
            : OperationResult.Fail($"Could not update the Windows Defender exclusion. {GetProcessError(result)}");
    }

    public OperationResult OptimizeGameLoopRegistry()
    {
        var installPath = _registry.GetLocalString("InstallPath", "UI");
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return OperationResult.Fail("GameLoop installation path was not found.");
        }

        var registryKeys = new[]
        {
            "AndroidEmulator.exe", "AndroidEmulatorEn.exe", "AndroidEmulatorEx.exe",
            "aow_exe.exe", "AndroidRenderer.exe"
        };
        var gpuRouting = _gpuRouting.ApplyHighPerformance(installPath, registryKeys);
        foreach (var key in registryKeys)
        {
            var result = _runner.Run("reg.exe", new[]
            {
                "ADD", $@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{key}\PerfOptions",
                "/v", "CpuPriorityClass", "/t", "REG_DWORD", "/d", "3", "/f"
            });
            if (!result.Succeeded)
            {
                return gpuRouting.Success
                    ? OperationResult.Fail("GPU routing was applied, but CPU priority optimization requires administrator privileges.")
                    : OperationResult.Fail("GameLoop registry optimization requires administrator privileges.");
            }
        }

        foreach (var key in registryKeys)
        {
            var result = _runner.Run("reg.exe", new[]
            {
                "ADD", @"HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers",
                "/v", Path.Combine(installPath, key), "/t", "REG_SZ", "/d", "~ DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE", "/f"
            });
            if (!result.Succeeded)
            {
                return OperationResult.Fail("GameLoop registry optimization could not be completed.");
            }
        }

        return gpuRouting.Success
            ? OperationResult.Ok(gpuRouting.Message + " GameLoop registry optimization applied.")
            : OperationResult.Fail("GameLoop registry optimization applied, but GPU routing was not confirmed: " + gpuRouting.Message);
    }

    public OperationResult OptimizeGameLoop()
    {
        var report = PerformanceExecutionReport.Create(
            ("GameLoop registry and GPU routing", OptimizeGameLoopRegistry()),
            ("GameLoop runtime priority", _processPriority.Apply(GetGameLoopRoot())),
            ("NVIDIA profile", OptimizeForNvidia()),
            ("Defender exclusion", AddDefenderExclusion()));

        return report.ToOperationResult(
            "Windows and GPU boost applied successfully.",
            "Windows and GPU boost completed with issues.");
    }

    public OperationResult OptimizeForNvidia()
    {
        var provider = ReadWmiText("(Get-CimInstance Win32_VideoController | ForEach-Object { $_.AdapterCompatibility; $_.Name }) -join ' | '");
        if (!provider.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Ok("NVIDIA optimization was not needed.");
        }

        var profilePath = Path.Combine(_assetRoot, "mk.nip");
        var inspectorPath = Path.Combine(_assetRoot, "nvidiaProfileInspector.exe");
        var installPath = _registry.GetLocalString("InstallPath", "UI");
        if (!File.Exists(profilePath) || !File.Exists(inspectorPath) || string.IsNullOrWhiteSpace(installPath))
        {
            return OperationResult.Fail("NVIDIA optimizer assets or GameLoop path were not found.");
        }

        try
        {
            var document = XDocument.Load(profilePath, LoadOptions.PreserveWhitespace);
            var executablePath = Path.Combine(installPath, "androidemulatoren.exe").ToLowerInvariant();
            var profileName = document.Descendants("ProfileName").FirstOrDefault();
            var executable = document.Descendants("Executeables").Elements("string").FirstOrDefault();
            if (profileName is not null) profileName.Value = executablePath;
            if (executable is not null) executable.Value = executablePath;

            var fxaa = document.Descendants("ProfileSetting")
                .FirstOrDefault(node => node.Element("SettingNameInfo")?.Value == "Enable FXAA")?
                .Element("SettingValue");
            if (fxaa is not null) fxaa.Value = "1";

            // Keep the bundled asset read-only. The previous implementation held
            // a StreamWriter open on mk.nip while Profile Inspector tried to import
            // the same file, which produced the "file is being used" error.
            var importPath = Path.Combine(Path.GetTempPath(), $"Nexora-{Guid.NewGuid():N}.nip");
            try
            {
                using (var writer = new StreamWriter(importPath, false, Encoding.Unicode))
                {
                    document.Save(writer);
                }

                var result = _runner.Run(inspectorPath, new[] { importPath, "-silent" }, TimeSpan.FromSeconds(60));
                var detail = string.IsNullOrWhiteSpace(result.StandardError)
                    ? string.Empty
                    : $" {result.StandardError.Trim()}";
                return result.Succeeded
                    ? OperationResult.Ok("NVIDIA optimization applied.")
                    : OperationResult.Fail($"NVIDIA Profile Inspector could not apply the profile.{detail}");
            }
            finally
            {
                try { File.Delete(importPath); } catch { /* best-effort cleanup of our temporary import */ }
            }
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"NVIDIA optimization failed: {ex.Message}");
        }
    }

    public OperationResult OptimizeAll()
    {
        var report = PerformanceExecutionReport.Create(
            ("Smart settings", ApplySmartSettings()),
            ("GameLoop registry and GPU routing", OptimizeGameLoopRegistry()),
            ("GameLoop runtime priority", _processPriority.Apply(GetGameLoopRoot())),
            ("NVIDIA profile", OptimizeForNvidia()),
            ("Defender exclusion", AddDefenderExclusion()),
            ("Temp cleanup", CleanTemp()));

        return report.ToOperationResult(
            "All recommended settings applied successfully.",
            "Optimizer completed with issues.");
    }

    public OperationResult KillGameLoopProcesses()
    {
        var gameLoopRoot = GetGameLoopRoot();
        var candidates = FindGameLoopProcesses(gameLoopRoot);
        if (candidates.Count == 0)
        {
            return OperationResult.Ok("No GameLoop processes were found.");
        }

        var ended = 0;
        foreach (var process in candidates)
        {
            try
            {
                var result = _runner.Run("taskkill.exe", new[] { "/PID", process.Id.ToString(), "/T", "/F" }, TimeSpan.FromSeconds(10));
                if (result.Succeeded)
                {
                    ended++;
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        var remaining = FindGameLoopProcesses(gameLoopRoot);
        if (remaining.Count > 0)
        {
            var names = string.Join(", ", remaining.Select(process => $"{process.ProcessName}.exe").Distinct(StringComparer.OrdinalIgnoreCase));
            foreach (var process in remaining) process.Dispose();
            return OperationResult.Fail($"GameLoop force close could not end: {names}.");
        }

        return OperationResult.Ok($"GameLoop force close completed. {ended} process(es) ended.");
    }

    private List<Process> FindGameLoopProcesses(string? gameLoopRoot)
    {
        var candidates = new List<Process>();
        foreach (var imageName in GameLoopProcesses)
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(imageName)))
            {
                var isGameLoopProcess = false;
                try
                {
                    var executablePath = process.MainModule?.FileName;
                    isGameLoopProcess = IsGameLoopPath(executablePath, gameLoopRoot) ||
                        (string.IsNullOrWhiteSpace(executablePath) && SafeFallbackProcessNames.Contains(imageName));
                }
                catch
                {
                    // The app runs elevated, but protected processes can still
                    // deny path access. Only use the fallback for emulator-only
                    // names; never terminate a generic Windows process by name.
                    isGameLoopProcess = SafeFallbackProcessNames.Contains(imageName);
                }

                if (isGameLoopProcess)
                {
                    candidates.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }
        }

        return candidates
            .GroupBy(process => process.Id)
            .Select(group => group.First())
            .ToList();
    }

    private string? GetGameLoopRoot()
    {
        var installPath = _registry.GetLocalString("InstallPath", "UI");
        if (string.IsNullOrWhiteSpace(installPath)) return null;

        try
        {
            return Directory.GetParent(Path.GetFullPath(installPath))?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsGameLoopPath(string? executablePath, string? gameLoopRoot)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;

        try
        {
            var fullPath = Path.GetFullPath(executablePath);
            if (!string.IsNullOrWhiteSpace(gameLoopRoot))
            {
                var root = Path.GetFullPath(gameLoopRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return fullPath.Contains($"{Path.DirectorySeparatorChar}TxGameAssistant{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public OperationResult ChangeDns(string primary, string secondary)
    {
        var script = $"$servers = [string[]]@({PowerShellQuote(primary)}, {PowerShellQuote(secondary)}); " +
                     "$adapters = @(Get-NetAdapter -ErrorAction Stop | Where-Object { $_.Status -eq 'Up' -and " +
                     "@(Get-NetIPAddress -InterfaceIndex $_.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | " +
                     "Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -notlike '169.254.*' }).Count -gt 0 }); " +
                     "if ($adapters.Count -eq 0) { Write-Error 'No active network adapters were found.'; exit 2 }; " +
                     "$failed = New-Object 'System.Collections.Generic.List[string]'; " +
                     "$applied = 0; " +
                     "foreach ($adapter in $adapters) { try { Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses $servers -ErrorAction Stop; $applied++ } " +
                     "catch { [void]$failed.Add(([string]$adapter.Name + ': ' + $_.Exception.Message)) } }; " +
                     "ipconfig /flushdns | Out-Null; " +
                     "$notApplied = New-Object 'System.Collections.Generic.List[string]'; " +
                     "foreach ($adapter in $adapters) { $actual = @(Get-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | " +
                     "Select-Object -ExpandProperty ServerAddresses); " +
                     "if ($actual.Count -ne $servers.Count -or (($actual -join '|') -ne ($servers -join '|'))) { [void]$notApplied.Add([string]$adapter.Name) } }; " +
                     "if ($applied -eq 0 -or $failed.Count -gt 0 -or $notApplied.Count -gt 0) { " +
                     "Write-Error ('Could not verify DNS on: ' + (($failed + $notApplied) -join ', ')); exit 2 }; " +
                     "Write-Output ('NEXORA_DNS_OK|' + $applied)";
        var result = _runner.RunPowerShell(script, TimeSpan.FromSeconds(45));
        if (!result.Succeeded || !result.StandardOutput.Contains("NEXORA_DNS_OK|", StringComparison.Ordinal))
        {
            return OperationResult.Fail($"Could not change DNS settings. {GetProcessError(result)}");
        }

        var marker = result.StandardOutput.Trim().Split('|').LastOrDefault();
        return OperationResult.Ok($"DNS changed to {primary} / {secondary} on {marker ?? "active adapters"}.");
    }

    public int? PingDns(string host)
    {
        try
        {
            using var ping = new Ping();
            long? lowest = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var reply = ping.Send(host, 1000);
                if (reply.Status == IPStatus.Success && (lowest is null || reply.RoundtripTime < lowest))
                {
                    lowest = reply.RoundtripTime;
                }
            }

            return lowest is null ? null : (int)lowest.Value;
        }
        catch
        {
            return null;
        }
    }

    public OperationResult CreateShortcut(string displayName, string packageName)
    {
        var marketPath = _registry.GetLocalString("InstallPath") ?? @"C:\Program Files\TxGameAssistant\AppMarket";
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, $"{displayName}.lnk");
        var iconSource = Path.Combine(_assetRoot, "Icons", $"{packageName}.ico");
        var iconPath = Path.Combine(marketPath, $"{packageName}.ico");
        var target = Path.Combine(marketPath, "AppMarket.exe");
        if (!File.Exists(target))
        {
            return OperationResult.Fail("GameLoop AppMarket.exe was not found.");
        }

        try
        {
            if (File.Exists(iconSource))
            {
                File.Copy(iconSource, iconPath, overwrite: true);
            }

            var script = "$ws = New-Object -ComObject WScript.Shell; " +
                         $"$s = $ws.CreateShortcut({PowerShellQuote(shortcutPath)}); " +
                         $"$s.TargetPath = {PowerShellQuote(target)}; " +
                         $"$s.Arguments = {PowerShellQuote($"-startpkg {packageName}  -from DesktopLink")}; " +
                         "$s.Description = 'Nexora PUBG Mobile Tool'; " +
                         $"$s.IconLocation = {PowerShellQuote(iconPath)}; $s.Save()";
            var result = _runner.RunPowerShell(script);
            return result.Succeeded
                ? OperationResult.Ok("Desktop shortcut created.")
                : OperationResult.Fail("Could not create the desktop shortcut.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Shortcut creation failed: {ex.Message}");
        }
    }

    public OperationResult SetIpadResolution(int width, int height)
    {
        var running = FindGameLoopProcesses(GetGameLoopRoot());
        if (running.Count > 0)
        {
            var names = string.Join(", ", running.Select(process => $"{process.ProcessName}.exe").Distinct(StringComparer.OrdinalIgnoreCase));
            foreach (var process in running) process.Dispose();
            return OperationResult.Fail($"Close GameLoop before applying iPad View ({names}), then apply it again.");
        }

        return _ipadLayout.Apply(width, height);
    }

    public OperationResult ResetIpadResolution()
    {
        return _ipadLayout.Reset();
    }

    private bool MakeScale(int value, bool low)
    {
        var success = true;
        foreach (var version in GameLoopService.PubgVersions.Keys)
        {
            var contentScale = $"{version}_ContentScale";
            var renderQuality = $"{version}_RenderQuality";
            var fpsLevel = $"{version}_FPSLevel";
            if (_registry.GetUserDword(contentScale) is not null)
            {
                success &= Set(contentScale, value) && _registry.GetUserDword(contentScale) == value;
            }

            if (_registry.GetUserDword(fpsLevel) is not null)
            {
                success &= Set(fpsLevel, 0) && _registry.GetUserDword(fpsLevel) == 0;
            }

            if (_registry.GetUserDword(renderQuality) is not null)
            {
                var quality = low || value == 1 ? 2 : value;
                success &= Set(renderQuality, quality) && _registry.GetUserDword(renderQuality) == quality;
            }
        }

        return success;
    }

    private bool Set(string name, int value) => _registry.SetUserDword(name, value);

    private static void ClearChildren(string directory, ICollection<string> skipped)
    {
        if (!Directory.Exists(directory)) return;
        string[] files;
        try
        {
            files = Directory.EnumerateFiles(directory).ToArray();
        }
        catch
        {
            skipped.Add(directory);
            return;
        }

        foreach (var file in files)
        {
            try { File.Delete(file); } catch { skipped.Add(file); }
        }

        string[] children;
        try
        {
            children = Directory.EnumerateDirectories(directory).ToArray();
        }
        catch
        {
            skipped.Add(directory);
            return;
        }

        foreach (var child in children)
        {
            try { Directory.Delete(child, recursive: true); } catch { skipped.Add(child); }
        }
    }

    private long? ReadWmiNumber(string expression)
    {
        var result = _runner.RunPowerShell($"$value = {expression}; if ($null -ne $value) {{ [Console]::Write($value) }}");
        return long.TryParse(result.StandardOutput.Trim(), out var value) ? value : null;
    }

    private string ReadWmiText(string expression)
    {
        var result = _runner.RunPowerShell($"$value = {expression}; if ($null -ne $value) {{ [Console]::Write($value) }}");
        return result.StandardOutput.Trim();
    }

    private static string PowerShellQuote(string value) => "'" + value.Replace("'", "''") + "'";

    private static string GetProcessError(ProcessResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        detail = detail.Trim();
        return string.IsNullOrWhiteSpace(detail)
            ? "No additional details were returned."
            : detail.Length > 240 ? detail[..240] : detail;
    }
}
