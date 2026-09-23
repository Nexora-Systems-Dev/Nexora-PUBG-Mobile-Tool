using Nexora.Configuration;
using Nexora.Shared.Kernel;
using Nexora.Infrastructure.Files;

namespace Nexora.Features.Updates.Infrastructure;

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

        var removed = 0;
        foreach (var candidate in EnumerateStagingCandidates(tempDirectory, updates))
        {
            if (TryPurgeStaleDirectory(candidate, threshold)) removed++;
        }

        return removed;
    }

    /// <summary>Lists staging trees only; an unreadable base directory yields no candidates.</summary>
    private static string[] EnumerateStagingCandidates(string? tempDirectory, UpdateOptions updates)
    {
        try
        {
            return Directory.EnumerateDirectories(tempDirectory ?? Path.GetTempPath(), updates.StagingPrefix + "*").ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Deletes one staging tree that has aged past the threshold, leaving locked
    /// or in-use trees untouched. Reports whether the tree is now gone.
    /// </summary>
    private static bool TryPurgeStaleDirectory(string candidate, DateTime threshold)
    {
        try
        {
            if (Directory.GetLastWriteTimeUtc(candidate) > threshold) return false;
            TryDeleteDirectory(candidate);
            return !Directory.Exists(candidate);
        }
        catch
        {
            // Skip directories that cannot be accessed or deleted.
            return false;
        }
    }

    internal static void TryDeleteDirectory(string path) => FileUtilities.TryDeleteDirectory(path);
}
