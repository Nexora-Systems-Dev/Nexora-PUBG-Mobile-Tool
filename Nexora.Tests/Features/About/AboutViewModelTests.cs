using FluentAssertions;
using Nexora.Configuration;
using Nexora.Features.About.Presentation;
using Xunit;

namespace Nexora.Tests.Features.About;

/// <summary>
/// The About page is static copy plus the version pill, so the ViewModel's
/// whole contract is the pill text: the pre-extraction shell format, kept in
/// sync with the code constant it renders rather than hardcoded to a stale
/// XAML placeholder.
/// </summary>
public sealed class AboutViewModelTests
{
    [Fact]
    public void VersionDisplay_MatchesTheShellFormatForTheCurrentVersion()
    {
        new AboutViewModel().VersionDisplay
            .Should().Be("VERSION " + AppConstants.CurrentVersion.TrimStart('v'));
    }

    [Fact]
    public void VersionDisplay_HasNoLeadingVInTheNumber()
    {
        var display = new AboutViewModel().VersionDisplay;

        display.Should().StartWith("VERSION ");
        display.Should().NotContain("VERSION v");
        display.Should().NotBeNullOrWhiteSpace();
    }
}
