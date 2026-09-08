using FluentAssertions;
using Nexora.Features.Performance;
using Nexora.UI.Presentation;
using Xunit;

namespace Nexora.Tests.UI;

public sealed class OptimizerDisplayFormatterTests
{
    [Fact]
    public void Format_FormatsDesktopHardwareAndPlanCorrectly()
    {
        // Arrange
        var hardware = new HardwareSnapshot(
            CpuVendor: "Intel",
            CpuName: "Core i9-13900K",
            PhysicalCores: 8,
            LogicalCores: 16,
            TotalMemoryGb: 32,
            GpuVendor: "NVIDIA",
            GpuName: "RTX 4090",
            GpuMemoryGb: 24,
            RefreshRateHz: 144,
            IsLaptop: false,
            IsOnAcPower: true,
            VirtualizationEnabled: true,
            HypervisorDetected: false);

        var plan = new OptimizerPlan(
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

        // Act
        var display = OptimizerDisplayFormatter.Format(hardware, plan);

        // Assert
        display.HardwareProfile.Should().Be("HIGH HARDWARE PROFILE");
        display.HardwareGpu.Should().Be("NVIDIA • RTX 4090");
        display.HardwareCpu.Should().Be("8 cores / 16 threads");
        display.HardwareMemory.Should().Be("32 GB RAM");
        display.HardwareDisplay.Should().Be("144 Hz display");
        display.HardwarePower.Should().Be("Desktop / performance ready");
        display.HardwareVirtualization.Should().Be("VT on");

        display.PlanTier.Should().Be("HIGH • Ultimate Performance");
        display.PlanCpu.Should().Be("6 physical cores");
        display.PlanMemory.Should().Be("8 GB");
        display.PlanRender.Should().Be("2x • FXAA 1");
        display.PlanFps.Should().Be("120 FPS");
        display.PlanGpu.Should().Be("NVIDIA GeForce RTX");
        display.SmartPlanSummary.Should().Contain("NVIDIA GeForce RTX; 6 CPU cores and 8 GB recommended");
    }

    [Fact]
    public void Format_HandlesLaptopOnBatteryWithHypervisor()
    {
        // Arrange
        var hardware = new HardwareSnapshot(
            CpuVendor: "Intel",
            CpuName: "Core i7-1165G7",
            PhysicalCores: 4,
            LogicalCores: 8,
            TotalMemoryGb: 16,
            GpuVendor: "Intel",
            GpuName: "Iris Xe",
            GpuMemoryGb: 4,
            RefreshRateHz: 60,
            IsLaptop: true,
            IsOnAcPower: false,
            VirtualizationEnabled: true,
            HypervisorDetected: true);

        var plan = new OptimizerPlan(
            Tier: "entry",
            EmulatorMemoryMb: 4096,
            EmulatorCpuCores: 2,
            ContentScale: 1,
            RenderQuality: 1,
            FxaaQuality: 0,
            EnableLocalShaderCache: true,
            EnableGlobalShaderCache: false,
            RecommendedFps: "60 FPS",
            GpuRoute: "Intel Iris Xe",
            PowerMode: "Balanced");

        // Act
        var display = OptimizerDisplayFormatter.Format(hardware, plan);

        // Assert
        display.HardwarePower.Should().Be("Battery / balanced");
        display.HardwareVirtualization.Should().Be("VT on / hypervisor");
        display.PlanMemory.Should().Be("4 GB");
    }

    [Fact]
    public void Format_HandlesVirtualizationDisabled()
    {
        // Arrange
        var hardware = new HardwareSnapshot(
            CpuVendor: "AMD",
            CpuName: "Ryzen 5 5600X",
            PhysicalCores: 6,
            LogicalCores: 12,
            TotalMemoryGb: 16,
            GpuVendor: "AMD",
            GpuName: "Radeon",
            GpuMemoryGb: 8,
            RefreshRateHz: 75,
            IsLaptop: false,
            IsOnAcPower: true,
            VirtualizationEnabled: false,
            HypervisorDetected: false);

        var plan = new OptimizerPlan(
            Tier: "mid",
            EmulatorMemoryMb: 6144,
            EmulatorCpuCores: 4,
            ContentScale: 1,
            RenderQuality: 2,
            FxaaQuality: 1,
            EnableLocalShaderCache: true,
            EnableGlobalShaderCache: false,
            RecommendedFps: "90 FPS",
            GpuRoute: "AMD Radeon",
            PowerMode: "High Performance");

        // Act
        var display = OptimizerDisplayFormatter.Format(hardware, plan);

        // Assert
        display.HardwareVirtualization.Should().Be("VT off");
        display.PlanMemory.Should().Be("6 GB");
    }
}
