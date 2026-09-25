using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;
using Nexora.Features.Updates.Infrastructure;

namespace Nexora.Tests.Features.Updates;

/// <summary>
/// Drift guard for the release-checksum transport (U-01): the line
/// <c>scripts/package-release.ps1</c> prints for the operator to paste into the
/// GitHub release body is the <em>only</em> channel an unsigned release's
/// checksum arrives on, so whatever shape that script emits must be a shape
/// <see cref="UpdateArchiveValidator.TryExtractSha256"/> parses. The template
/// is read from the script source rather than restated here, so a change to
/// either side breaks this test instead of shipping an unverifiable release.
/// </summary>
public sealed class UpdateChecksumFormatTests
{
    /// <summary>SHA-256 of an empty file: a known-good 64-char hex constant.</summary>
    private const string SampleHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [Fact]
    public void ScriptPublisherLine_ProducesAChecksumTheUpdaterCanParse()
    {
        var template = ReadPublisherLineTemplate();

        // A template without this placeholder has no checksum to extract, so a
        // rename of the variable fails here rather than silently emitting a
        // line the updater cannot read.
        template.Should().Contain("$exeChecksum", "the publisher line must carry the executable checksum");

        var publishedLine = PopulateTemplate(template);
        publishedLine.Should().Contain(SampleHash, "populating the template must leave the checksum readable");

        // This is the live path: UpdateChecker feeds the release body to the
        // same extractor, so a shape it returns null for is a release the
        // updater refuses to install.
        UpdateArchiveValidator.TryExtractSha256(publishedLine).Should().Be(SampleHash);
    }

    [Fact]
    public void TryExtractSha256_RejectsTheHistoricalPublisherShape()
    {
        // The shape the script emitted before U-01: the label and the hash were
        // separated by "(<filename>): ", which matches neither extractor — the
        // label class stops at the parenthesis, and the hash table regex needs
        // the hash first. Keeping this shape rejected pins why the template
        // must not revert to it.
        var rejected = $"SHA-256 (Nexora-v1.3.0-win-x64.exe): {SampleHash}";

        UpdateArchiveValidator.TryExtractSha256(rejected).Should().BeNull();
    }

    /// <summary>
    /// Reads the <c>$publisherLine</c> assignment out of the packaging script,
    /// failing loudly if the line is removed or stops being a simple string
    /// literal.
    /// </summary>
    private static string ReadPublisherLineTemplate()
    {
        var scriptPath = Path.Combine(FindRepoRoot(), "scripts", "package-release.ps1");
        File.Exists(scriptPath).Should().BeTrue("the packaging script is expected at scripts/package-release.ps1");

        var script = File.ReadAllText(scriptPath);
        var match = Regex.Match(script, @"\$publisherLine\s*=\s*""([^""]+)""");
        match.Success.Should().BeTrue("the script must define a paste-ready $publisherLine string literal");

        return match.Groups[1].Value;
    }

    /// <summary>
    /// Substitutes the script's own variables with release-shaped samples, the
    /// way PowerShell expands them when the script runs.
    /// </summary>
    private static string PopulateTemplate(string template) => template
        .Replace("$releaseName", "Nexora-v1.3.0-win-x64")
        .Replace("$Version", "v1.3.0")
        .Replace("$Runtime", "win-x64")
        .Replace("$exeChecksum", SampleHash);

    /// <summary>Walks up from the test binaries to the folder holding Nexora.slnx.</summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Nexora.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate Nexora.slnx above the test binaries.");
    }
}
