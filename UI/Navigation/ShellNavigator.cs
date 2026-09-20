namespace Nexora.UI.Navigation;

/// <summary>
/// The shell's page-visibility router: hides every page, shows the selected
/// one, and reports back which item was selected (or null for an unknown
/// key, in which case everything stays hidden — exactly the pre-extraction
/// shell behavior). Deliberately UI-framework-free: each page is a
/// show/hide callback, so the routing logic is unit-testable without an STA
/// thread while the shell keeps owning the actual controls.
/// </summary>
public sealed class ShellNavigator
{
    private readonly IReadOnlyDictionary<string, Action<bool>> _show;

    public ShellNavigator(IReadOnlyDictionary<string, Action<bool>> show)
    {
        _show = show;
    }

    /// <summary>
    /// Shows <paramref name="page"/> and hides the rest. Returns the matched
    /// item so the caller can honor <see cref="NavigationItem.RefreshOnNavigate"/>,
    /// or null when the key matches nothing.
    /// </summary>
    public NavigationItem? Navigate(string? page)
    {
        foreach (var pair in _show)
        {
            pair.Value(string.Equals(pair.Key, page, StringComparison.Ordinal));
        }

        return NavigationItem.All.FirstOrDefault(item => item.Key == page);
    }
}
