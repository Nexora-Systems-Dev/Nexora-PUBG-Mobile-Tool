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

    public OptimizerPlan Build(HardwareSnapshot hardware)
    {
        // Treat unknown or zero VRAM as limited to avoid overly aggressive profiles on detection failure.
        var limitedGpu = hardware.GpuMemoryGb <= LimitedVramGb;
        var entryLevel = hardware.TotalMemoryGb <= SmallMemoryGb || hardware.PhysicalCores <= FewCores || limitedGpu;
        var performanceLevel = hardware.TotalMemoryGb >= LargeMemoryGb && hardware.PhysicalCores >= ManyCores &&
            hardware.HasDedicatedGpu && hardware.GpuMemoryGb >= PerformanceVramGb;
        string tier;
        if (performanceLevel)
        {
            tier = "Performance";
        }
        else if (entryLevel)
        {
            tier = "Entry";
        }
        else
        {
            tier = "Balanced";
        }

        int reserveMb;
        if (hardware.TotalMemoryGb <= SmallMemoryGb)
        {
            reserveMb = SmallMemoryReserveMb;
        }
        else if (hardware.TotalMemoryGb <= LargeMemoryGb)
        {
            reserveMb = MediumMemoryReserveMb;
        }
        else
        {
            reserveMb = LargeMemoryReserveMb;
        }

        var memoryMb = Math.Clamp(hardware.TotalMemoryGb * 1024 - reserveMb, MinMemoryMb, MaxMemoryMb);
        memoryMb = Math.Max(MinMemoryMb, memoryMb / MemoryStepMb * MemoryStepMb);

        var cpuCores = hardware.PhysicalCores <= FewCores
            ? Math.Max(SmallMachineCoreFloor, hardware.PhysicalCores - 1)
            : Math.Min(MaxCpuCores, Math.Max(MinCpuCores, (int)Math.Round(hardware.PhysicalCores * CpuShareRatio, MidpointRounding.AwayFromZero)));

        var lowRenderPath = entryLevel || (!hardware.HasDedicatedGpu && hardware.GpuMemoryGb < PerformanceVramGb);
        var contentScale = lowRenderPath ? LowContentScale : HighContentScale;
        int fxaaQuality;
        if (limitedGpu)
        {
            fxaaQuality = DisabledFxaa;
        }
        else if (hardware.HasDedicatedGpu)
        {
            fxaaQuality = DedicatedFxaa;
        }
        else
        {
            fxaaQuality = IntegratedFxaa;
        }

        // Target 120 FPS recommendation while leaving actual runtime support to the emulator and game.
        const string recommendedFps = "120 FPS";
        var gpuRoute = hardware.HasDedicatedGpu
            ? $"High-performance {hardware.GpuName}"
            : $"Integrated {hardware.GpuName}";
        string powerMode;
        if (!hardware.IsLaptop)
        {
            powerMode = "High performance available";
        }
        else if (hardware.IsOnAcPower)
        {
            powerMode = "Performance on AC";
        }
        else
        {
            powerMode = "Balanced on battery";
        }

        return new OptimizerPlan(
            tier,
            memoryMb,
            cpuCores,
            contentScale,
            RenderQuality: DefaultRenderQuality,
            fxaaQuality,
            EnableLocalShaderCache: true,
            EnableGlobalShaderCache: false,
            recommendedFps,
            gpuRoute,
            powerMode);
    }
}
