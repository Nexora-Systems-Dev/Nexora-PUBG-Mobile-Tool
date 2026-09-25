using System.Windows.Media;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexora;
using Nexora.Features.About.Presentation;
using Nexora.Features.Graphics.Presentation;
using Nexora.Features.Network.Presentation;
using Nexora.Features.Optimizer.Presentation;
using Nexora.Features.Shortcuts.Presentation;
using Nexora.Features.Tuning.Presentation;
using Nexora.UI.Presentation;
using Xunit;

namespace Nexora.Tests.UI;

/// <summary>
/// Pins the shell helper that replaced the six per-view container preambles,
/// the scattered dispatcher guards, and the absorbed brush fallback table:
/// every page ViewModel resolves from the composed container, the liveness
/// truth table holds without an STA thread, and every known key still
/// degrades to its signal-family brush — never a throw, never null.
/// </summary>
public sealed class ShellHelperTests
{
    private static ServiceProvider BuildContainer()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void TryResolveViewModel_GraphicsViewModel_ResolvesFromTheContainer()
    {
        using var provider = BuildContainer();

        var resolved = ShellHelper.TryResolveViewModel(provider, out GraphicsViewModel? viewModel);

        resolved.Should().BeTrue();
        viewModel.Should().NotBeNull();
    }

    [Fact]
    public void TryResolveViewModel_TuningViewModel_ResolvesFromTheContainer()
    {
        using var provider = BuildContainer();

        var resolved = ShellHelper.TryResolveViewModel(provider, out TuningViewModel? viewModel);

        resolved.Should().BeTrue();
        viewModel.Should().NotBeNull();
    }

    [Fact]
    public void TryResolveViewModel_NetworkViewModel_ResolvesFromTheContainer()
    {
        using var provider = BuildContainer();

        var resolved = ShellHelper.TryResolveViewModel(provider, out NetworkViewModel? viewModel);

        resolved.Should().BeTrue();
        viewModel.Should().NotBeNull();
    }

    [Fact]
    public void TryResolveViewModel_OptimizerViewModel_ResolvesFromTheContainer()
    {
        using var provider = BuildContainer();

        var resolved = ShellHelper.TryResolveViewModel(provider, out OptimizerViewModel? viewModel);

        resolved.Should().BeTrue();
        viewModel.Should().NotBeNull();
    }

    [Fact]
    public void TryResolveViewModel_ShortcutsViewModel_ResolvesFromTheContainer()
    {
        using var provider = BuildContainer();

        var resolved = ShellHelper.TryResolveViewModel(provider, out ShortcutsViewModel? viewModel);

        resolved.Should().BeTrue();
        viewModel.Should().NotBeNull();
    }

    [Fact]
    public void TryResolveViewModel_AboutViewModel_ResolvesFromTheContainer()
    {
        using var provider = BuildContainer();

        var resolved = ShellHelper.TryResolveViewModel(provider, out AboutViewModel? viewModel);

        resolved.Should().BeTrue();
        viewModel.Should().NotBeNull();
    }

    [Fact]
    public void TryResolveViewModel_NullProvider_ReturnsFalseWithoutThrowing()
    {
        var resolved = ShellHelper.TryResolveViewModel(null, out GraphicsViewModel? viewModel);

        resolved.Should().BeFalse("the designer path has no container and must fall back");
        viewModel.Should().BeNull();
    }

    [Fact]
    public void TryResolveViewModel_WithoutAppInstance_ReturnsFalseWithoutThrowing()
    {
        // The suite never boots the shell, so Application.Current is not an
        // App: the parameterless overload must degrade to the fallback path.
        var resolved = ShellHelper.TryResolveViewModel(out GraphicsViewModel? viewModel);

        resolved.Should().BeFalse();
        viewModel.Should().BeNull();
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void IsAlive_MatchesTheShutdownFlagTruthTable(bool started, bool finished, bool expected) =>
        ShellHelper.IsAlive(started, finished).Should().Be(expected);

    [Theory]
    [InlineData("Success", "#FF32CD32")]
    [InlineData("Danger", "#FFDC143C")]
    [InlineData("Warning", "#FFDAA520")]
    [InlineData("Accent", "#FF00BFFF")]
    [InlineData("TextSecondary", "#FF808080")]
    [InlineData("TextMuted", "#FF808080")]
    public void FallbackFor_KnownKey_ReturnsItsDocumentedBrush(string key, string expectedArgb)
    {
        ShellHelper.FallbackFor(key).Should().BeBrush(expectedArgb);
    }

    [Theory]
    [InlineData("RenamedKey")]
    [InlineData("")]
    public void FallbackFor_UnknownKey_ReturnsNeutralGrayWithoutThrowing(string key)
    {
        ShellHelper.FallbackFor(key).Should().BeBrush("#FF808080");
    }

    [Fact]
    public void FallbackFor_NullKey_ReturnsNeutralGrayWithoutThrowing()
    {
        ShellHelper.FallbackFor(null).Should().BeBrush("#FF808080");
    }

    [Fact]
    public void FallbackFor_AllKeys_ReturnFrozenBrushes()
    {
        foreach (var key in new[] { "Success", "Danger", "Warning", "Accent", "TextSecondary", "TextMuted", "AnythingElse", null })
        {
            ShellHelper.FallbackFor(key).IsFrozen.Should().BeTrue();
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
