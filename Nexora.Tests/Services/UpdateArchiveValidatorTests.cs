using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Nexora.Services;
using Xunit;

namespace Nexora.Tests.Services;

/// <summary>
/// Exercises the zip-slip guard in <see cref="UpdateArchiveValidator"/> against
/// real hostile archives: traversal and absolute-path entries must be rejected
/// and nothing may be written outside the staging directory.
/// </summary>
public sealed class UpdateArchiveValidatorTests
{
    [Theory]
    [InlineData("../../evil-{0}.exe")]
    [InlineData("..\\..\\evil-{0}.exe")]
    [InlineData("subdir/../../../evil-{0}.exe")]
    public void GetSafeExtractionPath_ReturnsNull_ForRelativeTraversal(string template)
    {
        var extractionRoot = CreateUniqueDirectory();
        try
        {
            var result = UpdateArchiveValidator.GetSafeExtractionPath(
                extractionRoot,
                string.Format(template, Guid.NewGuid().ToString("N")));

            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(extractionRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("C:/evil-{0}.exe")]
    [InlineData("C:\\evil-{0}.exe")]
    [InlineData("/absolute/evil-{0}.exe")]
    public void GetSafeExtractionPath_ReturnsNull_ForAbsolutePath(string template)
    {
        var extractionRoot = CreateUniqueDirectory();
        try
        {
            var result = UpdateArchiveValidator.GetSafeExtractionPath(
                extractionRoot,
                string.Format(template, Guid.NewGuid().ToString("N")));

            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(extractionRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("Nexora-v9.9.9-win-x64.exe")]
    [InlineData("subdir/payload.exe")]
    public void GetSafeExtractionPath_ReturnsRootedPath_ForSafeEntry(string entryName)
    {
        var extractionRoot = CreateUniqueDirectory();
        try
        {
            var result = UpdateArchiveValidator.GetSafeExtractionPath(extractionRoot, entryName);

            result.Should().NotBeNull();
            result.Should().StartWith(Path.GetFullPath(extractionRoot));
        }
        finally
        {
            Directory.Delete(extractionRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("../../evil-{0}.exe")]
    [InlineData("..\\..\\evil-{0}.exe")]
    [InlineData("C:/evil-{0}.exe")]
    [InlineData("/absolute/evil-{0}.exe")]
    public void ExtractEntriesSafely_Throws_AndWritesNothingOutsideStaging(string template)
    {
        // Unique parent per test: parallel tests cannot leak into the snapshot.
        var parent = Path.Combine(Path.GetTempPath(), "NexoraZipSlipTest-" + Guid.NewGuid().ToString("N"));
        var extractionRoot = Path.Combine(parent, "staging");
        Directory.CreateDirectory(extractionRoot);
        try
        {
            var canary = Guid.NewGuid().ToString("N");
            var hostileName = string.Format(template, canary);
            using var zipStream = CreateZip(hostileName, "payload-safe.exe");
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            var before = Directory.EnumerateFiles(parent, "*", SearchOption.AllDirectories).ToArray();

            var act = () => UpdateArchiveValidator.ExtractEntriesSafely(archive, extractionRoot, CancellationToken.None);

            // Existing contract: rejection surfaces as InvalidOperationException.
            act.Should().Throw<InvalidOperationException>().WithMessage("The update archive contains an unsafe entry path.");
            // Fail-closed: the safe entry after the hostile one is never extracted…
            File.Exists(Path.Combine(extractionRoot, "payload-safe.exe")).Should().BeFalse();
            // …and nothing lands outside staging.
            Directory.EnumerateFiles(parent, "*", SearchOption.AllDirectories).Should().BeEquivalentTo(before);
            File.Exists(EscapeTarget(extractionRoot, hostileName)).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private static MemoryStream CreateZip(params string[] entryNames)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in entryNames)
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                entryStream.Write(Encoding.UTF8.GetBytes("MZ hostile-payload"));
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string EscapeTarget(string extractionRoot, string hostileName)
    {
        var converted = hostileName.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(extractionRoot, converted));
    }

    private static string CreateUniqueDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "NexoraZipSlipTest-" + Guid.NewGuid().ToString("N"));
        return Directory.CreateDirectory(path).FullName;
    }
}
