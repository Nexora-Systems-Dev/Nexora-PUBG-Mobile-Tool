using Microsoft.Win32;
using Nexora.Configuration;

namespace Nexora.Infrastructure.Registry;

public sealed class RegistryService : IUserRegistry, IMachineRegistry
{
    private const string UserPath = @"SOFTWARE\Tencent\MobileGamePC";
    private const string LocalRoot = @"SOFTWARE\WOW6432Node\Tencent\MobileGamePC";

    /// <summary>
    /// Current application settings location. The vendor key mirrors the
    /// legacy <c>MK Apps</c> shape; the product segment tracks
    /// <see cref="AppConstants.ApplicationName"/>.
    /// </summary>
    private const string AppSettingsPath = $"SOFTWARE\\Nexora\\{AppConstants.ApplicationName}";

    /// <summary>
    /// Pre-rebrand settings location. Reads fall back here permanently so
    /// long-standing installs keep working; nothing ever writes here again.
    /// </summary>
    private const string LegacyAppSettingsPath = @"SOFTWARE\MK Apps\MK PUBG Mobile Tool";

    private readonly GameLoopOptions _gameLoop;

    public RegistryService(GameLoopOptions? gameLoop = null)
    {
        _gameLoop = gameLoop ?? new GameLoopOptions();
    }

    public int? GetUserDword(string name)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(UserPath, writable: false);
        return ReadDword(key?.GetValue(name));
    }

    public bool SetUserDword(string name, int value)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(UserPath, writable: true);
        key?.SetValue(name, value, RegistryValueKind.DWord);
        return key is not null;
    }

    /// <summary>
    /// Reads an application DWORD, trying the current settings key first and
    /// permanently falling back to the pre-rebrand key. When both keys hold
    /// the value, the current key wins.
    /// </summary>
    public int? GetAppSettingDword(string name)
    {
        return TryGetAppSettingDword(AppSettingsPath, name)
            ?? TryGetAppSettingDword(LegacyAppSettingsPath, name);
    }

    private static int? TryGetAppSettingDword(string settingsPath, string name)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(settingsPath, writable: false);
        return ReadDword(key?.GetValue(name));
    }

    /// <summary>
    /// Writes an application DWORD to the current settings key only.
    /// The pre-rebrand key is never written to.
    /// </summary>
    public void SetAppSettingDword(string name, int value)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(AppSettingsPath, writable: true);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }

    /// <summary>
    /// Deletes an application DWORD from both the current and the pre-rebrand
    /// settings keys. This is a deliberate exception to the write-new-only
    /// policy: Reset means "leave no trace", and deleting only the current
    /// key would let a legacy-only value resurface through the read fallback.
    /// </summary>
    public void DeleteAppSetting(string name)
    {
        DeleteAppSettingValue(AppSettingsPath, name);
        DeleteAppSettingValue(LegacyAppSettingsPath, name);
    }

    private static void DeleteAppSettingValue(string settingsPath, string name)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(settingsPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public string? GetLocalString(string name, string? branch = null)
    {
        branch ??= _gameLoop.Registry.BranchAppMarket;
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var key = baseKey.OpenSubKey($"{LocalRoot}\\{branch}", writable: false);
        return key?.GetValue(name)?.ToString();
    }

    public bool SetCurrentUserString(string subKeyPath, string name, string value)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true);
        if (key is null) return false;
        key.SetValue(name, value, RegistryValueKind.String);
        return true;
    }

    public string? GetCurrentUserString(string subKeyPath, string name)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
        return key?.GetValue(name)?.ToString();
    }

    public bool SetLocalMachineDword(string subKeyPath, string name, int value)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var key = baseKey.CreateSubKey(subKeyPath, writable: true);
        if (key is null) return false;
        key.SetValue(name, value, RegistryValueKind.DWord);
        return true;
    }

    public int? GetLocalMachineDword(string subKeyPath, string name)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var key = baseKey.OpenSubKey(subKeyPath, writable: false);
        return ReadDword(key?.GetValue(name));
    }

    /// <summary>
    /// A registry DWORD arrives as <see cref="int"/> or <see cref="long"/>
    /// depending on which tool wrote it, so both shapes are accepted and
    /// narrowed to <see cref="int"/>; a missing value stays null.
    /// </summary>
    private static int? ReadDword(object? value) => value switch
    {
        int intValue => intValue,
        long longValue => (int)longValue,
        _ => null
    };
}
