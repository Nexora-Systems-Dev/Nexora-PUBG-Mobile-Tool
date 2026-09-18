using System.IO;
using System.Windows.Media.Imaging;

namespace Nexora.UI.Helpers;

/// <summary>
/// Safely loads bitmap icon images from disk for UI preview.
/// </summary>
public static class IconImageLoader
{
    /// <summary>
    /// Loads an icon file into a BitmapImage, or returns null if missing or invalid.
    /// </summary>
    public static BitmapImage? TryLoadIcon(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
        {
            return null;
        }

        try
        {
            var icon = new BitmapImage();
            icon.BeginInit();
            icon.UriSource = new Uri(iconPath, UriKind.Absolute);
            icon.CacheOption = BitmapCacheOption.OnLoad;
            icon.EndInit();
            // OnLoad already keeps the decoded frame off the file handle; freezing
            // makes the image immutable so WPF can treat it as a static resource
            // instead of keeping a software-rasterization fallback alive for it
            // (QA §3.4 — shortcut preview icon decode on the startup path).
            if (icon.CanFreeze) icon.Freeze();
            return icon;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
