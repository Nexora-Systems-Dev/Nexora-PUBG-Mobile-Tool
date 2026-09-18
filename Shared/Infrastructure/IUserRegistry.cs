namespace Nexora.Shared.Infrastructure;

/// <summary>
/// Current-user registry access: GameLoop vendor DWORDs, Nexora
/// application settings (with permanent pre-rebrand read fallback),
/// and arbitrary current-user string values (GPU preferences, AppCompat flags).
/// </summary>
public interface IUserRegistry
{
    int? GetUserDword(string name);

    bool SetUserDword(string name, int value);

    int? GetAppSettingDword(string name);

    void SetAppSettingDword(string name, int value);

    void DeleteAppSetting(string name);

    bool SetCurrentUserString(string subKeyPath, string name, string value);

    string? GetCurrentUserString(string subKeyPath, string name);
}
