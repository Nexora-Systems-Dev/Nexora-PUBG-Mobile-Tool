using Nexora.Configuration;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

/// <summary>
/// Owns update staging lifecycle: per-attempt staging trees and stale-tree collection.
/// </summary>
public static class StagingDirectoryGC
{
    /// <summary>
    /// Removes staging directories older than <paramref name="maxAge"/>.
    /// Ignores directories that are currently locked or in active use.
    /// </summary>
    internal static int PurgeStaleStagingDirectories(string? tempDirectory = null, TimeSpan? maxAge = null, UpdateOptions? updates = null)
    {
        updates ??= new UpdateOptions();
        var threshold = DateTime.UtcNow - (maxAge ?? updates.StaleStagingMaxAge);
        string[] candidates;
        try
        {
            candidates = Directory.EnumerateDirectories(
                tempDirectory ?? Path.GetTempPath(),
                updates.StagingPrefix + "*").ToArray();
        }
        catch
        {
            return 0;
        }

        var removed = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(candidate) > threshold) continue;
                TryDeleteDirectory(candidate);
                if (!Directory.Exists(candidate)) removed++;
            }
            catch
            {
                // Skip directories that cannot be accessed or deleted.
            }
        }

        return removed;
    }

    internal static void TryDeleteDirectory(string path) => FileUtilities.TryDeleteDirectory(path);
}
