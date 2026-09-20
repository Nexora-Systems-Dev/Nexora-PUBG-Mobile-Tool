using Nexora.Features.GameLoop.Domain;

namespace Nexora.Features.Shortcuts.Domain;

/// <summary>
/// The shortcut preview card's display state: what the desktop launcher will
/// be called, which package it boots, and where it lands. The empty state is
/// a first-class value rather than a null, so the view paints the "choose a
/// version" card without branching on selection nullability.
/// </summary>
public sealed record ShortcutPreview(
    string DisplayName,
    string PackageName,
    string Destination,
    bool CanCreate)
{
    /// <summary>
    /// Builds the preview for a selected version, or the empty "choose a
    /// version" card when nothing is selected. Verbatim text of the
    /// pre-extraction <c>UpdateShortcutPreview</c> paint.
    /// </summary>
    public static ShortcutPreview FromVersion(PubgVersion? version) =>
        version is null
            ? new ShortcutPreview(
                "Choose a PUBG Mobile version",
                "No package selected",
                "The shortcut will be placed on your desktop.",
                false)
            : new ShortcutPreview(
                version.DisplayName,
                version.PackageName,
                $"Desktop shortcut: {version.DisplayName}.lnk",
                true);
}
