using FluentAssertions;
using Microsoft.Win32;
using Xunit;
using Nexora.Infrastructure.Registry;

namespace Nexora.Tests.Infrastructure;

/// <summary>
/// Pins the settings-key rebrand contract: reads try the current
/// <c>Nexora</c> key first with a permanent fallback to the pre-rebrand
/// <c>MK Apps</c> key, ordinary writes touch the current key only, and
/// Delete — the Reset path — clears both keys so no legacy value can
/// resurface through the fallback.
/// Uses GUID-suffixed value names and removes any keys it creates, so real
/// user data is never disturbed.
/// </summary>
public sealed class RegistryAppSettingsBrandingTests
{
    private const string CurrentSettingsPath = @"SOFTWARE\Nexora\Nexora PUBG Mobile Tool";
    private const string LegacySettingsPath = @"SOFTWARE\MK Apps\MK PUBG Mobile Tool";

    [Fact]
    public void GetAppSettingDword_ReturnsCurrentKeyValue_WhenBothKeysHoldTheValue()
    {
        var registry = new RegistryService();
        var valueName = "BrandingTest_" + Guid.NewGuid().ToString("N");

        try
        {
            WriteDword(CurrentSettingsPath, valueName, 111);
            WriteDword(LegacySettingsPath, valueName, 222);

            registry.GetAppSettingDword(valueName).Should().Be(111);
        }
        finally
        {
            DeleteValue(CurrentSettingsPath, valueName);
            DeleteValue(LegacySettingsPath, valueName);
            RemoveKeyIfEmpty(CurrentSettingsPath);
            RemoveKeyIfEmpty(LegacySettingsPath);
            RemoveKeyIfEmpty(@"SOFTWARE\MK Apps");
            RemoveKeyIfEmpty(@"SOFTWARE\Nexora");
        }
    }

    [Fact]
    public void GetAppSettingDword_FallsBackToLegacyKey_WhenCurrentKeyValueIsAbsent()
    {
        var registry = new RegistryService();
        var valueName = "BrandingTest_" + Guid.NewGuid().ToString("N");

        try
        {
            WriteDword(LegacySettingsPath, valueName, 333);

            registry.GetAppSettingDword(valueName).Should().Be(333);
        }
        finally
        {
            DeleteValue(LegacySettingsPath, valueName);
            RemoveKeyIfEmpty(LegacySettingsPath);
            RemoveKeyIfEmpty(@"SOFTWARE\MK Apps");
        }
    }

    [Fact]
    public void GetAppSettingDword_ReturnsNull_WhenAbsentFromBothKeys()
    {
        var registry = new RegistryService();

        registry.GetAppSettingDword("BrandingTest_" + Guid.NewGuid().ToString("N")).Should().BeNull();
    }

    [Fact]
    public void SetAppSettingDword_WritesOnlyToCurrentKey()
    {
        var registry = new RegistryService();
        var valueName = "BrandingTest_" + Guid.NewGuid().ToString("N");

        try
        {
            registry.SetAppSettingDword(valueName, 444);

            ReadDword(CurrentSettingsPath, valueName).Should().Be(444);
            ReadDword(LegacySettingsPath, valueName).Should().BeNull();
        }
        finally
        {
            DeleteValue(CurrentSettingsPath, valueName);
            RemoveKeyIfEmpty(CurrentSettingsPath);
            RemoveKeyIfEmpty(@"SOFTWARE\Nexora");
        }
    }

    [Fact]
    public void SetAppSettingDword_LeavesLegacyKeyCompletelyUntouched()
    {
        var registry = new RegistryService();
        var sentinelName = "BrandingSentinel_" + Guid.NewGuid().ToString("N");
        var workName = "BrandingTest_" + Guid.NewGuid().ToString("N");

        WriteDword(LegacySettingsPath, sentinelName, 555);
        try
        {
            var valueNamesBefore = GetValueNames(LegacySettingsPath);

            registry.SetAppSettingDword(workName, 666);

            GetValueNames(LegacySettingsPath).Should().BeEquivalentTo(valueNamesBefore);
            ReadDword(LegacySettingsPath, sentinelName).Should().Be(555);
            ReadDword(LegacySettingsPath, workName).Should().BeNull();
        }
        finally
        {
            DeleteValue(LegacySettingsPath, sentinelName);
            DeleteValue(CurrentSettingsPath, workName);
            RemoveKeyIfEmpty(CurrentSettingsPath);
            RemoveKeyIfEmpty(LegacySettingsPath);
            RemoveKeyIfEmpty(@"SOFTWARE\MK Apps");
            RemoveKeyIfEmpty(@"SOFTWARE\Nexora");
        }
    }

    [Fact]
    public void DeleteAppSetting_RemovesValueFromBothKeys_WithNoFallbackResurrection()
    {
        var registry = new RegistryService();
        var valueName = "BrandingTest_" + Guid.NewGuid().ToString("N");

        try
        {
            WriteDword(CurrentSettingsPath, valueName, 777);
            WriteDword(LegacySettingsPath, valueName, 888);

            registry.DeleteAppSetting(valueName);

            ReadDword(CurrentSettingsPath, valueName).Should().BeNull();
            ReadDword(LegacySettingsPath, valueName).Should().BeNull();
            registry.GetAppSettingDword(valueName).Should().BeNull();
        }
        finally
        {
            DeleteValue(CurrentSettingsPath, valueName);
            DeleteValue(LegacySettingsPath, valueName);
            RemoveKeyIfEmpty(CurrentSettingsPath);
            RemoveKeyIfEmpty(LegacySettingsPath);
            RemoveKeyIfEmpty(@"SOFTWARE\MK Apps");
            RemoveKeyIfEmpty(@"SOFTWARE\Nexora");
        }
    }

    private static void WriteDword(string settingsPath, string name, int value)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(settingsPath, writable: true);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static int? ReadDword(string settingsPath, string name)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(settingsPath, writable: false);
        return key?.GetValue(name) switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            _ => null
        };
    }

    private static string[] GetValueNames(string settingsPath)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(settingsPath, writable: false);
        return key?.GetValueNames() ?? [];
    }

    private static void DeleteValue(string settingsPath, string name)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(settingsPath, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
        catch { }
    }

    private static void RemoveKeyIfEmpty(string settingsPath)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(settingsPath, writable: false);
            if (key is null || key.ValueCount != 0 || key.SubKeyCount != 0) return;
        }
        catch
        {
            return;
        }

        try
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(settingsPath, throwOnMissingSubKey: false);
        }
        catch { }
    }
}
