using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Nexora.Configuration;
using Xunit;

namespace Nexora.Tests.Configuration;

/// <summary>
/// Drift guard for the release version (U-02). Five surfaces carry the version
/// and a release whose surfaces disagree is broken in a way CI does not catch:
/// the in-app updater compares a GitHub tag against <see cref="AppConstants.CurrentVersion"/>,
/// so a csproj that drifts from the anchor ships a build that reports a version
/// the tag does not match, and an update that never applies.
/// <see cref="AppConstants.CurrentVersion"/> is the anchor; everything else is
/// read from the source it ships from rather than restated here, so a bump that
/// misses a surface fails a test instead of a user.
/// </summary>
public sealed class ReleaseVersionConsistencyTests
{
    /// <summary>The anchor. Prefixed the way a git tag is.</summary>
    private const string AnchorVersion = "v1.3.0";

    /// <summary>The anchor without the tag prefix, as it appears in the csproj.</summary>
    private static string ReleaseVersion => AppConstants.CurrentVersion.TrimStart('v');

    /// <summary>
    /// The anchor with the fourth field the assembly/version table expects.
    /// </summary>
    private static string FourPartReleaseVersion => ReleaseVersion + ".0";

    [Fact]
    public void AppConstants_CurrentVersion_IsTheReleaseAnchor()
    {
        // The anchor itself: every other surface is derived from this constant,
        // so if it moves without a release decision this is where it must show.
        AppConstants.CurrentVersion.Should().Be(AnchorVersion);
    }

    [Fact]
    public void NexoraCsproj_Version_MatchesTheReleaseAnchor()
    {
        var version = ReadCsprojProperty("Version");

        version.Should().Be(ReleaseVersion, "Nexora.csproj <Version> must match the release anchor");
    }

    [Fact]
    public void NexoraCsproj_InformationalVersion_MatchesTheReleaseAnchor()
    {
        // <InformationalVersion> is what the Win32 PRODUCTVERSION resource is
        // built from, i.e. what "Get-Item | Select ProductVersion" reports, so
        // leaving it stale would ship an exe that advertises the old release.
        var informational = ReadCsprojProperty("InformationalVersion");

        informational.Should().Be(ReleaseVersion, "Nexora.csproj <InformationalVersion> must match the release anchor");
    }

    [Fact]
    public void NexoraCsproj_AssemblyAndFileVersions_MatchTheReleaseAnchor()
    {
        // The 4-part fields the CLR and Explorer surface. They carry the
        // trailing .0 the tag does not.
        ReadCsprojProperty("AssemblyVersion").Should().Be(FourPartReleaseVersion);
        ReadCsprojProperty("FileVersion").Should().Be(FourPartReleaseVersion);
    }

    [Fact]
    public void AppManifest_AssemblyIdentity_MatchesTheReleaseAnchor()
    {
        // The manifest identity is namespaced, so it is read through the asm.v1
        // namespace rather than by element name alone.
        var manifestPath = Path.Combine(FindRepoRoot(), "app.manifest");
        File.Exists(manifestPath).Should().BeTrue("the app manifest is expected at app.manifest");

        var document = XDocument.Load(manifestPath);
        XNamespace asm = "urn:schemas-microsoft-com:asm.v1";
        var identity = document.Root!.Element(asm + "assemblyIdentity");
        identity.Should().NotBeNull("the manifest must declare an assemblyIdentity");

        var identityVersion = identity!.Attribute("version")?.Value;
        identityVersion.Should().NotBeNullOrEmpty("the manifest assemblyIdentity must carry a version");

        // Identity minus the trailing .0 is the release, the same way "1.3.0.0"
        // reads as "1.3.0". Parsed rather than string-trimmed so a stray trailing
        // character cannot be mistaken for the fourth field.
        System.Version.TryParse(identityVersion, out var parsed).Should().BeTrue("the manifest version must be a four-part version");
        parsed!.ToString(3).Should().Be(ReleaseVersion, "app.manifest assemblyIdentity version must match the release anchor");
    }

    [Fact]
    public void PackageReleaseScript_DefaultVersion_MatchesTheReleaseAnchor()
    {
        // The default the packaging script ships when the operator omits -Version.
        // Read from the script source the way UpdateChecksumFormatTests reads the
        // publisher line, so renaming the parameter breaks this instead of the build.
        var scriptPath = Path.Combine(FindRepoRoot(), "scripts", "package-release.ps1");
        File.Exists(scriptPath).Should().BeTrue("the packaging script is expected at scripts/package-release.ps1");

        var script = File.ReadAllText(scriptPath);
        var match = Regex.Match(script, @"\[string\]\s*\$Version\s*=\s*""([^""]+)""");
        match.Success.Should().BeTrue("the script must default $Version to a literal string");

        // Compared including the tag prefix: the default is the release number an
        // operator pastes, not a numeric field.
        match.Groups[1].Value.Should().Be(AppConstants.CurrentVersion, "the packaging script default must match the release anchor");
    }

    /// <summary>Reads a version field out of the csproj PropertyGroup.</summary>
    private static string ReadCsprojProperty(string name)
    {
        var projectPath = Path.Combine(FindRepoRoot(), "Nexora.csproj");
        File.Exists(projectPath).Should().BeTrue("the project file is expected at Nexora.csproj");

        // Sdk-style projects carry no default namespace, so the element name is
        // looked up directly.
        var document = XDocument.Load(projectPath);
        var propertyGroup = document.Root!.Element("PropertyGroup");
        propertyGroup.Should().NotBeNull("the project must declare a PropertyGroup");

        var element = propertyGroup!.Element(name);
        element.Should().NotBeNull($"the project must declare <{name}>");

        return element!.Value.Trim();
    }

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
