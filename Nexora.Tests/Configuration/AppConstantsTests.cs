using FluentAssertions;
using Nexora.Configuration;
using Xunit;

namespace Nexora.Tests.Configuration;

/// <summary>
/// Verifies the remaining application identity constants and validation helpers.
/// Operational values (endpoints, timeouts, process definitions) are covered
/// by <see cref="OptionsTests"/> against their options classes.
/// </summary>
public sealed class AppConstantsTests
{
    [Fact]
    public void CurrentVersion_HasTagFormat()
    {
        var version = AppConstants.CurrentVersion;

        // Matches the version tag scheme used by GitHub releases.
        version.Should().MatchRegex(@"^v\d+\.\d+\.\d+$");
    }

    [Fact]
    public void ApplicationName_IsSingleIdentity()
    {
        AppConstants.ApplicationName.Should().Be("Nexora PUBG Mobile Tool");
    }

    [Fact]
    public void TaskkillFileName_IsExecutableName()
    {
        AppConstants.Tools.TaskkillFileName.Should().Be("taskkill.exe");
    }

    [Fact]
    public void IsValidAndroidPackageName_AcceptsDottedPackages()
    {
        AppConstants.Validation.IsValidAndroidPackageName("com.pubg.krmobile").Should().BeTrue();
    }

    [Fact]
    public void IsValidAndroidPackageName_RejectsBlankAndMalformed()
    {
        AppConstants.Validation.IsValidAndroidPackageName(null).Should().BeFalse();
        AppConstants.Validation.IsValidAndroidPackageName("").Should().BeFalse();
        AppConstants.Validation.IsValidAndroidPackageName("NoDots").Should().BeFalse();
    }
}
