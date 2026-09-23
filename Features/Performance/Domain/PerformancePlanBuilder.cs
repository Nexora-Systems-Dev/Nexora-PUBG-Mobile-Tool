using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;

namespace Nexora.Features.Performance.Domain;

/// <summary>
/// Evaluates hardware characteristics to build an optimizer configuration plan.
/// </summary>
public sealed class PerformancePlanBuilder
{
    // Hardware-tier thresholds (GB RAM/VRAM, physical core counts).
    private const int LimitedVramGb = 2;
    private const int SmallMemoryGb = 8;
    private const int LargeMemoryGb = 16;
    private const int FewCores = 4;
    private const int ManyCores = 6;
    private const int PerformanceVramGb = 4;

    // Memory-allocation ladder (MB).
    private const int SmallMemoryReserveMb = 4096;
    private const int MediumMemoryReserveMb = 6144;
    private const int LargeMemoryReserveMb = 8192;
    private const int MinMemoryMb = 2048;
    private const int MaxMemoryMb = 8192;
    private const int MemoryStepMb = 512;

    // CPU-share tuning.
    private const int SmallMachineCoreFloor = 2;
    private const int MinCpuCores = 4;
    private const int MaxCpuCores = 8;
    private const double CpuShareRatio = 0.75;

    // Render-quality ladder.
    private const int LowContentScale = 1;
    private const int HighContentScale = 2;
    private const int DisabledFxaa = 0;
    private const int IntegratedFxaa = 1;
    private const int DedicatedFxaa = 2;
    private const int DefaultRenderQuality = 2;

    // Tier names — asserted verbatim by PerformancePlanBuilderTests.
    private const string PerformanceTier = "Performance";
    private const string EntryTier = "Entry";
    private const string BalancedTier = "Balanced";

    // Power-mode labels — asserted verbatim by PerformancePlanBuilderTests.
    private const string HighPerformanceMode = "High performance available";
    private const string PerformanceOnAcMode = "Performance on AC";
    private const string BalancedOnBatteryMode = "Balanced on battery";

    // Target 120 FPS while leaving actual runtime support to the emulator and game.
    private const string RecommendedFps = "120 FPS";

    public OptimizerPlan Build(HardwareSnapshot hardware)
    {
        // Treat unknown or zero VRAM as limited to avoid overly aggressive profiles on detection failure.
        var limitedGpu = hardware.GpuMemoryGb <= LimitedVramGb;
        var entryLevel = hardware.TotalMemoryGb <= SmallMemoryGb || hardware.PhysicalCores <= FewCores || limitedGpu;

        // Each plan axis is an independent pure decision on the same snapshot.
        var render = ChooseRenderProfile(hardware, entryLevel, limitedGpu);
        return new OptimizerPlan(
            ClassifyTier(hardware, entryLevel),
            CalculateMemoryMb(hardware.TotalMemoryGb),
            CalculateCpuCores(hardware.PhysicalCores),
            render.ContentScale,
            RenderQuality: DefaultRenderQuality,
            render.FxaaQuality,
            EnableLocalShaderCache: true,
            EnableGlobalShaderCache: false,
            RecommendedFps,
            ChooseGpuRoute(hardware),
            ChoosePowerMode(hardware));
    }

    /// <summary>
    /// Performance needs every dimension strong; Entry is weak hardware or a
    /// limited GPU; anything between is Balanced.
    /// </summary>
    private static string ClassifyTier(HardwareSnapshot hardware, bool entryLevel) =>
        hardware.TotalMemoryGb >= LargeMemoryGb && hardware.PhysicalCores >= ManyCores &&
            hardware.HasDedicatedGpu && hardware.GpuMemoryGb >= PerformanceVramGb
                ? PerformanceTier
                : entryLevel ? EntryTier : BalancedTier;

    /// <summary>
    /// The emulator memory budget: subtract a reserve that scales with the
    /// machine, clamp to the supported range, then round down to the allocation step.
    /// </summary>
    private static int CalculateMemoryMb(int totalMemoryGb)
    {
        var reserveMb = totalMemoryGb <= SmallMemoryGb ? SmallMemoryReserveMb
            : totalMemoryGb <= LargeMemoryGb ? MediumMemoryReserveMb
            : LargeMemoryReserveMb;

        var memoryMb = Math.Clamp(totalMemoryGb * 1024 - reserveMb, MinMemoryMb, MaxMemoryMb);
        return Math.Max(MinMemoryMb, memoryMb / MemoryStepMb * MemoryStepMb);
    }

    /// <summary>
    /// Small machines keep a core for the host; others get a fixed share of
    /// physical cores clamped to the emulator's supported range.
    /// </summary>
    private static int CalculateCpuCores(int physicalCores) =>
        physicalCores <= FewCores
            ? Math.Max(SmallMachineCoreFloor, physicalCores - 1)
            : Math.Min(MaxCpuCores, Math.Max(MinCpuCores, (int)Math.Round(physicalCores * CpuShareRatio, MidpointRounding.AwayFromZero)));

    /// <summary>
    /// The render ladder: content scale drops on a weak GPU path, and FXAA is
    /// disabled on limited VRAM and split integrated/dedicated otherwise.
    /// </summary>
    private static (int ContentScale, int FxaaQuality) ChooseRenderProfile(HardwareSnapshot hardware, bool entryLevel, bool limitedGpu)
    {
        var lowRenderPath = entryLevel || (!hardware.HasDedicatedGpu && hardware.GpuMemoryGb < PerformanceVramGb);
        var contentScale = lowRenderPath ? LowContentScale : HighContentScale;
        var fxaaQuality = limitedGpu ? DisabledFxaa
            : hardware.HasDedicatedGpu ? DedicatedFxaa
            : IntegratedFxaa;

        return (contentScale, fxaaQuality);
    }

    /// <summary>The GPU route names the device the emulator should render on.</summary>
    private static string ChooseGpuRoute(HardwareSnapshot hardware) =>
        hardware.HasDedicatedGpu ? $"High-performance {hardware.GpuName}" : $"Integrated {hardware.GpuName}";

    /// <summary>Laptops throttle on battery; desktops always advertise high performance.</summary>
    private static string ChoosePowerMode(HardwareSnapshot hardware) =>
        !hardware.IsLaptop ? HighPerformanceMode
        : hardware.IsOnAcPower ? PerformanceOnAcMode
        : BalancedOnBatteryMode;
}
