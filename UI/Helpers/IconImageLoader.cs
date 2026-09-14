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
            return icon;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
