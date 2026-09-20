using FluentAssertions;
using Nexora.Features.Graphics.Presentation;
using Xunit;

namespace Nexora.Tests.Features.Graphics;

/// <summary>
/// The summary bar must never present an unread profile as a confident setting.
/// </summary>
public sealed class GraphicsDisplayFormatterTests
{
    [Fact]
    public void FormatSummary_AllMissing_RendersPlaceholders()
    {
        var display = GraphicsDisplayFormatter.FormatSummary(
            version: null, quality: null, frameRate: null, style: null, shadowEnabled: null, adb: null);

        display.Version.Should().Be(GraphicsDisplayFormatter.Placeholder);
        display.Quality.Should().Be(GraphicsDisplayFormatter.Placeholder);
        display.Fps.Should().Be(GraphicsDisplayFormatter.Placeholder);
        display.Style.Should().Be(GraphicsDisplayFormatter.Placeholder);
        display.Shadow.Should().Be(GraphicsDisplayFormatter.Placeholder);
        display.Adb.Should().Be(GraphicsDisplayFormatter.Placeholder);
    }

    [Fact]
    public void FormatSummary_WhitespaceOnly_FallsBackToPlaceholder()
    {
        var display = GraphicsDisplayFormatter.FormatSummary(
            version: "   ", quality: string.Empty, frameRate: "\t", style: " ", shadowEnabled: null, adb: "");

        display.Version.Should().Be(GraphicsDisplayFormatter.Placeholder);
        display.Quality.Should().Be(GraphicsDisplayFormatter.Placeholder);
        display.Fps.Should().Be(GraphicsDisplayFormatter.Placeholder);
        display.Style.Should().Be(GraphicsDisplayFormatter.Placeholder);
        display.Adb.Should().Be(GraphicsDisplayFormatter.Placeholder);
    }

    [Fact]
    public void FormatSummary_KnownValues_AreRenderedVerbatim()
    {
        var display = GraphicsDisplayFormatter.FormatSummary(
            version: "PUBG Mobile KR",
            quality: "HDR",
            frameRate: "Extreme",
            style: "Realistic",
            shadowEnabled: true,
            adb: "Connected");

        display.Version.Should().Be("PUBG Mobile KR");
        display.Quality.Should().Be("HDR");
        display.Fps.Should().Be("Extreme");
        display.Style.Should().Be("Realistic");
        display.Shadow.Should().Be("Enabled");
        display.Adb.Should().Be("Connected");
    }

    [Theory]
    [InlineData(true, "Enabled")]
    [InlineData(false, "Disabled")]
    [InlineData(null, "—")]
    public void FormatShadow_MapsTheTristateToggle(bool? shadowEnabled, string expected)
    {
        GraphicsDisplayFormatter.FormatShadow(shadowEnabled).Should().Be(expected);
    }

    [Theory]
    [InlineData(true, "DISCONNECT")]
    [InlineData(false, "CONNECT")]
    public void FormatConnectLabel_FlipsWithAdbState(bool isAdbConnected, string expected)
    {
        GraphicsDisplayFormatter.FormatConnectLabel(isAdbConnected).Should().Be(expected);
    }
}
