using Nexora.Features.Updates.Infrastructure;

namespace Nexora.Bootstrap;

/// <summary>
/// Background housekeeping tasks executed once during application startup.
/// </summary>
public static class StartupTasks
{
    /// <summary>
    /// Purges stale update staging directories left behind by previous runs.
    /// </summary>
    public static Task PurgeStaleUpdateStagingAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => StagingDirectoryGC.PurgeStaleStagingDirectories(), cancellationToken);
}
