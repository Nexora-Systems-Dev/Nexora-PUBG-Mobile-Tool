using Microsoft.Win32;

namespace Nexora.Services;

public sealed class RegistryService
{
    private const string UserPath = @"SOFTWARE\Tencent\MobileGamePC";
    private const string LocalRoot = @"SOFTWARE\WOW6432Node\Tencent\MobileGamePC";
    private const string AppSettingsPath = @"SOFTWARE\MK Apps\MK PUBG Mobile Tool";

    public int? GetUserDword(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(UserPath, writable: false);
        var value = key?.GetValue(name);
        return value switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            _ => null
        };
    }

    public string? GetUserString(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(UserPath, writable: false);
        return key?.GetValue(name)?.ToString();
    }

    public bool SetUserDword(string name, int value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UserPath, writable: true);
            key?.SetValue(name, value, RegistryValueKind.DWord);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public int? GetAppSettingDword(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(AppSettingsPath, writable: false);
        var value = key?.GetValue(name);
        return value switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            _ => null
        };
    }

    public void SetAppSettingDword(string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AppSettingsPath, writable: true);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void DeleteAppSetting(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(AppSettingsPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public string? GetLocalString(string name, string branch = "AppMarket")
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = baseKey.OpenSubKey($"{LocalRoot}\\{branch}", writable: false);
            return key?.GetValue(name)?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
