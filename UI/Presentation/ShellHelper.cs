using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;

namespace Nexora.UI.Presentation;

/// <summary>
/// The one home for the three things every page view used to copy from the
/// shell: resolving its ViewModel from the container, asking whether the
/// dispatcher is still alive before touching the tree, and looking up a theme
/// brush with a frozen fallback. Static and stateless on purpose — the
/// XAML-constructed views (and the designer, where no provider exists at
/// all) need these before any container scope is available, which is exactly
/// why this is a pure helper and not a registered service: there is nothing
/// to construct, scope, or dispose, mirroring the absorbed
/// <c>ResourceBrushLookup</c> precedent.
/// </summary>
internal static class ShellHelper
{
    /// <summary>
    /// Resolves a ViewModel from the running container. True with the
    /// resolved instance, false when there is no container (designer,
    /// direct construction) or the type is not registered — the caller
    /// builds its local fallback graph on false. This single preamble
    /// replaces the six byte-identical container checks that used to live
    /// in every page view's <c>BuildViewModel</c>.
    /// </summary>
    internal static bool TryResolveViewModel<T>(out T? viewModel) where T : class =>
        TryResolveViewModel(
            System.Windows.Application.Current is App ? App.Services : null,
            out viewModel);

    /// <summary>
    /// Provider-taking twin of <see cref="TryResolveViewModel{T}(out T)"/>:
    /// the seam that makes resolution unit-testable without an <see cref="App"/>
    /// instance (the suite runs off-STA by design and never boots the shell).
    /// </summary>
    internal static bool TryResolveViewModel<T>(IServiceProvider? services, out T? viewModel) where T : class
    {
        viewModel = services?.GetService<T>();
        return viewModel is not null;
    }

    /// <summary>
    /// Reports whether a dispatcher can still reach the visual tree. Takes
    /// the two shutdown flags (not the dispatcher) so the truth table pins
    /// in CI without an STA thread; call sites pass
    /// <c>Dispatcher.HasShutdownStarted/HasShutdownFinished</c> verbatim.
    /// A dead tree must never be painted or re-enabled (QA F-005/F-006).
    /// </summary>
    internal static bool IsAlive(bool hasShutdownStarted, bool hasShutdownFinished) =>
        !hasShutdownStarted && !hasShutdownFinished;

    /// <summary>
    /// The documented fallback for a key, or neutral gray for an unknown or
    /// null key. Pure data — pinned by tests without a live element tree.
    /// Absorbed verbatim from <c>ResourceBrushLookup</c>: the same frozen
    /// <see cref="Brushes"/> singletons in the same signal family as the
    /// token they stand in for, so a future rename stays readable on the
    /// dark chrome instead of throwing on the UI thread.
    /// </summary>
    internal static Brush FallbackFor(string? key) =>
        key is not null && Fallbacks.TryGetValue(key, out var fallback)
            ? fallback
            : Brushes.Gray;

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
    /// The theme brush for <paramref name="key"/>, or its documented fallback
    /// when the key is missing. Never throws, never returns null.
    /// </summary>
    internal static Brush GetBrush(FrameworkElement host, string? key) =>
        key is null
            ? FallbackFor(null)
            : host.TryFindResource(key) as Brush ?? FallbackFor(key);
}
