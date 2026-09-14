namespace Nexora.Configuration;

/// <summary>
/// Options configuring directories and folder names for temporary file cleanup.
/// </summary>
public sealed class TempCleanupOptions
{
    public const string SectionName = "TempCleanup";

    public IReadOnlyList<string> TargetDirectories { get; init; } = new[]
    {
        Path.GetTempPath(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch")
    };

    public string ShaderCacheFolderName { get; init; } = "ShaderCache";
}
