using FluentAssertions;
using Nexora.UI.Layout;
using Xunit;

namespace Nexora.Tests.UI;

public sealed class ResponsiveLayoutManagerTests
{
    [Theory]
    [InlineData(1000, ResponsiveLayoutManager.TightSidebarWidth)]
    [InlineData(1199.9, ResponsiveLayoutManager.TightSidebarWidth)]
    [InlineData(1200, ResponsiveLayoutManager.CompactSidebarWidth)]
    [InlineData(1319.9, ResponsiveLayoutManager.CompactSidebarWidth)]
    [InlineData(1320, ResponsiveLayoutManager.StandardSidebarWidth)]
    [InlineData(1920, ResponsiveLayoutManager.StandardSidebarWidth)]
    public void GetSidebarWidth_ReturnsExpectedWidth_ForBreakpoints(double windowWidth, double expectedWidth)
    {
        var width = ResponsiveLayoutManager.GetSidebarWidth(windowWidth);
        width.Should().Be(expectedWidth);
    }

    [Theory]
    [InlineData(1100, 24, 20, 24, 18)]
    [InlineData(1250, 32, 22, 32, 20)]
    [InlineData(1400, 48, 26, 48, 24)]
    public void GetPageMargin_ReturnsExpectedMargin_ForBreakpoints(
        double windowWidth,
        double left,
        double top,
        double right,
        double bottom)
    {
        var margin = ResponsiveLayoutManager.GetPageMargin(windowWidth);
        margin.Left.Should().Be(left);
        margin.Top.Should().Be(top);
        margin.Right.Should().Be(right);
        margin.Bottom.Should().Be(bottom);
    }
}
