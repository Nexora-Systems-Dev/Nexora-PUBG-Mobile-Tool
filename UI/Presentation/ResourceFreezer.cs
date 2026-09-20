using System.Windows;

namespace Nexora.UI.Presentation;

/// <summary>
/// Freezes the shared brushes and drop-shadow effects views declare in their
/// resource dictionaries. A frozen freezable is immutable, so WPF can skip
/// change tracking and the extra software-rasterization passes the per-page
/// accent glows would otherwise force on resize/scroll (QA §3.3). Only
/// resource-dictionary entries are touched — inline, data-bound values stay
/// live because <see cref="Freezable.CanFreeze"/> is checked first, and
/// nothing in code-behind mutates these resources.
/// </summary>
public static class ResourceFreezer
{
    /// <summary>
    /// Freezes every freezable resource on each given view. Called once from
    /// the shell after <c>InitializeComponent</c>.
    /// </summary>
    public static void FreezeAll(params FrameworkElement[] views)
    {
        foreach (var view in views)
        {
            foreach (var value in view.Resources.Values.OfType<Freezable>())
            {
                if (value.CanFreeze) value.Freeze();
            }
        }
    }
}
