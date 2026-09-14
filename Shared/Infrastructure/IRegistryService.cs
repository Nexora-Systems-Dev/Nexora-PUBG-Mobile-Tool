namespace Nexora.Shared.Infrastructure;

/// <summary>
/// Interface for Windows registry access across current user and local machine hives.
/// </summary>
public interface IRegistryService
{
    int? GetUserDword(string name);

    bool SetUserDword(string name, int value);

    int? GetAppSettingDword(string name);

    void SetAppSettingDword(string name, int value);

    void DeleteAppSetting(string name);

    string? GetLocalString(string name, string branch = "");

    bool SetLocalMachineDword(string subKeyPath, string name, int value);

    int? GetLocalMachineDword(string subKeyPath, string name);

    bool SetCurrentUserString(string subKeyPath, string name, string value);

    string? GetCurrentUserString(string subKeyPath, string name);
}
