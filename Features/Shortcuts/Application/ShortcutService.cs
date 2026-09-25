using System.Windows.Media.Imaging;
using Nexora.Configuration;
using Nexora.Shared.Kernel;
using Nexora.UI.Helpers;
using Nexora.Infrastructure.GameLoop;
using Nexora.Infrastructure.Processes;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Shortcuts.Application;

public sealed class ShortcutService : IShortcutService
{
    private readonly IProcessRunner _runner;
    private readonly IGameLoopPathResolver _paths;
    private readonly EmulatorOptions _emulator;
    private readonly string _assetRoot;

    public ShortcutService(IProcessRunner runner, IGameLoopPathResolver pathResolver, string assetRoot, EmulatorOptions? emulator = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _paths = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _assetRoot = assetRoot;
        _emulator = emulator ?? new EmulatorOptions();
    }

    /// <summary>
    /// Loads the shortcut preview icon for a PUBG Mobile package, or returns
    /// null when the package name is invalid or no icon asset exists for it.
    /// </summary>
    public BitmapImage? GetIcon(string? packageName)
    {
        if (!AppConstants.Validation.IsValidAndroidPackageName(packageName))
        {
            return null;
        }

        var iconPath = IconAssetPath(packageName);
        return IconImageLoader.TryLoadIcon(iconPath);
    }

    /// <summary>The bundled .ico for a package, whether or not one exists on disk.</summary>
    private string IconAssetPath(string? packageName) =>
        Path.Combine(_assetRoot, _emulator.Assets.IconsDirectoryName, $"{packageName}.ico");

    public OperationResult CreateShortcut(string displayName, string packageName)
    {
        // Defense-in-depth: the picker is catalog-gated upstream, but neither
        // value may reach a file path or the shortcut script unchecked. The
        // package rule is GetIcon's rule in this same class; the display-name
        // rule keeps it a single file name (no traversal, no separators).
        if (!AppConstants.Validation.IsValidAndroidPackageName(packageName))
        {
            return OperationResult.Fail("Invalid PUBG package name.");
        }

        if (!IsValidShortcutDisplayName(displayName))
        {
            return OperationResult.Fail("Invalid shortcut display name.");
        }

        var marketPath = _paths.GetAppMarketPath();
        if (marketPath is null)
        {
            return OperationResult.Fail(
                "GameLoop AppMarket path was not found. The installation path could not be resolved from the registry.");
        }
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, $"{displayName}.lnk");
        var iconSource = IconAssetPath(packageName);
        var iconPath = Path.Combine(marketPath, $"{packageName}.ico");
        var target = Path.Combine(marketPath, _emulator.Emulator.AppMarketFileName);
        if (!File.Exists(target))
        {
            return OperationResult.Fail("GameLoop AppMarket.exe was not found.");
        }

        try
        {
            if (File.Exists(iconSource))
            {
                File.Copy(iconSource, iconPath, overwrite: true);
            }

            var script = "$ws = New-Object -ComObject WScript.Shell; " +
                         $"$s = $ws.CreateShortcut({ProcessText.Quote(shortcutPath)}); " +
                         $"$s.TargetPath = {ProcessText.Quote(target)}; " +
                         $"$s.Arguments = {ProcessText.Quote($"-startpkg {packageName} -from DesktopLink")}; " +
                         $"$s.Description = {ProcessText.Quote(AppConstants.ApplicationName)}; " +
                         $"$s.IconLocation = {ProcessText.Quote(iconPath)}; $s.Save()";
            var result = _runner.RunPowerShell(script);
            return result.Succeeded
                ? OperationResult.Ok("Desktop shortcut created.")
                : OperationResult.Fail("Could not create the desktop shortcut.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Shortcut creation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// A display name is only ever interpolated as one <c>.lnk</c> file name,
    /// so it must be a single file name: non-blank, no parent traversal, no
    /// directory separators, no invalid file-name characters. Catalog names
    /// ("PUBG Mobile Global") pass; anything path-shaped fails.
    /// </summary>
    private static bool IsValidShortcutDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return false;
        if (!string.Equals(displayName, displayName.Trim(), StringComparison.Ordinal)) return false;
        if (displayName.Contains("..", StringComparison.Ordinal)) return false;
        if (displayName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0) return false;
        if (displayName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        if (Path.IsPathRooted(displayName)) return false;
        return true;
    }
}
