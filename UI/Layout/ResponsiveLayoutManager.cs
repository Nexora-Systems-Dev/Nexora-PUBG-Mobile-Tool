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
    /// Computes the sidebar width for the specified window width.
    /// </summary>
    public static double GetSidebarWidth(double windowWidth)
    {
        if (windowWidth < TightBreakpoint)
        {
            return TightSidebarWidth;
        }

        if (windowWidth < CompactBreakpoint)
        {
            return CompactSidebarWidth;
        }

        return StandardSidebarWidth;
    }

    /// <summary>
    /// Computes the content page margin for the specified window width.
    /// </summary>
    public static Thickness GetPageMargin(double windowWidth)
    {
        if (windowWidth < TightBreakpoint)
        {
            return TightPageMargin;
        }

        if (windowWidth < CompactBreakpoint)
        {
            return CompactPageMargin;
        }

        return StandardPageMargin;
    }
}
