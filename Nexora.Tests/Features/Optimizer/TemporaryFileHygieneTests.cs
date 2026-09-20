using FluentAssertions;
using Nexora.Configuration;
using Nexora.Shared.Kernel;
using Xunit;
using Nexora.Features.Updates.Application;
using Nexora.Features.Updates.Domain;
using Nexora.Features.Updates.Infrastructure;
using Nexora.Infrastructure.Processes;

namespace Nexora.Tests.Features.Optimizer;

/// <summary>
/// Verifies updater staging cleanup across success, failure, and cancellation paths,
/// ensuring stale staging trees are safely purged without leaving residual files.
public sealed class TemporaryFileHygieneTests
{
    [Fact]
    public void TryDeleteDirectory_RemovesPopulatedTree()
    {
        // A staging-like tree with an archive plus nested files.
        var root = CreateUniqueDirectory();
        File.WriteAllText(Path.Combine(root, new UpdateOptions().ArchiveFileName), "archive-bytes");
        var nested = Directory.CreateDirectory(Path.Combine(root, new UpdateOptions().ExtractionFolderName, "nested"));
        File.WriteAllText(Path.Combine(nested.FullName, "app.exe"), "exe-bytes");
        File.WriteAllText(Path.Combine(nested.FullName, "readme.txt"), "readme");

        var act = () => StagingDirectoryGC.TryDeleteDirectory(root);

        // Everything gone, no exception.
        act.Should().NotThrow();
        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void TryDeleteDirectory_IgnoresMissingAndBlankPaths()
    {
        var act = () =>
        {
            StagingDirectoryGC.TryDeleteDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
            StagingDirectoryGC.TryDeleteDirectory(string.Empty);
            StagingDirectoryGC.TryDeleteDirectory("   ");
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void TryDeleteDirectory_RemovesReadOnlyFiles()
    {
        var root = CreateUniqueDirectory();
        var readOnly = Path.Combine(root, "locked-attr.exe");
        File.WriteAllText(readOnly, "exe-bytes");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);

        var act = () => StagingDirectoryGC.TryDeleteDirectory(root);

        act.Should().NotThrow();
        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void TryDeleteDirectory_LeavesLockedFilesWithoutThrowing()
    {
        // One file held open with no sharing (like a running installer).
        var root = CreateUniqueDirectory();
        var lockedPath = Path.Combine(root, "running.exe");
        var freePath = Path.Combine(root, "scratch.txt");
        File.WriteAllText(lockedPath, "exe-bytes");
        File.WriteAllText(freePath, "scratch");
        using var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var act = () => StagingDirectoryGC.TryDeleteDirectory(root);

        // Best-effort - the free file is gone, the locked one survives, nothing throws.
        act.Should().NotThrow();
        File.Exists(freePath).Should().BeFalse();
        File.Exists(lockedPath).Should().BeTrue();
    }

    [Fact]
    public void PurgeStaleStagingDirectories_RemovesOnlyOldMatchingTrees()
    {
        // Isolated temp root with an old match, a fresh match, and an old non-match.
        var tempRoot = CreateUniqueDirectory();
        try
        {
            var oldMatch = Directory.CreateDirectory(Path.Combine(tempRoot, new UpdateOptions().StagingPrefix + "old-" + Guid.NewGuid().ToString("N"))).FullName;
            var freshMatch = Directory.CreateDirectory(Path.Combine(tempRoot, new UpdateOptions().StagingPrefix + "fresh-" + Guid.NewGuid().ToString("N"))).FullName;
            var oldOther = Directory.CreateDirectory(Path.Combine(tempRoot, "OtherApp-" + Guid.NewGuid().ToString("N"))).FullName;
            File.WriteAllText(Path.Combine(oldMatch, new UpdateOptions().ArchiveFileName), "stale-bytes");
            Directory.SetLastWriteTimeUtc(oldMatch, DateTime.UtcNow.AddDays(-2));
            Directory.SetLastWriteTimeUtc(oldOther, DateTime.UtcNow.AddDays(-2));

            var removed = 0;
            var act = () => removed = StagingDirectoryGC.PurgeStaleStagingDirectories(tempRoot, TimeSpan.FromHours(1));

            act.Should().NotThrow();
            removed.Should().Be(1);
            Directory.Exists(oldMatch).Should().BeFalse();
            Directory.Exists(freshMatch).Should().BeTrue();
            Directory.Exists(oldOther).Should().BeTrue();
        }
        finally
        {
            StagingDirectoryGC.TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PurgeStaleStagingDirectories_ReturnsZeroForMissingDirectory()
    {
        var removed = StagingDirectoryGC.PurgeStaleStagingDirectories(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            TimeSpan.FromHours(1));

        removed.Should().Be(0);
    }

    [Fact]
    public async Task DownloadAndLaunchAsync_WithoutAvailableUpdate_CreatesNoStaging()
    {
        // Scoped staging base: parallel tests creating real staging trees elsewhere
        // cannot leak into this test's before/after snapshot.
        var stagingBase = CreateUniqueDirectory();
        try
        {
            var service = new UpdateService(new ProcessRunner(), stagingBaseDirectory: stagingBase);
            var before = ListStagingDirectories(stagingBase);

            var result = await service.DownloadAndLaunchAsync(new UpdateInfo(false, AppConstants.CurrentVersion, string.Empty, string.Empty, string.Empty));

            // Rejected before any staging exists, and none is left behind.
            result.Success.Should().BeFalse();
            ListStagingDirectories(stagingBase).Should().BeEquivalentTo(before);
        }
        finally
        {
            StagingDirectoryGC.TryDeleteDirectory(stagingBase);
        }
    }

    [Fact]
    public async Task DownloadAndLaunchAsync_CancelledAttempt_LeavesNoStaging()
    {
        // An already-cancelled token fails the download before any network I/O.
        // Scoped staging base: parallel tests creating real staging trees elsewhere
        // cannot leak into this test's before/after snapshot.
        var stagingBase = CreateUniqueDirectory();
        try
        {
            var service = new UpdateService(new ProcessRunner(), stagingBaseDirectory: stagingBase);
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            var update = new UpdateInfo(true, "v9.9.9", "Nexora-v9.9.9-win-x64.zip", "https://example.com/Nexora-v9.9.9-win-x64.zip", string.Empty);
            var before = ListStagingDirectories(stagingBase);

            var act = () => service.DownloadAndLaunchAsync(update, cancelled.Token);

            // Surfaces as a failure result (never an escape), with no residue in the staging base.
            var result = await act.Should().NotThrowAsync();
            result.Subject.Success.Should().BeFalse();
            ListStagingDirectories(stagingBase).Should().BeEquivalentTo(before);
        }
        finally
        {
            StagingDirectoryGC.TryDeleteDirectory(stagingBase);
        }
    }

    private static string CreateUniqueDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "NexoraHygieneTest-" + Guid.NewGuid().ToString("N"));
        return Directory.CreateDirectory(path).FullName;
    }

    private static string[] ListStagingDirectories(string stagingBase)
    {
        try
        {
            return Directory.EnumerateDirectories(stagingBase, new UpdateOptions().StagingPrefix + "*").ToArray();
        }
        catch
        {
            return [];
        }
    }
}
