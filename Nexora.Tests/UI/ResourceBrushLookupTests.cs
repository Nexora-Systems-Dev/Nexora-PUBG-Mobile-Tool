using System.Windows.Media;
using FluentAssertions;
using Nexora.UI.Presentation;
using Xunit;

namespace Nexora.Tests.UI;

/// <summary>
/// Pins the documented resource fallback table without a live element tree:
/// every known key degrades to its signal-family brush, anything else (or
/// null) degrades to neutral gray — never a throw, never null.
/// </summary>
public sealed class ResourceBrushLookupTests
{
    [Theory]
    [InlineData("Success", "#FF32CD32")]
    [InlineData("Danger", "#FFDC143C")]
    [InlineData("Warning", "#FFDAA520")]
    [InlineData("Accent", "#FF00BFFF")]
    [InlineData("TextSecondary", "#FF808080")]
    [InlineData("TextMuted", "#FF808080")]
    public void FallbackFor_KnownKey_ReturnsItsDocumentedBrush(string key, string expectedArgb)
    {
        ResourceBrushLookup.FallbackFor(key).Should().BeBrush(expectedArgb);
    }

    [Theory]
    [InlineData("RenamedKey")]
    [InlineData("")]
    public void FallbackFor_UnknownKey_ReturnsNeutralGrayWithoutThrowing(string key)
    {
        ResourceBrushLookup.FallbackFor(key).Should().BeBrush("#FF808080");
    }

    [Fact]
    public void FallbackFor_NullKey_ReturnsNeutralGrayWithoutThrowing()
    {
        ResourceBrushLookup.FallbackFor(null).Should().BeBrush("#FF808080");
    }

    [Fact]
    public void FallbackFor_AllKeys_ReturnFrozenBrushes()
    {
        foreach (var key in new[] { "Success", "Danger", "Warning", "Accent", "TextSecondary", "TextMuted", "AnythingElse", null })
        {
            ResourceBrushLookup.FallbackFor(key).IsFrozen.Should().BeTrue();
        }
    }
}

/// <summary>
/// Brush equality by rendered color: <see cref="SolidColorBrush"/> is
/// reference-compared, so tests assert the ARGB that actually paints.
/// </summary>
internal static class BrushAssertions
{
    internal static void BeBrush(this FluentAssertions.Primitives.ObjectAssertions assertions, string expectedArgb)
    {
        var brush = assertions.Subject.Should().BeOfType<SolidColorBrush>().Subject;
        brush.Color.ToString().Should().Be(expectedArgb);
    }
}
