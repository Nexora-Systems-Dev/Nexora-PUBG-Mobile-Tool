using Nexora.Models;

namespace Nexora.Services.Performance;

/// <summary>
/// Pure decision logic for the optimizer. It never writes to Windows or GameLoop.
/// </summary>
public sealed class PerformancePlanBuilder
{
    public OptimizerPlan Build(HardwareSnapshot hardware)
    {
        // Keep the legacy fallback behavior: unknown/zero VRAM is treated as
        // limited so detection failures never lead to an aggressive profile.
        var limitedGpu = hardware.GpuMemoryGb <= 2;
        var entryLevel = hardware.TotalMemoryGb <= 8 || hardware.PhysicalCores <= 4 || limitedGpu;
        var performanceLevel = hardware.TotalMemoryGb >= 16 && hardware.PhysicalCores >= 6 &&
            hardware.HasDedicatedGpu && hardware.GpuMemoryGb >= 4;
        var tier = performanceLevel ? "Performance" : entryLevel ? "Entry" : "Balanced";

        var reserveMb = hardware.TotalMemoryGb <= 8 ? 4096 : hardware.TotalMemoryGb <= 16 ? 6144 : 8192;
        var memoryMb = Math.Clamp(hardware.TotalMemoryGb * 1024 - reserveMb, 2048, 8192);
        memoryMb = Math.Max(2048, memoryMb / 512 * 512);

        var cpuCores = hardware.PhysicalCores <= 4
            ? Math.Max(2, hardware.PhysicalCores - 1)
            : Math.Min(8, Math.Max(4, (int)Math.Round(hardware.PhysicalCores * 0.75, MidpointRounding.AwayFromZero)));

        var lowRenderPath = entryLevel || (!hardware.HasDedicatedGpu && hardware.GpuMemoryGb < 4);
        var contentScale = lowRenderPath ? 1 : 2;
        var fxaaQuality = limitedGpu ? 0 : hardware.HasDedicatedGpu ? 2 : 1;

        // The optimizer must not downgrade the user's frame-rate mode. GameLoop
        // and PUBG remain responsible for exposing supported modes up to 120 FPS.
        const string recommendedFps = "120 FPS";
        var gpuRoute = hardware.HasDedicatedGpu
            ? $"High-performance {hardware.GpuName}"
            : $"Integrated {hardware.GpuName}";
        var powerMode = hardware.IsLaptop
            ? hardware.IsOnAcPower ? "Performance on AC" : "Balanced on battery"
            : "High performance available";

        return new OptimizerPlan(
            tier,
            memoryMb,
            cpuCores,
            contentScale,
            RenderQuality: 2,
            fxaaQuality,
            EnableLocalShaderCache: true,
            EnableGlobalShaderCache: false,
            recommendedFps,
            gpuRoute,
            powerMode);
    }
}
