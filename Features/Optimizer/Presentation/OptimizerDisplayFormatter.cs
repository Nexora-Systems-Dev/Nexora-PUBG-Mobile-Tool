using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;

namespace Nexora.Features.Optimizer.Presentation;

public sealed record OptimizerDisplayModel(
    string HardwareProfile,
    string HardwareGpu,
    string HardwareCpu,
    string HardwareMemory,
    string HardwareDisplay,
    string HardwarePower,
    string HardwareVirtualization,
    string PlanTier,
    string PlanCpu,
    string PlanMemory,
    string PlanRender,
    string PlanFps,
    string PlanGpu,
    string SmartPlanSummary);

/// <summary>
/// Formats hardware telemetry snapshots and recommended optimization plans into user-facing display strings.
/// </summary>
public static class OptimizerDisplayFormatter
{
    public static OptimizerDisplayModel Format(HardwareSnapshot hardware, OptimizerPlan plan)
    {
        var powerText = hardware.IsLaptop
            ? hardware.IsOnAcPower ? "AC / performance ready" : "Battery / balanced"
            : "Desktop / performance ready";

        var vtText = hardware.VirtualizationEnabled
            ? hardware.HypervisorDetected ? "VT on / hypervisor" : "VT on"
            : "VT off";

        var memoryString = FormattableString.Invariant($"{plan.EmulatorMemoryMb / 1024d:0.#} GB");

        return new OptimizerDisplayModel(
            HardwareProfile: $"{plan.Tier.ToUpperInvariant()} HARDWARE PROFILE",
            HardwareGpu: $"{hardware.GpuVendor} • {hardware.GpuName}",
            HardwareCpu: $"{hardware.PhysicalCores} cores / {hardware.LogicalCores} threads",
            HardwareMemory: $"{hardware.TotalMemoryGb} GB RAM",
            HardwareDisplay: $"{hardware.RefreshRateHz} Hz display",
            HardwarePower: powerText,
            HardwareVirtualization: vtText,
            PlanTier: $"{plan.Tier.ToUpperInvariant()} • {plan.PowerMode}",
            PlanCpu: $"{plan.EmulatorCpuCores} physical cores",
            PlanMemory: memoryString,
            PlanRender: $"{plan.ContentScale}x • FXAA {plan.FxaaQuality}",
            PlanFps: plan.RecommendedFps,
            PlanGpu: plan.GpuRoute,
            SmartPlanSummary: $"{plan.GpuRoute}; {plan.EmulatorCpuCores} CPU cores and {memoryString} recommended. Frame rate mode: {plan.RecommendedFps}.");
    }
}
