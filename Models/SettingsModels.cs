namespace Nexora.Models;

public sealed record PubgVersion(string PackageName, string DisplayName);

public sealed record ConnectionResult(
    bool Success,
    string Message,
    IReadOnlyList<PubgVersion> InstalledVersions);

public sealed record OperationResult(bool Success, string Message)
{
    public static OperationResult Ok(string message) => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}

public sealed record GraphicsSelection(
    string Quality,
    string FrameRate,
    string Style,
    bool EnableShadow,
    bool EnableKoreanFullHd);

public sealed record IpadResolutionPreset(
    string Label,
    int Width,
    int Height,
    string Guidance)
{
    public string DisplayName => $"{Label}  •  {Width} × {Height}";
    public string Details => $"{Width} × {Height}  •  {Guidance}";
}

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
    public bool HasDedicatedGpu => GpuVendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
        || GpuName.Contains("RTX", StringComparison.OrdinalIgnoreCase)
        || GpuName.Contains("GTX", StringComparison.OrdinalIgnoreCase)
        || GpuName.Contains("RX", StringComparison.OrdinalIgnoreCase)
        || GpuName.Contains("Arc", StringComparison.OrdinalIgnoreCase);
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
