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
    /// Expected publisher identifier in the Authenticode certificate subject.
    /// </summary>
    public string ExpectedPublisher { get; init; } = "Nexora";

    /// <summary>
    /// Maximum age before temporary update staging directories are swept as orphans.
    /// </summary>
    public TimeSpan StaleStagingMaxAge { get; init; } = TimeSpan.FromHours(24);
}
