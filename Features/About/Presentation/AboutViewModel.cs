using Nexora.Configuration;

namespace Nexora.Features.About.Presentation;

/// <summary>
/// Owns the About page's only dynamic content: the version pill. The page is
/// otherwise static marketing copy, but the version string is code
/// (<see cref="AppConstants.CurrentVersion"/>) rather than XAML text, so it
/// lives here — computed once, testable, and free of any shell logic. The
/// view paints <c>VersionText</c> from <see cref="VersionDisplay"/> in its
/// constructor; there is nothing to refresh and no bus to guard.
/// </summary>
public sealed class AboutViewModel
{
    /// <summary>
    /// The version pill text, verbatim the pre-extraction shell format:
    /// "VERSION " plus the current version without its leading 'v'.
    /// </summary>
    public string VersionDisplay => "VERSION " + AppConstants.CurrentVersion.TrimStart('v');
}
