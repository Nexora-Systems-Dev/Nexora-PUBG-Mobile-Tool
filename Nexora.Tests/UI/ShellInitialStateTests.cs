using System.Linq;
using FluentAssertions;
using Nexora.UI.Layout;
using Nexora.UI.Navigation;
using Xunit;

namespace Nexora.Tests.UI;

/// <summary>
/// Pins the shell's first-paint contract without a window: the initial page
/// shows exactly one page (Graphics), and the XAML-declared startup width
/// selects the standard tier — so a future breakpoint edit that moves first
/// paint off-tier fails here instead of shipping a new settle-jumble.
/// </summary>
public sealed class ShellInitialStateTests
{
    /// <summary>
    /// The XAML-declared startup size (MainWindow.xaml Width/Height).
    /// </summary>
    private const double StartupWidth = 1440;

    [Fact]
    public void InitialPage_ShowsOnlyGraphics()
    {
        var shown = new Dictionary<string, bool>();
        var navigator = new ShellNavigator(
            NavigationItem.All.ToDictionary(
                item => item.Key,
                item => (Action<bool>)(show => shown[item.Key] = show)),
            new Dictionary<string, Func<Task>>());

        var item = navigator.Navigate(NavigationItem.Graphics.Key);

        item.Should().Be(NavigationItem.Graphics);
        shown.Should().BeEquivalentTo(new Dictionary<string, bool>
        {
            ["Graphics"] = true,
            ["Optimizer"] = false,
            ["Tuning"] = false,
            ["Network"] = false,
            ["Shortcuts"] = false,
            ["About"] = false,
        });
    }

    [Fact]
    public void StartupWidth_SelectsTheStandardTier()
    {
        ResponsiveLayoutManager.GetSidebarWidth(StartupWidth)
            .Should().Be(ResponsiveLayoutManager.StandardSidebarWidth);
        ResponsiveLayoutManager.GetPageMargin(StartupWidth)
            .Should().Be(ResponsiveLayoutManager.StandardPageMargin);
    }
}
