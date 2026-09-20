using FluentAssertions;
using Nexora.UI.Navigation;
using Xunit;

namespace Nexora.Tests.UI;

/// <summary>
/// The shell's page router is UI-framework-free by design (each page is a
/// show/hide callback), so the visibility contract and the Tuning
/// refresh flag are pinned here without an STA thread.
/// </summary>
public sealed class ShellNavigatorTests
{
    [Fact]
    public void Navigate_KnownPage_ShowsOnlyThatPageAndReturnsItsItem()
    {
        var shown = new Dictionary<string, bool>();
        var navigator = Build(shown);

        var item = navigator.Navigate("Tuning");

        item.Should().Be(NavigationItem.Tuning);
        item!.RefreshOnNavigate.Should().BeTrue();
        shown.Should().BeEquivalentTo(new Dictionary<string, bool>
        {
            ["Graphics"] = false,
            ["Optimizer"] = false,
            ["Tuning"] = true,
            ["Network"] = false,
            ["Shortcuts"] = false,
            ["About"] = false,
        });
    }

    [Fact]
    public void Navigate_NonRefreshingPage_ReturnsItemWithoutTheFlag()
    {
        var shown = new Dictionary<string, bool>();
        var navigator = Build(shown);

        var item = navigator.Navigate("About");

        item.Should().Be(NavigationItem.About);
        item!.RefreshOnNavigate.Should().BeFalse();
        shown["About"].Should().BeTrue();
        shown.Values.Count(v => v).Should().Be(1);
    }

    [Fact]
    public void Navigate_UnknownPage_HidesEverythingAndReturnsNull()
    {
        var shown = new Dictionary<string, bool>();
        var navigator = Build(shown);

        var item = navigator.Navigate("Nope");

        item.Should().BeNull();
        shown.Values.Should().OnlyContain(v => !v);
    }

    [Fact]
    public void Navigate_NullKey_HidesEverythingAndReturnsNull()
    {
        var shown = new Dictionary<string, bool>();
        var navigator = Build(shown);

        var item = navigator.Navigate(null);

        item.Should().BeNull();
        shown.Values.Should().OnlyContain(v => !v);
    }

    private static ShellNavigator Build(Dictionary<string, bool> shown) =>
        new(NavigationItem.All.ToDictionary(
            item => item.Key,
            item => (Action<bool>)(show => shown[item.Key] = show)));
}
