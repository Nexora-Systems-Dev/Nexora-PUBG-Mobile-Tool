namespace Nexora.UI.Navigation;

/// <summary>
/// The shell's page-visibility router: hides every page, shows the selected
/// one, and reports back which item was selected (or null for an unknown
/// key, in which case everything stays hidden — exactly the pre-extraction
/// shell behavior). Deliberately UI-framework-free: each page is a
/// show/hide callback, so the routing logic is unit-testable without an STA
/// thread while the shell keeps owning the actual controls. The
/// refresh-on-arrive map lives here too for the same reason — it is a
/// <see cref="Func{TResult}"/> table, not a control table.
/// </summary>
public sealed class ShellNavigator
{
    private readonly IReadOnlyDictionary<string, Action<bool>> _show;
    private readonly IReadOnlyDictionary<string, Func<Task>> _refresh;

    public ShellNavigator(
        IReadOnlyDictionary<string, Action<bool>> show,
        IReadOnlyDictionary<string, Func<Task>> refresh)
    {
        _show = show;
        _refresh = refresh;
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

    /// <summary>
    /// Looks up the refresh callback a flagged page registered. Returns false
    /// for a null key, an unknown page, or a flagged page with no entry — it
    /// never throws, so a missing registration degrades to today's silent
    /// skip instead of breaking navigation.
    /// </summary>
    public bool TryGetRefresh(string? page, out Func<Task>? refresh)
    {
        refresh = null;
        return page is not null && _refresh.TryGetValue(page, out refresh);
    }
}
