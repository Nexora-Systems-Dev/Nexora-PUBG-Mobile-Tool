using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Configuration;
using Nexora.Features.GameLoop;
using Nexora.Features.Layout;
using Nexora.Features.Performance;
using Nexora.Features.SystemTools.Network;
using Nexora.Services;
using Nexora.Services.Performance;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Nexora.UI.Presentation;
using Xunit;

namespace Nexora.Tests.Services;

/// <summary>
/// Live functional verification suite targeting the active GameLoop emulator instance.
/// Validates dynamic path resolution (Registry & Process detection) and Step 1 (Connection & Diagnostics).
/// </summary>
public sealed class LiveGameLoopVerificationTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public LiveGameLoopVerificationTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "LiveFunctionalVerification")]
    public void DynamicPathResolution_ResolvesGameLoopPathsFromRegistry_WithoutHardcodedDrives()
    {
        var registry = new RegistryService();
        var runner = new ProcessRunner();
        var processService = new GameLoopProcessService(runner, registry);

        // Resolve paths dynamically via registry branches
        var appMarketPath = registry.GetLocalString(AppConstants.Registry.ValueInstallPath, AppConstants.Registry.BranchAppMarket);
        var uiPath = registry.GetLocalString(AppConstants.Registry.ValueInstallPath, AppConstants.Registry.BranchUI);
        var gameLoopRoot = processService.GetGameLoopRoot();

        _output.WriteLine($"[Diagnostic] AppMarket Path: {appMarketPath}");
        _output.WriteLine($"[Diagnostic] UI Path: {uiPath}");
        _output.WriteLine($"[Diagnostic] GameLoop Root: {gameLoopRoot}");

        // Ensure paths are resolved dynamically and exist on disk
        appMarketPath.Should().NotBeNullOrWhiteSpace("AppMarket install path must be resolved from registry");
        Directory.Exists(appMarketPath!).Should().BeTrue($"Resolved AppMarket path '{appMarketPath}' must exist on disk");

        uiPath.Should().NotBeNullOrWhiteSpace("UI install path must be resolved from registry");
        Directory.Exists(uiPath!).Should().BeTrue($"Resolved UI path '{uiPath}' must exist on disk");

        gameLoopRoot.Should().NotBeNullOrWhiteSpace("GameLoop root directory must be resolved from UI install path");
        Directory.Exists(gameLoopRoot!).Should().BeTrue($"Resolved GameLoop root '{gameLoopRoot}' must exist on disk");

        // Verify that UI and AppMarket paths are within the resolved GameLoop root
        uiPath!.StartsWith(gameLoopRoot!, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        appMarketPath!.StartsWith(gameLoopRoot!, StringComparison.OrdinalIgnoreCase).Should().BeTrue();

        // Verify that adb.exe exists within the dynamically discovered UI folder
        var adbBinary = Path.Combine(uiPath, AppConstants.Adb.FileName);
        File.Exists(adbBinary).Should().BeTrue($"adb.exe must exist in resolved UI directory '{adbBinary}'");
        _output.WriteLine($"[Diagnostic] ADB Binary Path: {adbBinary}");
    }

    [Fact]
    [Trait("Category", "LiveFunctionalVerification")]
    public async Task Step1_LiveConnectionAndDiagnostics_SuccessfullyConnectsAndLoadsPubgSettings()
    {
        // Build services using composition root / DI container
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var gameLoopService = provider.GetRequiredService<IGameLoopService>();
        var adbClient = provider.GetRequiredService<IAdbClient>();
        var registryService = provider.GetRequiredService<IRegistryService>();

        var isRunning = GameLoopService.IsGameLoopRunning();
        _output.WriteLine($"[Diagnostic] GameLoop Running: {isRunning}");
        isRunning.Should().BeTrue("GameLoop emulator process (AndroidEmulatorEn / AndroidEmulatorEx / AndroidEmulator) must be active");

        var adbStatus = registryService.GetUserDword(AppConstants.Registry.ValueAdbDisable);
        _output.WriteLine($"[Diagnostic] AdbDisable Value: {adbStatus}");
        adbStatus.Should().NotBeNull("GameLoop AdbDisable registry setting must exist");
        adbStatus.Should().Be(0, "AdbDisable must be 0 (enabled) for ADB bridge communication");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var bootFinished = await adbClient.WaitForBootAsync(cts.Token);
        _output.WriteLine($"[Diagnostic] ADB Boot Complete: {bootFinished}");
        bootFinished.Should().BeTrue("GameLoop Android subsystem must report dev.bootcomplete = 1");

        var connectionResult = await gameLoopService.ConnectAsync(cts.Token);
        connectionResult.Should().NotBeNull();
        _output.WriteLine($"[Diagnostic] Connection Success: {connectionResult.Success}, Message: {connectionResult.Message}");
        connectionResult.Success.Should().BeTrue($"ConnectAsync must succeed: {connectionResult.Message}");

        _output.WriteLine($"[Diagnostic] Installed Packages Detected: {string.Join(", ", connectionResult.InstalledVersions.Select(v => $"{v.DisplayName} ({v.PackageName})"))}");
        connectionResult.InstalledVersions.Should().NotBeEmpty("At least one installed PUBG Mobile version should be detected");
        connectionResult.InstalledVersions.Should().Contain(v => v.PackageName == "com.tencent.ig", "PUBG Mobile Global must be installed");

        gameLoopService.IsConnected.Should().BeTrue("GameLoopService must be in connected state with Active.sav loaded");
        gameLoopService.CurrentPackage.Should().Be("com.tencent.ig");

        var quality = gameLoopService.GetGraphicsQuality();
        var fps = gameLoopService.GetFrameRate();
        var style = gameLoopService.GetGraphicsStyle();

        _output.WriteLine($"[Diagnostic] Live PUBG Graphics Quality: {quality}");
        _output.WriteLine($"[Diagnostic] Live PUBG Frame Rate: {fps}");
        _output.WriteLine($"[Diagnostic] Live PUBG Graphics Style: {style}");

        quality.Should().NotBeNullOrWhiteSpace("Read BattleRenderQuality property from pulled Active.sav");
        fps.Should().NotBeNullOrWhiteSpace("Read BattleFPS property from pulled Active.sav");
        style.Should().NotBeNullOrWhiteSpace("Read BattleRenderStyle property from pulled Active.sav");
    }

    [Fact]
    [Trait("Category", "LiveFunctionalVerification")]
    public async Task Step2_GraphicsSettingsPage_LiveApplicationAndVerification_NonDestructive()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var gameLoopService = provider.GetRequiredService<IGameLoopService>();
        var adbClient = provider.GetRequiredService<IAdbClient>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var connectResult = await gameLoopService.ConnectAsync(cts.Token);
        connectResult.Success.Should().BeTrue("Must connect to GameLoop before graphics modification");

        var originalQuality = gameLoopService.GetGraphicsQuality();
        var originalFps = gameLoopService.GetFrameRate();
        var originalStyle = gameLoopService.GetGraphicsStyle();
        var originalShadow = await gameLoopService.GetShadowAsync(cts.Token);

        _output.WriteLine($"[Original Live Settings] Quality: {originalQuality}, FPS: {originalFps}, Style: {originalStyle}, Shadow: {originalShadow}");

        var targetQuality = originalQuality == "Smooth" ? "Balanced" : "Smooth";
        var targetFps = originalFps == "Extreme" ? "Ultra Extreme" : "Extreme";
        var targetStyle = originalStyle == "Colorful" ? "Classic" : "Colorful";
        var targetShadow = false;

        var targetSelection = new GraphicsSelection(
            targetQuality,
            targetFps,
            targetStyle,
            EnableShadow: targetShadow,
            EnableKoreanFullHd: false);

        _output.WriteLine($"[Applying Target Preset] Quality: {targetQuality}, FPS: {targetFps}, Style: {targetStyle}, Shadow: {targetShadow}");

        var applyResult = await gameLoopService.ApplyGraphicsAsync(targetSelection, cts.Token);
        applyResult.Success.Should().BeTrue($"ApplyGraphicsAsync must succeed: {applyResult.Message}");
        _output.WriteLine($"[ApplyGraphicsAsync Result] {applyResult.Message}");

        gameLoopService.GetGraphicsQuality().Should().Be(targetQuality);
        gameLoopService.GetFrameRate().Should().Be(targetFps);
        gameLoopService.GetGraphicsStyle().Should().Be(targetStyle);
        var liveShadow = await gameLoopService.GetShadowAsync(cts.Token);
        liveShadow.Should().Be("Disable");

        var tempSavPath = Path.Combine(Path.GetTempPath(), $"live_verify_{Guid.NewGuid():N}.sav");
        try
        {
            var remoteSavPath = $"/sdcard/Android/data/{gameLoopService.CurrentPackage}/files/UE4Game/ShadowTrackerExtra/ShadowTrackerExtra/Saved/SaveGames/Active.sav";
            var pullSuccess = await adbClient.PullAsync(remoteSavPath, tempSavPath, cts.Token);

            pullSuccess.Should().BeTrue("Fresh Active.sav must pull successfully from remote emulator");
            File.Exists(tempSavPath).Should().BeTrue();
            var remoteSavBytes = File.ReadAllBytes(tempSavPath);
            remoteSavBytes.Length.Should().BeGreaterThan(1000, "Active.sav should be a valid non-empty SaveGame file");

            var savEditor = new Ue4SavEditor(remoteSavBytes);
            var qualityMap = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
            {
                ["Smooth"] = 0x01, ["Balanced"] = 0x02, ["HD"] = 0x03, ["HDR"] = 0x04, ["Ultra HDR"] = 0x05, ["Extreme HDR"] = 0x06
            };
            var fpsMap = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
            {
                ["Low"] = 0x02, ["Medium"] = 0x03, ["High"] = 0x04, ["Ultra"] = 0x05, ["Extreme"] = 0x06, ["Extreme+"] = 0x07, ["Ultra Extreme"] = 0x08
            };
            var styleMap = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
            {
                ["Classic"] = 0x01, ["Colorful"] = 0x02, ["Realistic"] = 0x03, ["Soft"] = 0x04, ["Movie"] = 0x06
            };

            savEditor.ReadProperty("BattleRenderQuality").Should().Be(qualityMap[targetQuality]);
            savEditor.ReadProperty("BattleFPS").Should().Be(fpsMap[targetFps]);
            savEditor.ReadProperty("BattleRenderStyle").Should().Be(styleMap[targetStyle]);
            _output.WriteLine($"[Remote Active.sav Verified] BattleRenderQuality: 0x{savEditor.ReadProperty("BattleRenderQuality"):X2}, BattleFPS: 0x{savEditor.ReadProperty("BattleFPS"):X2}, BattleRenderStyle: 0x{savEditor.ReadProperty("BattleRenderStyle"):X2}");
        }
        finally
        {
            if (File.Exists(tempSavPath)) File.Delete(tempSavPath);
        }

        var originalSelection = new GraphicsSelection(
            originalQuality,
            originalFps,
            originalStyle,
            EnableShadow: string.Equals(originalShadow, "Enable", StringComparison.OrdinalIgnoreCase),
            EnableKoreanFullHd: false);

        var restoreResult = await gameLoopService.ApplyGraphicsAsync(originalSelection, cts.Token);
        restoreResult.Success.Should().BeTrue($"Restoring original graphics settings must succeed: {restoreResult.Message}");
        _output.WriteLine($"[Restored Original Settings] Quality: {gameLoopService.GetGraphicsQuality()}, FPS: {gameLoopService.GetFrameRate()}, Style: {gameLoopService.GetGraphicsStyle()}");

        gameLoopService.GetGraphicsQuality().Should().Be(originalQuality);
        gameLoopService.GetFrameRate().Should().Be(originalFps);
        gameLoopService.GetGraphicsStyle().Should().Be(originalStyle);
    }

    [Fact]
    [Trait("Category", "LiveFunctionalVerification")]
    public async Task Step3_PerformanceCenter_LiveHardwarePlanAndRegistryOptimization_NonDestructive()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var performanceEngine = provider.GetRequiredService<IGameLoopPerformanceEngine>();
        var registry = provider.GetRequiredService<IRegistryService>();
        var processRunner = provider.GetRequiredService<IProcessRunner>() as ProcessRunner ?? new ProcessRunner();
        var registryOptimizer = new GameLoopRegistryOptimizer(processRunner, registry as RegistryService ?? new RegistryService());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var hardware = await performanceEngine.GetHardwareSnapshotAsync(cts.Token);
        _output.WriteLine($"[Hardware Snapshot] CPU: {hardware.CpuName} ({hardware.PhysicalCores} cores, {hardware.LogicalCores} threads)");
        _output.WriteLine($"[Hardware Snapshot] RAM: {hardware.TotalMemoryGb} GB");
        _output.WriteLine($"[Hardware Snapshot] GPU: {hardware.GpuVendor} - {hardware.GpuName} ({hardware.GpuMemoryGb} GB VRAM)");
        _output.WriteLine($"[Hardware Snapshot] Display: {hardware.RefreshRateHz} Hz");
        _output.WriteLine($"[Hardware Snapshot] Laptop: {hardware.IsLaptop}, On AC: {hardware.IsOnAcPower}, VT: {hardware.VirtualizationEnabled}");

        hardware.PhysicalCores.Should().BeGreaterThan(0, "Host must have at least 1 physical CPU core");
        hardware.LogicalCores.Should().BeGreaterThanOrEqualTo(hardware.PhysicalCores, "Logical cores >= physical cores");
        hardware.TotalMemoryGb.Should().BeGreaterThan(0, "Host must have detectable RAM");
        hardware.GpuName.Should().NotBeNullOrWhiteSpace("GPU model must be detected");
        hardware.RefreshRateHz.Should().BeGreaterThanOrEqualTo(60, "Display refresh rate >= 60Hz");

        var plan = performanceEngine.GetRecommendedPlan(hardware);
        _output.WriteLine($"[Recommended Plan] Tier: {plan.Tier}, Memory: {plan.EmulatorMemoryMb} MB, CPU Cores: {plan.EmulatorCpuCores}");
        _output.WriteLine($"[Recommended Plan] Content Scale: {plan.ContentScale}, FXAA: {plan.FxaaQuality}, Target FPS: {plan.RecommendedFps}");
        _output.WriteLine($"[Recommended Plan] GPU Route: {plan.GpuRoute}, Power Mode: {plan.PowerMode}");

        plan.Tier.Should().BeOneOf("Performance", "Balanced", "Entry");
        plan.EmulatorMemoryMb.Should().BeInRange(2048, 8192);
        plan.EmulatorCpuCores.Should().BeInRange(2, Math.Max(2, hardware.PhysicalCores));
        plan.ContentScale.Should().BeOneOf(1, 2);
        plan.RecommendedFps.Should().Be("120 FPS");

        var displayModel = OptimizerDisplayFormatter.Format(hardware, plan);
        displayModel.HardwareProfile.Should().Contain(plan.Tier.ToUpperInvariant());
        displayModel.PlanMemory.Should().NotBeNullOrWhiteSpace();
        displayModel.SmartPlanSummary.Should().Contain(plan.RecommendedFps);
        _output.WriteLine($"[UI Display Formatter] {displayModel.SmartPlanSummary}");

        var settingKeys = new List<string>
        {
            "VSyncEnabled",
            "GraphicsCardEnabled",
            "SetGraphicsCard",
            "VMDPI",
            "FxaaQuality",
            "LocalShaderCacheEnabled",
            "ShaderCacheEnabled",
            "RenderOptimizeEnabled",
            AppConstants.Registry.ValueAdbDisable,
            "VMMemorySizeInMB",
            "VMCpuCount"
        };

        foreach (var version in GameLoopService.PubgVersions.Keys)
        {
            settingKeys.Add($"{version}_ContentScale");
            settingKeys.Add($"{version}_RenderQuality");
            settingKeys.Add($"{version}_FPSLevel");
        }

        var originalRegistry = new Dictionary<string, int?>(StringComparer.Ordinal);
        foreach (var key in settingKeys)
        {
            originalRegistry[key] = registry.GetUserDword(key);
        }

        try
        {
            // Apply Smart Settings via GameLoopRegistryOptimizer directly and via performanceEngine
            var optimizerResult = registryOptimizer.ApplySmartSettings(hardware, plan);
            optimizerResult.Success.Should().BeTrue($"ApplySmartSettings must succeed: {optimizerResult.Message}");
            _output.WriteLine($"[Registry Optimizer Result] {optimizerResult.Message}");

            // Verify GameLoop preferences were written accurately into HKCU without Win32 exceptions
            registry.GetUserDword("VMMemorySizeInMB").Should().Be(plan.EmulatorMemoryMb);
            registry.GetUserDword("VMCpuCount").Should().Be(plan.EmulatorCpuCores);
            registry.GetUserDword(AppConstants.Registry.ValueAdbDisable).Should().Be(0);
            registry.GetUserDword("GraphicsCardEnabled").Should().Be(1);
            registry.GetUserDword("SetGraphicsCard").Should().Be(1);
            registry.GetUserDword("RenderOptimizeEnabled").Should().Be(1);
            _output.WriteLine($"[Verified Registry] VMMemorySizeInMB: {registry.GetUserDword("VMMemorySizeInMB")} MB, VMCpuCount: {registry.GetUserDword("VMCpuCount")} cores, GraphicsCardEnabled: {registry.GetUserDword("GraphicsCardEnabled")}");
        }
        finally
        {
            // Non-destructively restore original registry state
            foreach (var (key, value) in originalRegistry)
            {
                if (value.HasValue)
                {
                    registry.SetUserDword(key, value.Value);
                }
            }
            _output.WriteLine("[Registry State Restored] Successfully rolled back modified HKCU keys to original values.");
        }

        // 4a. Verify PowerSessionService directly for Windows power plan tuning
        var powerSession = new PowerSessionService(processRunner);
        var powerApplyResult = powerSession.Apply(hardware);
        powerApplyResult.Success.Should().BeTrue($"PowerSessionService.Apply must succeed: {powerApplyResult.Message}");
        _output.WriteLine($"[PowerSession Apply] {powerApplyResult.Message}");

        var powerRestoreResult = powerSession.Restore();
        powerRestoreResult.Success.Should().BeTrue($"PowerSessionService.Restore must succeed: {powerRestoreResult.Message}");
        _output.WriteLine($"[PowerSession Restore] {powerRestoreResult.Message}");

        // 4b. Verify Facade Performance Session execution & restoration
        var sessionApplyResult = performanceEngine.ApplyPerformanceSession();
        _output.WriteLine($"[ApplyPerformanceSession Result] Success: {sessionApplyResult.Success}, Message: {sessionApplyResult.Message}");
        sessionApplyResult.Message.Should().NotBeNullOrWhiteSpace();

        var sessionRestoreResult = await performanceEngine.RestorePerformanceSessionAsync(cts.Token);
        sessionRestoreResult.Success.Should().BeTrue($"RestorePerformanceSession must succeed: {sessionRestoreResult.Message}");
        _output.WriteLine($"[RestorePerformanceSession Result] {sessionRestoreResult.Message}");
    }

    [Fact]
    [Trait("Category", "LiveFunctionalVerification")]
    public async Task Step4_NetworkAndView_LiveDnsPingAndIpadLayoutVerification_NonDestructive()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var networkTools = provider.GetRequiredService<INetworkToolsService>();
        var windowsTools = provider.GetRequiredService<IWindowsToolsService>();
        var registry = provider.GetRequiredService<IRegistryService>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        DnsCatalog.Entries.Should().NotBeEmpty("DNS Catalog must have entries");
        var googleDns = DnsCatalog.Entries.First(e => e.Primary == "8.8.8.8");
        var cloudflareDns = DnsCatalog.Entries.First(e => e.Primary == "1.1.1.1");

        var googlePing = await networkTools.PingDnsAsync(googleDns.Primary, cts.Token);
        _output.WriteLine($"[DNS Latency] {googleDns.Label} -> Latency: {(googlePing.HasValue ? $"{googlePing} ms" : "Timeout / Unreachable")}");
        googlePing.Should().NotBeNull("Google DNS (8.8.8.8) should respond to ping on live network");
        googlePing!.Value.Should().BeGreaterThan(0, "Ping latency must be positive");

        var cloudflarePing = await networkTools.PingDnsAsync(cloudflareDns.Primary, cts.Token);
        _output.WriteLine($"[DNS Latency] {cloudflareDns.Label} -> Latency: {(cloudflarePing.HasValue ? $"{cloudflarePing} ms" : "Timeout / Unreachable")}");
        cloudflarePing.Should().NotBeNull("Cloudflare DNS (1.1.1.1) should respond to ping on live network");
        cloudflarePing!.Value.Should().BeGreaterThan(0, "Ping latency must be positive");

        // Cancellation token support verification
        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();
        var cancelledPing = await networkTools.PingDnsAsync(googleDns.Primary, cancelledCts.Token);
        cancelledPing.Should().BeNull("Cancelled ping attempt must safely return null without hanging or throwing unhandled exception");
        _output.WriteLine("[DNS Latency] Cancellation token correctly respected.");

        IpadPresetCatalog.Presets.Should().HaveCount(10, "Catalog should define 10 iPad resolution profiles");
        var competitivePreset = IpadPresetCatalog.Presets.First(p => p.Label.StartsWith("Competitive 4:3"));
        competitivePreset.Should().NotBeNull();
        competitivePreset.Width.Should().Be(1920);
        competitivePreset.Height.Should().Be(1440);
        IpadPresetCatalog.FindByDisplayName(competitivePreset.DisplayName).Should().Be(competitivePreset);
        _output.WriteLine($"[iPad Preset] Selected: {competitivePreset.DisplayName} ({competitivePreset.Width}x{competitivePreset.Height}) - {competitivePreset.Details}");

        // Verify execution guard: GameLoop is currently running, so SetIpadResolution MUST guard and reject
        var guardResult = windowsTools.SetIpadResolution(competitivePreset.Width, competitivePreset.Height);
        guardResult.Success.Should().BeFalse("Applying iPad resolution while GameLoop is active must be blocked");
        guardResult.Message.Should().Contain("Close GameLoop before applying iPad View");
        _output.WriteLine($"[Execution Guard Verified] {guardResult.Message}");

        var tempKeymapDir = Path.Combine(Path.GetTempPath(), $"ipad_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempKeymapDir);

        var realKeymapPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AndroidTbox", "TVM_100.xml");
        var testKeymapPath = Path.Combine(tempKeymapDir, "TVM_100.xml");

        if (File.Exists(realKeymapPath))
        {
            File.Copy(realKeymapPath, testKeymapPath, overwrite: true);
        }
        else
        {
            var minimalXml = "<Item ApkName=\"com.tencent.ig\"><KeyMapMode Name=\"1080P\"><KeyMapping ItemName=\"Fire\"><SwitchOperation Description=\"\"/></KeyMapping></KeyMapMode></Item>";
            File.WriteAllText(testKeymapPath, minimalXml);
        }

        var customOptions = new IpadLayoutOptions
        {
            KeymapDirectory = tempKeymapDir,
            KeymapFileName = "TVM_100.xml"
        };

        var ipadService = new IpadLayoutService(registry, customOptions);

        // Record initial VMResWidth / VMResHeight in HKCU
        var originalResWidth = registry.GetUserDword("VMResWidth");
        var originalResHeight = registry.GetUserDword("VMResHeight");

        try
        {
            // Apply iPad resolution
            var applyResult = ipadService.Apply(competitivePreset.Width, competitivePreset.Height);
            applyResult.Success.Should().BeTrue($"ipadService.Apply must succeed: {applyResult.Message}");
            _output.WriteLine($"[IpadLayout Apply Result] {applyResult.Message}");

            // Verify backup file was created
            File.Exists(customOptions.GetBackupFilePath()).Should().BeTrue("Backup file TVM_100.xml.mkbackup must be created");

            // Verify registry values were set
            registry.GetUserDword("VMResWidth").Should().Be(competitivePreset.Width);
            registry.GetUserDword("VMResHeight").Should().Be(competitivePreset.Height);
            _output.WriteLine($"[Verified Registry Resolution] VMResWidth: {registry.GetUserDword("VMResWidth")}, VMResHeight: {registry.GetUserDword("VMResHeight")}");

            // Reset iPad resolution
            var resetResult = ipadService.Reset();
            resetResult.Success.Should().BeTrue($"ipadService.Reset must succeed: {resetResult.Message}");
            _output.WriteLine($"[IpadLayout Reset Result] {resetResult.Message}");

            // Verify backup file was deleted upon reset
            File.Exists(customOptions.GetBackupFilePath()).Should().BeFalse("Backup file must be deleted after reset");
        }
        finally
        {
            // Clean up registry if needed
            if (originalResWidth.HasValue) registry.SetUserDword("VMResWidth", originalResWidth.Value);
            if (originalResHeight.HasValue) registry.SetUserDword("VMResHeight", originalResHeight.Value);
            if (Directory.Exists(tempKeymapDir)) Directory.Delete(tempKeymapDir, true);
            _output.WriteLine("[Cleanup] Staged keymap directory deleted and HKCU resolution restored.");
        }
    }
}
