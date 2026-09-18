using System.Diagnostics;
using FluentAssertions;
using Nexora.Features.GameLoop;
using Nexora.Features.Performance;
using Nexora.Services.Performance;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

// Journeys:
//  As a player, I want the Tuning page to show the emulator's current CPU/RAM/DPI/toggle
//  values, so that I edit reality instead of guessing.
//  As a player, I want one Apply to force all ten settings into the registry with a
//  per-setting report, so that the emulator runs with least lag and max stability.
//  As a player, I want Apply to refuse while GameLoop runs, so that the emulator
//  cannot overwrite my settings on exit.
public sealed class EmulatorSettingsServiceTests
{
    [Fact]
    public async Task LoadAsync_ReturnsCurrentRegistryValues()
    {
        var registry = new MemoryRegistry();
        registry.SetUserDword(EmulatorTuningCatalog.CpuCoresName, 4);
        registry.SetUserDword(EmulatorTuningCatalog.MemoryMbName, 4096);
        registry.SetUserDword(EmulatorTuningCatalog.DpiName, 240);
        registry.SetUserDword(EmulatorTuningCatalog.RenderCacheName, 1);
        registry.SetUserDword(EmulatorTuningCatalog.GlobalCacheName, 1);
        registry.SetUserDword(EmulatorTuningCatalog.DiscreteGpuName, 1);
        registry.SetUserDword(EmulatorTuningCatalog.RenderOptimizeName, 0);
        registry.SetUserDword(EmulatorTuningCatalog.VSyncName, 0);
        registry.SetUserDword(EmulatorTuningCatalog.AdbDisableName, 0);
        registry.SetUserDword(EmulatorTuningCatalog.AntiAliasingName, 1);
        var service = CreateService(registry);

        var state = await service.LoadAsync();

        state.IsGameLoopRunning.Should().BeFalse();
        state.Selection.CpuCores.Should().Be(4);
        state.Selection.MemoryMb.Should().Be(4096);
        state.Selection.Dpi.Should().Be(240);
        state.Selection.RenderCacheEnabled.Should().BeTrue();
        state.Selection.RenderOptimizeEnabled.Should().BeFalse();
        state.Selection.AdbEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_MissingValues_UsesClampedDefaults()
    {
        var service = CreateService(new MemoryRegistry(), logicalCores: 2, totalMemoryGb: 4);

        var state = await service.LoadAsync();

        state.Selection.CpuCores.Should().BeInRange(1, 2);
        state.Selection.MemoryMb.Should().BeInRange(
            EmulatorTuningCatalog.MinMemoryMb, 4 * 1024);
        state.Selection.Dpi.Should().Be(EmulatorTuningCatalog.DefaultDpi);
    }

    [Fact]
    public async Task LoadAsync_ReportsGameLoopRunning()
    {
        using var running = new Process();
        var service = CreateService(
            new MemoryRegistry(),
            processes: new List<Process> { running });

        var state = await service.LoadAsync();

        state.IsGameLoopRunning.Should().BeTrue();
        state.RunningProcessNames.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_WhenGameLoopRunning_RefusesWithoutWriting()
    {
        using var running = new Process();
        var registry = new MemoryRegistry();
        var service = CreateService(
            registry,
            processes: new List<Process> { running });

        var result = await service.ApplyAsync(BalancedSelection());

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Close GameLoop");
        registry.WrittenNames.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_WritesAllTenControls_WithReadBackVerify()
    {
        var registry = new MemoryRegistry();
        var service = CreateService(registry);

        var result = await service.ApplyAsync(new EmulatorTuningSelection(
            CpuCores: 4,
            MemoryMb: 4096,
            Dpi: 240,
            RenderCacheEnabled: true,
            GlobalCacheEnabled: true,
            DiscreteGpuEnabled: true,
            RenderOptimizeEnabled: true,
            VSyncEnabled: false,
            AdbEnabled: true,
            AntiAliasingEnabled: true));

        result.Success.Should().BeTrue();
        registry.GetUserDword(EmulatorTuningCatalog.CpuCoresName).Should().Be(4);
        registry.GetUserDword(EmulatorTuningCatalog.MemoryMbName).Should().Be(4096);
        registry.GetUserDword(EmulatorTuningCatalog.DpiName).Should().Be(240);
        registry.GetUserDword(EmulatorTuningCatalog.RenderCacheName).Should().Be(1);
        registry.GetUserDword(EmulatorTuningCatalog.VSyncName).Should().Be(0);
        // Inverted semantics live in exactly one place: ON means AdbDisable=0.
        registry.GetUserDword(EmulatorTuningCatalog.AdbDisableName).Should().Be(0);
        // Discrete graphics is a paired write: both values move together.
        registry.GetUserDword(EmulatorTuningCatalog.DiscreteGpuName).Should().Be(1);
        registry.GetUserDword(EmulatorTuningCatalog.DiscreteGpuPairedName).Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_ClampsCpuCores_ToHardwareLimit()
    {
        var registry = new MemoryRegistry();
        var service = CreateService(registry, logicalCores: 4);

        var result = await service.ApplyAsync(BalancedSelection() with { CpuCores = 8 });

        result.Success.Should().BeTrue();
        registry.GetUserDword(EmulatorTuningCatalog.CpuCoresName).Should().Be(4);
    }

    [Fact]
    public async Task ApplyAsync_ClampsCpuCores_ToCatalogMaximum()
    {
        var registry = new MemoryRegistry();
        var service = CreateService(registry, logicalCores: 32);

        var result = await service.ApplyAsync(BalancedSelection() with { CpuCores = 32 });

        result.Success.Should().BeTrue();
        registry.GetUserDword(EmulatorTuningCatalog.CpuCoresName)
            .Should().Be(EmulatorTuningCatalog.MaxCpuCores);
    }

    [Fact]
    public async Task ApplyAsync_ClampsMemory_ToPhysicalRam()
    {
        var registry = new MemoryRegistry();
        var service = CreateService(registry, totalMemoryGb: 16);

        var result = await service.ApplyAsync(BalancedSelection() with { MemoryMb = 32768 });

        result.Success.Should().BeTrue();
        registry.GetUserDword(EmulatorTuningCatalog.MemoryMbName).Should().Be(16 * 1024);
    }

    [Fact]
    public async Task ApplyAsync_RejectsUnknownDpi_WithoutWritingAnything()
    {
        var registry = new MemoryRegistry();
        var service = CreateService(registry);

        var result = await service.ApplyAsync(BalancedSelection() with { Dpi = 999 });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("DPI");
        registry.WrittenNames.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_AdbDisabled_MapsToAdbDisableOne()
    {
        var registry = new MemoryRegistry();
        var service = CreateService(registry);

        var result = await service.ApplyAsync(BalancedSelection() with { AdbEnabled = false });

        result.Success.Should().BeTrue();
        registry.GetUserDword(EmulatorTuningCatalog.AdbDisableName).Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_WhenWriteFails_ReturnsFailure()
    {
        var service = CreateService(new MemoryRegistry { FailWrites = true });

        var result = await service.ApplyAsync(BalancedSelection());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WhenReadBackMismatches_ReturnsFailure()
    {
        var service = CreateService(new MemoryRegistry { DropWrites = true });

        var result = await service.ApplyAsync(BalancedSelection());

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(EmulatorTuningCatalog.CpuCoresName);
    }

    [Fact]
    public async Task ApplyAsync_PreCancelledToken_ThrowsOperationCanceled()
    {
        var service = CreateService(new MemoryRegistry());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.ApplyAsync(BalancedSelection(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Constructor_Throws_WhenRegistryNull()
    {
        var act = () => new EmulatorSettingsService(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("registry");
    }

    [Fact]
    public void Catalog_RegistryNames_AreUnique()
    {
        var names = new[]
        {
            EmulatorTuningCatalog.CpuCoresName,
            EmulatorTuningCatalog.MemoryMbName,
            EmulatorTuningCatalog.DpiName,
            EmulatorTuningCatalog.RenderCacheName,
            EmulatorTuningCatalog.GlobalCacheName,
            EmulatorTuningCatalog.DiscreteGpuName,
            EmulatorTuningCatalog.DiscreteGpuPairedName,
            EmulatorTuningCatalog.RenderOptimizeName,
            EmulatorTuningCatalog.VSyncName,
            EmulatorTuningCatalog.AdbDisableName,
            EmulatorTuningCatalog.AntiAliasingName,
        };

        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Catalog_DpiOptions_ContainDefault()
    {
        EmulatorTuningCatalog.DpiOptions.Should().Contain(EmulatorTuningCatalog.DefaultDpi);
    }

    [Fact]
    public async Task LoadAsync_DoesNotBlockCallerWhileSnapshotIsInFlight()
    {
        using var gate = new ManualResetEventSlim(false);
        var service = new EmulatorSettingsService(
            new MemoryRegistry(),
            new FixedProcessService(new List<Process>()),
            async _ =>
            {
                // Runs on a worker; the caller's await must stay free until released.
                await Task.Run(() => gate.Wait(TimeSpan.FromSeconds(30)));
                return TestSnapshot();
            });

        var load = service.LoadAsync();
        var first = await Task.WhenAny(load, Task.Delay(TimeSpan.FromSeconds(5)));
        first.Should().NotBe(load, "LoadAsync must await the snapshot instead of blocking the caller (QA F-002).");
        gate.Set();

        (await load).Selection.CpuCores.Should().BeInRange(1, 8);
    }

    [Fact]
    public async Task LoadAsync_CachesHardwareSnapshotForSession()
    {
        var calls = 0;
        var service = new EmulatorSettingsService(
            new MemoryRegistry(),
            new FixedProcessService(new List<Process>()),
            _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(TestSnapshot());
            });

        await service.LoadAsync();
        await service.LoadAsync();

        calls.Should().Be(1, "hardware does not change at runtime; the first scan is the session snapshot.");
    }

    private static HardwareSnapshot TestSnapshot(int logicalCores = 8, int totalMemoryGb = 16) => new(
        "Test CPU vendor", "Test CPU",
        PhysicalCores: Math.Max(1, logicalCores / 2),
        LogicalCores: logicalCores,
        TotalMemoryGb: totalMemoryGb,
        "Test GPU vendor", "Test GPU", 0, 60, false, true, false, false);

    private static EmulatorTuningSelection BalancedSelection() => new(
        CpuCores: 4,
        MemoryMb: 4096,
        Dpi: EmulatorTuningCatalog.DefaultDpi,
        RenderCacheEnabled: true,
        GlobalCacheEnabled: true,
        DiscreteGpuEnabled: true,
        RenderOptimizeEnabled: true,
        VSyncEnabled: false,
        AdbEnabled: true,
        AntiAliasingEnabled: true);

    private static EmulatorSettingsService CreateService(
        MemoryRegistry registry,
        int logicalCores = 8,
        int totalMemoryGb = 16,
        List<Process>? processes = null) => new(
            registry,
            new FixedProcessService(processes ?? new List<Process>()),
            _ => Task.FromResult(TestSnapshot(logicalCores, totalMemoryGb)));

    private sealed class FixedProcessService(List<Process> processes) : IGameLoopProcessService
    {
        public OperationResult KillGameLoopProcesses(CancellationToken cancellationToken = default) =>
            OperationResult.Ok("Stub kill.");

        public List<Process> FindGameLoopProcesses(string? gameLoopRoot = null) => processes;

        public string? GetGameLoopRootFromRegistry() => null;

        public string? GetGameLoopRoot() => null;

        public string? GetGameLoopUiPath() => null;
    }

    private sealed class MemoryRegistry : IUserRegistry
    {
        private readonly Dictionary<string, int> _userDwords = new(StringComparer.OrdinalIgnoreCase);

        public bool FailWrites { get; set; }

        public bool DropWrites { get; set; }

        public IReadOnlyList<string> WrittenNames => _writtenNames;
        private readonly List<string> _writtenNames = new();

        public int? GetUserDword(string name) =>
            _userDwords.TryGetValue(name, out var value) ? value : null;

        public bool SetUserDword(string name, int value)
        {
            if (FailWrites) return false;
            _writtenNames.Add(name);
            if (!DropWrites) _userDwords[name] = value;
            return true;
        }

        public int? GetAppSettingDword(string name) => null;

        public void SetAppSettingDword(string name, int value)
        {
        }

        public void DeleteAppSetting(string name)
        {
        }

        public bool SetCurrentUserString(string subKeyPath, string name, string value) => false;

        public string? GetCurrentUserString(string subKeyPath, string name) => null;
    }
}
