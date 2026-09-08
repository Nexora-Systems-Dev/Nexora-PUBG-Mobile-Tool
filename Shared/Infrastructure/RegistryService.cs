using Microsoft.Win32;
using Nexora.Configuration;

namespace Nexora.Shared.Infrastructure;

public sealed class RegistryService : IRegistryService
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

    public string? GetLocalString(string name, string branch = AppConstants.Registry.BranchAppMarket)
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

    public bool SetCurrentUserString(string subKeyPath, string name, string value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true);
            if (key is null) return false;
            key.SetValue(name, value, RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string? GetCurrentUserString(string subKeyPath, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
            return key?.GetValue(name)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public bool SetLocalMachineDword(string subKeyPath, string name, int value)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using var key = baseKey.CreateSubKey(subKeyPath, writable: true);
            if (key is null) return false;
            key.SetValue(name, value, RegistryValueKind.DWord);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public int? GetLocalMachineDword(string subKeyPath, string name)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKeyPath, writable: false);
            var value = key?.GetValue(name);
            return value switch
            {
                int intValue => intValue,
                long longValue => (int)longValue,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
}
