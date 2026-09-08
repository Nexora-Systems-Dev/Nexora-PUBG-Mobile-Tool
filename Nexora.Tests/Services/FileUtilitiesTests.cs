using FluentAssertions;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

public sealed class FileUtilitiesTests
{
    [Fact]
    public void TryDeleteDirectory_RemovesPopulatedTree()
    {
        var root = CreateUniqueDirectory();
        File.WriteAllText(Path.Combine(root, "file1.txt"), "data1");
        var nested = Directory.CreateDirectory(Path.Combine(root, "nested"));
        File.WriteAllText(Path.Combine(nested.FullName, "file2.txt"), "data2");

        var act = () => FileUtilities.TryDeleteDirectory(root);

        act.Should().NotThrow();
        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void TryDeleteDirectory_IgnoresMissingAndBlankPaths()
    {
        var act = () =>
        {
            FileUtilities.TryDeleteDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
            FileUtilities.TryDeleteDirectory(string.Empty);
            FileUtilities.TryDeleteDirectory("   ");
            FileUtilities.TryDeleteDirectory(null!);
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void TryDeleteDirectory_RemovesReadOnlyFiles()
    {
        var root = CreateUniqueDirectory();
        var readOnly = Path.Combine(root, "readonly.txt");
        File.WriteAllText(readOnly, "readonly-bytes");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);

        var act = () => FileUtilities.TryDeleteDirectory(root);

        act.Should().NotThrow();
        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void TryDeleteDirectory_LeavesLockedFilesWithoutThrowing()
    {
        var root = CreateUniqueDirectory();
        var lockedFile = Path.Combine(root, "locked.bin");
        File.WriteAllText(lockedFile, "locked-bytes");

        using (File.Open(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var act = () => FileUtilities.TryDeleteDirectory(root);
            act.Should().NotThrow();
            File.Exists(lockedFile).Should().BeTrue();
        }

        // Cleanup after lock released
        FileUtilities.TryDeleteDirectory(root);
        Directory.Exists(root).Should().BeFalse();
    }

    private static string CreateUniqueDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "Nexora-FileUtil-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
