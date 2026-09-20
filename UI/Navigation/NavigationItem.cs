namespace Nexora.UI.Navigation;

/// <summary>
/// A shell page: its navigation key (matching the sidebar button's
/// <c>Tag</c>) plus whether selecting it refreshes its content. The sidebar
/// labels live in XAML; this is the code side of the same contract.
/// </summary>
public sealed record NavigationItem(string Key, bool RefreshOnNavigate = false)
{
    public static readonly NavigationItem Graphics = new("Graphics");

    public static readonly NavigationItem Optimizer = new("Optimizer");

    public static readonly NavigationItem Tuning = new("Tuning", RefreshOnNavigate: true);

    public static readonly NavigationItem Network = new("Network");

    public static readonly NavigationItem Shortcuts = new("Shortcuts");

    public static readonly NavigationItem About = new("About");

    /// <summary>Every page in sidebar order.</summary>
    public static IReadOnlyList<NavigationItem> All { get; } =
    [
        Graphics,
        Optimizer,
        Tuning,
        Network,
        Shortcuts,
        About,
    ];
}
