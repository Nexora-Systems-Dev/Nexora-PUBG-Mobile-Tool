namespace Nexora.Features.GameLoop;

/// <summary>
/// Single home for every Emulator Tuning control: registry value names,
/// slider bounds, DPI allow-list, and load defaults. Nothing outside this
/// catalog may hardcode a tuning value name.
/// </summary>
public static class EmulatorTuningCatalog
{
    public const string CpuCoresName = "VMCpuCount";
    public const string MemoryMbName = "VMMemorySizeInMB";
    public const string DpiName = "VMDPI";
    public const string RenderCacheName = "LocalShaderCacheEnabled";
    public const string GlobalCacheName = "ShaderCacheEnabled";
    public const string DiscreteGpuName = "GraphicsCardEnabled";
    public const string DiscreteGpuPairedName = "SetGraphicsCard";
    public const string RenderOptimizeName = "RenderOptimizeEnabled";
    public const string VSyncName = "VSyncEnabled";
    public const string AdbDisableName = "AdbDisable";
    public const string AntiAliasingName = "FxaaQuality";

    public const int MinCpuCores = 1;
    public const int MaxCpuCores = 8;
    public const int DefaultCpuCores = 4;

    public const int MinMemoryMb = 1024;
    public const int MemoryStepMb = 512;
    public const int DefaultMemoryMb = 4096;

    public const int DefaultDpi = 320;

    /// <summary>
    /// DPI values GameLoop accepts (Smart Settings writes 240/480 itself).
    /// Anything else is rejected before any write happens.
    /// </summary>
    public static readonly int[] DpiOptions = [120, 160, 240, 320, 480];
}
