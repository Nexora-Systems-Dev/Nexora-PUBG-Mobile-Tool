namespace Nexora.Features.GameLoop;

/// <summary>
/// Single home for the on-device UE4 remote-path template. Every ADB
/// pull/push of the save game and shadow config must build its path from
/// here so the template cannot drift between collaborators.
/// </summary>
public sealed class RemotePaths
{
    private RemotePaths(string packageName)
    {
        PackageName = packageName;
        SavedRoot = $"/sdcard/Android/data/{packageName}/files/UE4Game/ShadowTrackerExtra/ShadowTrackerExtra/Saved";
    }

    public string PackageName { get; }

    /// <summary>
    /// The UE4 Saved directory for the package on the device.
    /// </summary>
    public string SavedRoot { get; }

    public string ActiveSavPath => $"{SavedRoot}/SaveGames/Active.sav";

    public string UserCustomIniPath => $"{SavedRoot}/Config/Android/UserCustom.ini";

    /// <summary>
    /// Builds the remote paths for a PUBG package. The package name is
    /// required: silently building a path from a blank name would address
    /// the wrong directory on the device.
    /// </summary>
    public static RemotePaths For(string? packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            throw new ArgumentException("A PUBG package name is required to build remote paths.", nameof(packageName));
        }

        return new RemotePaths(packageName);
    }
}
