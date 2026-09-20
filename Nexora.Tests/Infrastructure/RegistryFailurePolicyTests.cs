using System.Diagnostics;
using FluentAssertions;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;
using Nexora.Infrastructure.Registry;
using Nexora.Shared.Contracts;

namespace Nexora.Tests.Infrastructure;

/// <summary>
/// Pins the throw-everywhere registry policy: genuine registry failures
/// surface as thrown exceptions (never collapse to false/null), while
/// legitimately-absent values still return null. Also pins that
/// GameLoopRegistryOptimizer preserves its elevation guidance now that
/// registry failures throw.
/// </summary>
public sealed class RegistryFailurePolicyTests
{
    // NOTE: there are deliberately no direct "RegistryService.<method> throws"
    // tests here. Real registry failures (access denied, I/O errors) cannot be
    // forced hermetically: OpenSubKey returns null for malformed paths instead
    // of throwing, and CreateSubKey truncates an embedded null and performs a
    // real write (a first version of these tests created a real HKCU\Invalid
    // key proving exactly that). Throw-everywhere for the six methods is
    // verified by re-read — no catch block remains in RegistryService — while
    // the tests below pin the behavior that matters: throwing fakes propagate
    // into the documented Fail results at each catching boundary.

    [Fact]
    public void ApplySmartSettings_Fails_WithExceptionMessage_WhenRegistryThrows()
    {
        // The throwing fake covers both hives, so the same instance feeds
        // both optimizer facets.
        var throwing = new ThrowingRegistryService(new UnauthorizedAccessException("Access denied."));
        var optimizer = new GameLoopRegistryOptimizer(throwing, throwing, new StubProcessService());

        var result = optimizer.ApplySmartSettings(SampleHardware(), SamplePlan());

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Smart settings could not finish");
    }

    [Fact]
    public void OptimizeGameLoopRegistry_PreservesElevationMessage_WhenCpuKeysDenied()
    {
        var throwing = new ThrowingRegistryService(new UnauthorizedAccessException("Access denied."));
        var optimizer = new GameLoopRegistryOptimizer(throwing, throwing, new StubProcessService());

        var result = optimizer.OptimizeGameLoopRegistry();

        result.Success.Should().BeFalse();
        result.Message.Should().Be("GameLoop registry optimization requires administrator privileges.");
    }

    [Fact]
    public void OptimizeGameLoopRegistry_PreservesCompletionMessage_WhenAppCompatDenied()
    {
        var denied = new AppCompatDeniedRegistryService();
        var optimizer = new GameLoopRegistryOptimizer(denied, denied, new StubProcessService());

        var result = optimizer.OptimizeGameLoopRegistry();

        result.Success.Should().BeFalse();
        result.Message.Should().Be("GameLoop registry optimization could not be completed.");
    }

    private static HardwareSnapshot SampleHardware() => new(
        CpuVendor: "Intel",
        CpuName: "Core i7",
        PhysicalCores: 8,
        LogicalCores: 16,
        TotalMemoryGb: 16,
        GpuVendor: "NVIDIA",
        GpuName: "RTX 4060",
        GpuMemoryGb: 8,
        RefreshRateHz: 144,
        IsLaptop: false,
        IsOnAcPower: true,
        VirtualizationEnabled: true,
        HypervisorDetected: false);

    private static OptimizerPlan SamplePlan() => new(
        Tier: "high",
        EmulatorMemoryMb: 8192,
        EmulatorCpuCores: 6,
        ContentScale: 2,
        RenderQuality: 2,
        FxaaQuality: 1,
        EnableLocalShaderCache: true,
        EnableGlobalShaderCache: false,
        RecommendedFps: "120 FPS",
        GpuRoute: "NVIDIA GeForce RTX",
        PowerMode: "Ultimate Performance");

    private sealed class StubProcessService : IGameLoopProcessService
    {
        public OperationResult KillGameLoopProcesses(CancellationToken cancellationToken = default) =>
            OperationResult.Ok("No GameLoop processes were found.");

        public List<Process> FindGameLoopProcesses(string? gameLoopRoot = null) => new();

        public string? GetGameLoopRootFromRegistry() => @"C:\GameLoop";

        public string? GetGameLoopRoot() => @"C:\GameLoop";

        public string? GetGameLoopUiPath() => @"C:\GameLoop\UI";
    }

    private sealed class ThrowingRegistryService(Exception failure) : IUserRegistry, IMachineRegistry
    {
        public int? GetUserDword(string name) => throw failure;

        public bool SetUserDword(string name, int value) => throw failure;

        public int? GetAppSettingDword(string name) => throw failure;

        public void SetAppSettingDword(string name, int value) => throw failure;

        public void DeleteAppSetting(string name) => throw failure;

        public string? GetLocalString(string name, string? branch = null) => throw failure;

        public bool SetLocalMachineDword(string subKeyPath, string name, int value) => throw failure;

        public int? GetLocalMachineDword(string subKeyPath, string name) => throw failure;

        public bool SetCurrentUserString(string subKeyPath, string name, string value) => throw failure;

        public string? GetCurrentUserString(string subKeyPath, string name) => throw failure;
    }

    private sealed class AppCompatDeniedRegistryService : IUserRegistry, IMachineRegistry
    {
        private static readonly UnauthorizedAccessException Denied = new("Access denied.");

        public int? GetUserDword(string name) => null;

        public bool SetUserDword(string name, int value) => true;

        public int? GetAppSettingDword(string name) => null;

        public void SetAppSettingDword(string name, int value)
        {
        }

        public void DeleteAppSetting(string name)
        {
        }

        public string? GetLocalString(string name, string? branch = null) => null;

        public bool SetLocalMachineDword(string subKeyPath, string name, int value) => true;

        public int? GetLocalMachineDword(string subKeyPath, string name) => 3;

        public bool SetCurrentUserString(string subKeyPath, string name, string value) => throw Denied;

        public string? GetCurrentUserString(string subKeyPath, string name) => throw Denied;
    }
}
