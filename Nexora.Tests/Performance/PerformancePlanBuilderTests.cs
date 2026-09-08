using FluentAssertions;
using Nexora.Features.Performance;
using Nexora.Services.Performance;
using Xunit;

namespace Nexora.Tests.Performance;

public sealed class PerformancePlanBuilderTests
{
    private readonly PerformancePlanBuilder _sut = new();

    [Fact]
    public void Build_ReturnsEntryPlan_WhenHardwareIsLimited()
    {
        // Arrange: integrated GPU with no VRAM, 8 GB RAM, 4 CPU cores.
        var hardware = new HardwareSnapshot(
            "GenuineIntel", "Intel Core i3-10100", 4, 8, 8,
            "Intel", "Intel UHD Graphics 630", 0, 60,
            false, true, false, false);

        // Act
        var plan = _sut.Build(hardware);

        // Assert: conservative profile that never oversells weak hardware.
        plan.Tier.Should().Be("Entry");
        plan.EmulatorMemoryMb.Should().Be(4096);
        plan.EmulatorCpuCores.Should().Be(3);
        plan.ContentScale.Should().Be(1);
        plan.RenderQuality.Should().Be(2);
        plan.FxaaQuality.Should().Be(0);
        plan.EnableLocalShaderCache.Should().BeTrue();
        plan.EnableGlobalShaderCache.Should().BeFalse();
        plan.RecommendedFps.Should().Be("120 FPS");
        plan.GpuRoute.Should().Be("Integrated Intel UHD Graphics 630");
        plan.PowerMode.Should().Be("High performance available");
    }

    [Fact]
    public void Build_ReturnsPerformancePlan_WhenHardwareIsHighEnd()
    {
        // Arrange: dedicated NVIDIA GPU with ample VRAM, RAM, and cores.
        var hardware = new HardwareSnapshot(
            "GenuineIntel", "Intel Core i7-13700K", 8, 16, 32,
            "NVIDIA", "NVIDIA GeForce RTX 4070", 12, 144,
            false, true, true, false);

        // Act
        var plan = _sut.Build(hardware);

        // Assert
        plan.Tier.Should().Be("Performance");
        plan.EmulatorMemoryMb.Should().Be(8192);
        plan.EmulatorCpuCores.Should().Be(6);
        plan.ContentScale.Should().Be(2);
        plan.FxaaQuality.Should().Be(2);
        plan.GpuRoute.Should().Be("High-performance NVIDIA GeForce RTX 4070");
        plan.PowerMode.Should().Be("High performance available");
    }

    [Fact]
    public void Build_ReturnsBalancedPlan_WhenHardwareIsMidRange()
    {
        // Arrange: dedicated GPU but only 3 GB VRAM blocks the Performance tier.
        var hardware = new HardwareSnapshot(
            "AuthenticAMD", "AMD Ryzen 5 5600", 6, 12, 16,
            "NVIDIA", "NVIDIA GeForce GTX 1650", 3, 75,
            false, true, false, false);

        // Act
        var plan = _sut.Build(hardware);

        // Assert
        plan.Tier.Should().Be("Balanced");
        plan.EmulatorMemoryMb.Should().Be(8192);
        plan.EmulatorCpuCores.Should().Be(5);
        plan.ContentScale.Should().Be(2);
        plan.FxaaQuality.Should().Be(2);
        plan.RecommendedFps.Should().Be("120 FPS");
    }

    [Fact]
    public void Build_ClampsMemoryToFloor_WhenSystemRamIsVeryLow()
    {
        // Arrange: 4 GB machine where the reserve would otherwise zero the budget.
        var hardware = new HardwareSnapshot(
            "GenuineIntel", "Intel Celeron N4020", 2, 2, 4,
            "Intel", "Intel UHD Graphics 600", 0, 60,
            false, true, false, false);

        // Act
        var plan = _sut.Build(hardware);

        // Assert: never below the 2048 MB emulator minimum.
        plan.Tier.Should().Be("Entry");
        plan.EmulatorMemoryMb.Should().Be(2048);
        plan.EmulatorCpuCores.Should().Be(2);
    }

    [Theory]
    [InlineData(true, true, "Performance on AC")]
    [InlineData(true, false, "Balanced on battery")]
    [InlineData(false, false, "High performance available")]
    public void Build_SelectsPowerMode_FromFormFactorAndAcState(
        bool isLaptop, bool isOnAcPower, string expectedPowerMode)
    {
        // Arrange
        var hardware = new HardwareSnapshot(
            "GenuineIntel", "Intel Core i5-1135G7", 4, 8, 8,
            "Intel", "Intel Iris Xe Graphics", 1, 60,
            isLaptop, isOnAcPower, false, false);

        // Act
        var plan = _sut.Build(hardware);

        // Assert
        plan.PowerMode.Should().Be(expectedPowerMode);
    }
}
