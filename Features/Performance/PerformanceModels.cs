namespace Nexora.Features.Performance;

public sealed record HardwareSnapshot(
    string CpuVendor,
    string CpuName,
    int PhysicalCores,
    int LogicalCores,
    int TotalMemoryGb,
    string GpuVendor,
    string GpuName,
    int GpuMemoryGb,
    int RefreshRateHz,
    bool IsLaptop,
    bool IsOnAcPower,
    bool VirtualizationEnabled,
    bool HypervisorDetected)
{
    public string Architecture => "GameLoop 64-bit";
    public bool HasDedicatedGpu => GpuVendorClassifier.IsDedicatedGpu(GpuVendor, GpuName);
}

public sealed record OptimizerPlan(
    string Tier,
    int EmulatorMemoryMb,
    int EmulatorCpuCores,
    int ContentScale,
    int RenderQuality,
    int FxaaQuality,
    bool EnableLocalShaderCache,
    bool EnableGlobalShaderCache,
    string RecommendedFps,
    string GpuRoute,
    string PowerMode);
