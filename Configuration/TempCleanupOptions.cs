namespace Nexora.Configuration;

/// <summary>
/// Strongly typed options configuring directories and target patterns for temporary file cleanup.
/// </summary>
public sealed class TempCleanupOptions
{
    public const string SectionName = "TempCleanup";

    /// <summary>
    /// Target directories whose contents will be swept during cleanup.
    /// Defaults to user %TEMP%, %WINDIR%\Temp, and %WINDIR%\Prefetch.
    /// </summary>
    public IReadOnlyList<string> TargetDirectories { get; init; } = new[]
    {
        Path.GetTempPath(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch")
    };

    /// <summary>
    /// Name of the emulator shader cache directory located under the GameLoop install root.
    /// </summary>
    public string ShaderCacheFolderName { get; init; } = "ShaderCache";
}
