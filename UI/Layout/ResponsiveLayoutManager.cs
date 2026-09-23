using System.Windows;

namespace Nexora.UI.Layout;

/// <summary>
/// Calculates responsive UI layout metrics based on window viewport width.
/// </summary>
public static class ResponsiveLayoutManager
{
    public const double TightBreakpoint = 1200;
    public const double CompactBreakpoint = 1320;

    public const double TightSidebarWidth = 228;
    public const double CompactSidebarWidth = 240;
    public const double StandardSidebarWidth = 260;

    public static readonly Thickness TightPageMargin = new(24, 20, 24, 18);
    public static readonly Thickness CompactPageMargin = new(32, 22, 32, 20);
    public static readonly Thickness StandardPageMargin = new(48, 26, 48, 24);

    /// <summary>
    /// Picks the tight, compact, or standard value for the current width. Both
    /// metrics share one tier selector so the two breakpoints stay in one place —
    /// adding a tier changes the selector, not two parallel ladders.
    /// </summary>
    private static T SelectTier<T>(double windowWidth, (T Tight, T Compact, T Standard) tiers) =>
        windowWidth < TightBreakpoint
            ? tiers.Tight
            : windowWidth < CompactBreakpoint
                ? tiers.Compact
                : tiers.Standard;

    /// <summary>
    /// Computes the sidebar width for the specified window width.
    /// </summary>
    public static double GetSidebarWidth(double windowWidth) =>
        SelectTier(windowWidth, (TightSidebarWidth, CompactSidebarWidth, StandardSidebarWidth));

    /// <summary>
    /// Computes the content page margin for the specified window width.
    /// </summary>
    public static Thickness GetPageMargin(double windowWidth) =>
        SelectTier(windowWidth, (TightPageMargin, CompactPageMargin, StandardPageMargin));
}
