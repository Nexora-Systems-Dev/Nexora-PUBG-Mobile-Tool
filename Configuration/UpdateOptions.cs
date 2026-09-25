namespace Nexora.Configuration;

/// <summary>
/// Options for the self-update feed, staging layout, and executable verification.
/// </summary>
public sealed class UpdateOptions
{
    public const string SectionName = "Update";

    public string Repository { get; init; } = "Nexora-Systems-Dev/Nexora-PUBG-Mobile-Tool";

    public string ReleasesUrl => "https://api.github.com/repos/" + Repository + "/releases/latest";

    public string UserAgent { get; init; } = "Nexora-PUBG-Mobile-Tool";

    public string Runtime { get; init; } = "win-x64";

    public string StagingPrefix { get; init; } = "NexoraUpdate-";

    public string ArchiveFileName { get; init; } = "update.zip";

    public string ExtractionFolderName { get; init; } = "extracted";

    public string PortableExecutableName { get; init; } = AppConstants.ApplicationName + ".exe";

    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Budget for the update archive body alone (Item-9 split): redirect hops
    /// share <see cref="HttpTimeout"/> because they are headers-only and fast,
    /// but the ~150 MB body must survive slow networks — 10 minutes holds a
    /// ~256 KB/s floor. Measured from the code (no live run needed): the
    /// whole fetch previously shared one 12 s deadline, so any body slower
    /// than ~12 MB/s timed out.
    /// </summary>
    public TimeSpan DownloadBodyTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Expected publisher identifier in the Authenticode certificate subject.
    /// </summary>
    public string ExpectedPublisher { get; init; } = "Nexora";

    /// <summary>
    /// Maximum age before temporary update staging directories are swept as orphans.
    /// </summary>
    public TimeSpan StaleStagingMaxAge { get; init; } = TimeSpan.FromHours(24);
}
