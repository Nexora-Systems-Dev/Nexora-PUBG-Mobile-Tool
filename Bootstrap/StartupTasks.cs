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
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>A task that completes when the sweep finishes.</returns>
    public static Task PurgeStaleUpdateStagingAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => StagingDirectoryGC.PurgeStaleStagingDirectories(), cancellationToken);
    }
}
