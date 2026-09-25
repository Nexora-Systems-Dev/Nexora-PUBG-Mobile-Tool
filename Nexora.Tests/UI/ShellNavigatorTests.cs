using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Nexora;
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

    [Fact]
    public void ProductionRefreshMap_CoversEveryFlaggedItem()
    {
        var map = MainWindow.BuildRefreshMap(() => Task.CompletedTask);

        foreach (var item in NavigationItem.All.Where(i => i.RefreshOnNavigate))
        {
            map.Should().ContainKey(item.Key, "a flagged page with no refresh entry silently skips its refresh — the CI guard");
            map[item.Key].Should().NotBeNull("the registered delegate must be wired, not a placeholder");
        }
    }

    [Fact]
    public void ProductionShowMap_CoversEveryPage()
    {
        var seen = new Dictionary<string, bool>();
        var map = MainWindow.BuildShowMap(
            show => seen["Graphics"] = show,
            show => seen["Optimizer"] = show,
            show => seen["Tuning"] = show,
            show => seen["Network"] = show,
            show => seen["Shortcuts"] = show,
            show => seen["About"] = show);

        // U-09's missing half beside ProductionRefreshMap_CoversEveryFlaggedItem:
        // every sidebar page must have a show entry, or selecting it paints nothing.
        map.Keys.Should().BeEquivalentTo(NavigationItem.All.Select(item => item.Key));
        foreach (var item in NavigationItem.All)
        {
            map[item.Key].Should().NotBeNull("the registered callback must be wired, not a placeholder");
            map[item.Key](true);
            seen[item.Key].Should().BeTrue();
            map[item.Key](false);
            seen[item.Key].Should().BeFalse();
        }
    }

    [Fact]
    public void TryGetRefresh_MissingEntry_ReturnsFalse()
    {
        var navigator = new ShellNavigator(
            new Dictionary<string, Action<bool>>(),
            new Dictionary<string, Func<Task>>());

        var found = navigator.TryGetRefresh(NavigationItem.Tuning.Key, out var refresh);

        found.Should().BeFalse();
        refresh.Should().BeNull();
    }

    [Fact]
    public void TryGetRefresh_KnownEntry_ReturnsItsCallback()
    {
        var callback = new Func<Task>(() => Task.CompletedTask);
        var navigator = new ShellNavigator(
            new Dictionary<string, Action<bool>>(),
            new Dictionary<string, Func<Task>> { [NavigationItem.Tuning.Key] = callback });

        var found = navigator.TryGetRefresh(NavigationItem.Tuning.Key, out var refresh);

        found.Should().BeTrue();
        refresh.Should().BeSameAs(callback);
    }

    /// <summary>
    /// The complete refresh map for the default build: every page flagged
    /// <see cref="NavigationItem.RefreshOnNavigate"/> gets a no-op callback, so
    /// the 4 navigation tests above stay green under the new constructor and the
    /// flagged set stays pinned by construction.
    /// </summary>
    private static ShellNavigator Build(Dictionary<string, bool> shown) =>
        new(NavigationItem.All.ToDictionary(
            item => item.Key,
            item => (Action<bool>)(show => shown[item.Key] = show)),
            NavigationItem.All
                .Where(item => item.RefreshOnNavigate)
                .ToDictionary(item => item.Key, _ => new Func<Task>(() => Task.CompletedTask)));
}
