namespace Nexora.Features.Performance.Infrastructure;

/// <summary>
/// Detected GPU vendor from a Windows video-controller description string.
/// </summary>
public enum GpuVendor
{
    Nvidia,
    Intel,
    Amd,
    Unknown
}

/// <summary>
/// Single home for GPU classification: vendor detection from a description
/// string and the discrete-GPU test behind <see cref="HardwareSnapshot.HasDedicatedGpu"/>.
/// Both previously lived as independent substring heuristics that could disagree
/// on the same card (e.g. Intel Arc: discrete by name markers, Intel by vendor).
/// </summary>
public static class GpuVendorClassifier
{
    private const string NvidiaMarker = "NVIDIA";

    private static readonly string[] AmdMarkers =
    [
        "AMD",
        "Radeon",
        "Advanced Micro Devices"
    ];

    private static readonly string[] DiscreteNameMarkers =
    [
        "RTX",
        "GTX",
        "RX",
        "Arc"
    ];

    public static GpuVendor Classify(string? providerText)
    {
        if (string.IsNullOrWhiteSpace(providerText)) return GpuVendor.Unknown;
        if (HasNvidiaMarker(providerText)) return GpuVendor.Nvidia;
        if (providerText.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Intel;
        if (AmdMarkers.Any(marker => providerText.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return GpuVendor.Amd;
        }

        return GpuVendor.Unknown;
    }

    /// <summary>
    /// Discrete-GPU test: an NVIDIA vendor string or a discrete model marker in
    /// the adapter name. An AMD vendor string alone is deliberately not enough —
    /// integrated Radeons carry it too, so those still need an RX name marker.
    /// </summary>
    public static bool IsDedicatedGpu(string? gpuVendor, string? gpuName) =>
        HasNvidiaMarker(gpuVendor) || DiscreteNameMarkers.Any(marker =>
            gpuName?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>
    /// NVIDIA wins over Intel in the vendor ladder and alone proves a discrete
    /// GPU, so both decisions share one marker test and cannot drift apart.
    /// </summary>
    private static bool HasNvidiaMarker(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains(NvidiaMarker, StringComparison.OrdinalIgnoreCase);
}
