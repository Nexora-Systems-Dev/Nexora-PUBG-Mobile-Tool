namespace Nexora.Shared.Infrastructure;

/// <summary>
/// Local-machine registry access: GameLoop install-path discovery and
/// machine-wide DWORD settings (e.g. IFEO CPU priorities).
/// </summary>
public interface IMachineRegistry
{
    string? GetLocalString(string name, string? branch = null);

    bool SetLocalMachineDword(string subKeyPath, string name, int value);

    int? GetLocalMachineDword(string subKeyPath, string name);
}
