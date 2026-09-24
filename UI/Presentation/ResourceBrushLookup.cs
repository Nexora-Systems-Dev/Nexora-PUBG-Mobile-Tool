using System.Windows;
using System.Windows.Media;

namespace Nexora.UI.Presentation;

/// <summary>
/// Null-safe theme brush lookup. <see cref="FrameworkElement.FindResource"/>
/// throws on a miss, so every direct call site used to carry a dead
/// <c>?? fallback</c>; this is the one place that degrades instead — a
/// missing key paints its documented fallback, an unknown or null key paints
/// neutral gray. All fallbacks are frozen <see cref="Brushes"/> singletons in
/// the same signal family as the token they stand in for, so a future rename
/// stays readable on the dark chrome instead of throwing on the UI thread.
/// </summary>
internal static class ResourceBrushLookup
{
    private static readonly IReadOnlyDictionary<string, Brush> Fallbacks = new Dictionary<string, Brush>(StringComparer.Ordinal)
    {
        // #10B981 emerald dot -> bright green dot.
        ["Success"] = Brushes.LimeGreen,
        // #EF4444 error red -> near-identical red.
        ["Danger"] = Brushes.Crimson,
        // #FBBF24 amber notice -> amber family, still readable on dark panels.
        ["Warning"] = Brushes.Goldenrod,
        // #00D2FF electric cyan -> near-identical cyan.
        ["Accent"] = Brushes.DeepSkyBlue,
        // #9DA8B2 / #677580 status grays -> neutral mid-gray status line.
        ["TextSecondary"] = Brushes.Gray,
        ["TextMuted"] = Brushes.Gray,
    };

    /// <summary>
    /// The documented fallback for a key, or neutral gray for an unknown or
    /// null key. Pure data — pinned by tests without a live element tree.
    /// </summary>
    internal static Brush FallbackFor(string? key) =>
        key is not null && Fallbacks.TryGetValue(key, out var fallback)
            ? fallback
            : Brushes.Gray;

    /// <summary>
    /// The theme brush for <paramref name="key"/>, or its documented fallback
    /// when the key is missing. Never throws, never returns null.
    /// </summary>
    internal static Brush Get(FrameworkElement host, string? key) =>
        key is null
            ? FallbackFor(null)
            : host.TryFindResource(key) as Brush ?? FallbackFor(key);
}
