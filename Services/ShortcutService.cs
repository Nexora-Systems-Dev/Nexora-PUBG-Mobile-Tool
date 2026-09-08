using Nexora.Configuration;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

public sealed class ShortcutService
{
    private readonly ProcessRunner _runner;
    private readonly RegistryService _registry;
    private readonly string _assetRoot;

    public ShortcutService(ProcessRunner runner, RegistryService registry, string assetRoot)
    {
        _runner = runner;
        _registry = registry;
        _assetRoot = assetRoot;
    }

    public OperationResult CreateShortcut(string displayName, string packageName)
    {
        var marketPath = _registry.GetLocalString(AppConstants.Registry.ValueInstallPath)
            ?? (_registry.GetLocalString(AppConstants.Registry.ValueInstallPath, AppConstants.Registry.BranchUI) is { } ui
                ? Path.Combine(Directory.GetParent(ui)?.FullName ?? string.Empty, AppConstants.Registry.BranchAppMarket)
                : null)
            ?? AppConstants.Assets.DefaultAppMarketPath;
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, $"{displayName}.lnk");
        var iconSource = Path.Combine(_assetRoot, AppConstants.Assets.IconsDirectoryName, $"{packageName}.ico");
        var iconPath = Path.Combine(marketPath, $"{packageName}.ico");
        var target = Path.Combine(marketPath, AppConstants.Emulator.AppMarketFileName);
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
                         $"$s.Arguments = {ProcessText.Quote($"-startpkg {packageName}  -from DesktopLink")}; " +
                          $"$s.Description = '{AppConstants.ApplicationName}'; " +
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
}
