using System.Diagnostics;
using Nexora.Configuration;
using Nexora.Features.GameLoop;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

/// <summary>
/// Orchestrates GameLoop emulator connectivity, PUBG Mobile version detection,
/// graphics configuration updates via Unreal Engine 4 .sav binary patching, and shadow tuning.
/// </summary>
public sealed class GameLoopService : IGameLoopService
{
    public static readonly IReadOnlyDictionary<string, string> PubgVersions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["com.tencent.ig"] = "PUBG Mobile Global",
            ["com.vng.pubgmobile"] = "PUBG Mobile VN",
            ["com.rekoo.pubgm"] = "PUBG Mobile TW",
            ["com.pubg.krmobile"] = "PUBG Mobile KR",
            ["com.pubg.imobile"] = "Battlegrounds Mobile India"
        };

    private static readonly IReadOnlyDictionary<string, byte> QualityValues =
        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["Smooth"] = 0x01,
            ["Balanced"] = 0x02,
            ["HD"] = 0x03,
            ["HDR"] = 0x04,
            ["Ultra HDR"] = 0x05,
            ["Extreme HDR"] = 0x06
        };

    private static readonly IReadOnlyDictionary<string, byte> FrameRateValues =
        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["Low"] = 0x02,
            ["Medium"] = 0x03,
            ["High"] = 0x04,
            ["Ultra"] = 0x05,
            ["Extreme"] = 0x06,
            ["Extreme+"] = 0x07,
            ["Ultra Extreme"] = 0x08
        };

    private static readonly IReadOnlyDictionary<string, byte> StyleValues =
        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["Classic"] = 0x01,
            ["Colorful"] = 0x02,
            ["Realistic"] = 0x03,
            ["Soft"] = 0x04,
            ["Movie"] = 0x06
        };

    private readonly IRegistryService _registry;
    private readonly IAdbClient _adb;
    private readonly GameLoopWorkingStorage _storage;
    private byte[]? _activeSavContent;

    public GameLoopService(IRegistryService registry, IAdbClient adb)
        : this(registry, adb, new GameLoopWorkingStorage())
    {
    }

    public GameLoopService(IRegistryService registry, IAdbClient adb, GameLoopWorkingStorage storage)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    /// <summary>
    /// Currently loaded and connected PUBG Mobile package name, or null when disconnected.
    /// </summary>
    public string? CurrentPackage { get; private set; }

    /// <summary>
    /// Returns true if an active .sav buffer is loaded and a target PUBG package is active.
    /// </summary>
    public bool IsConnected => _activeSavContent is not null && !string.IsNullOrWhiteSpace(CurrentPackage);

    /// <summary>
    /// Resets the connection state and clears in-memory save data.
    /// </summary>
    public void Disconnect()
    {
        _activeSavContent = null;
        CurrentPackage = null;
    }

    /// <summary>
    /// Checks prerequisites, waits for ADB boot, and enumerates installed PUBG Mobile packages.
    /// </summary>
    public async Task<ConnectionResult> ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var adbStatus = _registry.GetUserDword(AppConstants.Registry.ValueAdbDisable);
        if (adbStatus is null)
        {
            return new ConnectionResult(false, "Could not read GameLoop ADB status.", Array.Empty<PubgVersion>());
        }

        if (adbStatus == 1)
        {
            _registry.SetUserDword(AppConstants.Registry.ValueAdbDisable, 0);
            return new ConnectionResult(false, "ADB was enabled. Restart GameLoop and try again.", Array.Empty<PubgVersion>());
        }

        if (!IsGameLoopRunning())
        {
            return new ConnectionResult(false, "GameLoop is not running.", Array.Empty<PubgVersion>());
        }

        if (!await _adb.WaitForBootAsync(cancellationToken))
        {
            AdbClient.KillAdb();
            return new ConnectionResult(false, "GameLoop ADB did not finish booting.", Array.Empty<PubgVersion>());
        }

        cancellationToken.ThrowIfCancellationRequested();
        _storage.EnsureDirectoryCreated();
        if (!await _adb.PullAsync("/default.prop", _storage.ConnectionProbePath, cancellationToken))
        {
            AdbClient.KillAdb();
            return new ConnectionResult(false, "Could not connect to GameLoop ADB.", Array.Empty<PubgVersion>());
        }

        var installedPackages = _adb.FindInstalledPackages(PubgVersions.Keys, cancellationToken);
        var installedVersions = installedPackages
            .Where(PubgVersions.ContainsKey)
            .Select(package => new PubgVersion(package, PubgVersions[package]))
            .ToList();

        if (installedVersions.Count == 0)
        {
            return new ConnectionResult(false, "No supported PUBG Mobile version was found.", installedVersions);
        }

        if (installedVersions.Count == 1)
        {
            var loadResult = await LoadVersionAsync(installedVersions[0].PackageName, cancellationToken);
            return loadResult.Success
                ? new ConnectionResult(true, $"Using {installedVersions[0].DisplayName}.", installedVersions)
                : new ConnectionResult(false, loadResult.Message, installedVersions);
        }

        return new ConnectionResult(true, "Select the PUBG Mobile version to use.", installedVersions);
    }

    /// <summary>
    /// Pulls and loads the Active.sav and UserCustom.ini configuration files for the specified package.
    /// </summary>
    public async Task<OperationResult> LoadVersionAsync(string packageName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!AppConstants.Validation.IsValidAndroidPackageName(packageName))
        {
            return OperationResult.Fail("Invalid PUBG package name.");
        }

        if (!PubgVersions.ContainsKey(packageName))
        {
            return OperationResult.Fail("Unsupported PUBG Mobile version.");
        }

        _storage.PrepareWorkingFiles();
        var remoteSavPath = $"/sdcard/Android/data/{packageName}/files/UE4Game/ShadowTrackerExtra/ShadowTrackerExtra/Saved/SaveGames/Active.sav";

        if (!await _adb.PullAsync(remoteSavPath, _storage.PreviousSavPath, cancellationToken))
        {
            return OperationResult.Fail("Could not read the PUBG graphics file from GameLoop.");
        }

        try
        {
            _activeSavContent = File.ReadAllBytes(_storage.PreviousSavPath);
            CurrentPackage = packageName;

            var remoteShadowPath = $"/sdcard/Android/data/{packageName}/files/UE4Game/ShadowTrackerExtra/ShadowTrackerExtra/Saved/Config/Android/UserCustom.ini";
            await _adb.PullAsync(remoteShadowPath, _storage.ShadowSettingsPath, cancellationToken);

            return OperationResult.Ok($"Connected to {PubgVersions[packageName]}.");
        }
        catch (IOException ex)
        {
            return OperationResult.Fail($"Could not read the graphics file: {ex.Message}");
        }
    }

    public string GetGraphicsQuality() => ReadProperty("BattleRenderQuality") switch
    {
        0x01 => "Smooth",
        0x02 => "Balanced",
        0x03 => "HD",
        0x04 => "HDR",
        0x05 => "Ultra HDR",
        0x06 => "Extreme HDR",
        _ => "Smooth"
    };

    public string GetFrameRate() => ReadProperty("BattleFPS") switch
    {
        0x02 => "Low",
        0x03 => "Medium",
        0x04 => "High",
        0x05 => "Ultra",
        0x06 => "Extreme",
        0x07 => "Extreme+",
        0x08 => "Ultra Extreme",
        _ => "Low"
    };

    public string GetGraphicsStyle() => ReadProperty("BattleRenderStyle") switch
    {
        0x01 => "Classic",
        0x02 => "Colorful",
        0x03 => "Realistic",
        0x04 => "Soft",
        0x06 => "Movie",
        _ => "Classic"
    };

    /// <summary>
    /// Retrieves the current shadow status ("Enable" or "Disable") from UserCustom.ini.
    /// </summary>
    public async Task<string> GetShadowAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CurrentPackage))
        {
            return "Disable";
        }

        var localShadowPath = _storage.ShadowSettingsPath;
        if (!File.Exists(localShadowPath))
        {
            var remoteShadowPath = $"/sdcard/Android/data/{CurrentPackage}/files/UE4Game/ShadowTrackerExtra/ShadowTrackerExtra/Saved/Config/Android/UserCustom.ini";
            if (!await _adb.PullAsync(remoteShadowPath, localShadowPath, cancellationToken))
            {
                return "Disable";
            }
        }

        foreach (var line in File.ReadLines(localShadowPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("+CVars=0B572A11181D160E280C1815100D0044", StringComparison.Ordinal))
            {
                continue;
            }

            return trimmed.EndsWith("48", StringComparison.Ordinal) ? "Enable" : "Disable";
        }

        return "Disable";
    }

    /// <summary>
    /// Applies the selected graphics quality, framerate, style, and shadow settings to the active PUBG Mobile installation.
    /// </summary>
    public async Task<OperationResult> ApplyGraphicsAsync(GraphicsSelection selection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsConnected || _activeSavContent is null || string.IsNullOrWhiteSpace(CurrentPackage))
        {
            return OperationResult.Fail("Connect to GameLoop first.");
        }

        if (!TryResolveGraphicsValues(selection, out var qualityByte, out var fpsByte, out var styleByte))
        {
            return OperationResult.Fail("One or more graphics settings are invalid.");
        }

        var savUpdateResult = UpdateGraphicsSavProperties(qualityByte, fpsByte, styleByte);
        if (!savUpdateResult.Success)
        {
            return savUpdateResult;
        }

        var shadowResult = UnrealCVarCodec.UpdateShadowFile(_storage.ShadowSettingsPath, selection.EnableShadow);
        if (!shadowResult.Success)
        {
            return shadowResult;
        }

        _storage.PrepareWorkingFiles();
        File.WriteAllBytes(_storage.PendingSavPath, _activeSavContent);

        var deployResult = await DeployConfigurationFilesAsync(CurrentPackage, cancellationToken);
        if (!deployResult.Success)
        {
            return deployResult;
        }

        if (selection.EnableKoreanFullHd && CurrentPackage.Equals("com.pubg.krmobile", StringComparison.OrdinalIgnoreCase))
        {
            return await ApplyKoreanFullHdAsync(cancellationToken);
        }

        RelaunchPubgActivity(CurrentPackage);
        return OperationResult.Ok("Graphics settings applied successfully.");
    }

    private static bool TryResolveGraphicsValues(
        GraphicsSelection selection,
        out byte qualityByte,
        out byte fpsByte,
        out byte styleByte)
    {
        fpsByte = 0;
        styleByte = 0;

        return QualityValues.TryGetValue(selection.Quality, out qualityByte) &&
               FrameRateValues.TryGetValue(selection.FrameRate, out fpsByte) &&
               StyleValues.TryGetValue(selection.Style, out styleByte);
    }

    private OperationResult UpdateGraphicsSavProperties(byte qualityByte, byte fpsByte, byte styleByte)
    {
        // Some PUBG/GameLoop builds omit the lobby/menu fields. The battle
        // field is the important one; optional fields must not block an update.
        var qualityUpdated = false;
        foreach (var property in new[] { "ArtQuality", "LobbyRenderQuality", "BattleRenderQuality" })
        {
            if (ChangeProperty(property, qualityByte))
            {
                qualityUpdated = true;
            }
        }

        if (!qualityUpdated)
        {
            return OperationResult.Fail("Could not update the graphics quality in the PUBG profile.");
        }

        var fpsUpdated = false;
        foreach (var property in new[] { "FPSLevel", "BattleFPS", "LobbyFPS" })
        {
            if (ChangeProperty(property, fpsByte))
            {
                fpsUpdated = true;
            }
        }

        if (!fpsUpdated)
        {
            return OperationResult.Fail("Could not update the frame rate in the PUBG profile.");
        }

        if (!ChangeProperty("BattleRenderStyle", styleByte))
        {
            return OperationResult.Fail("Could not update the graphics style.");
        }

        return OperationResult.Ok("Graphics settings updated in save buffer.");
    }

    private async Task<OperationResult> DeployConfigurationFilesAsync(string packageName, CancellationToken cancellationToken)
    {
        var remoteDataRoot = $"/sdcard/Android/data/{packageName}/files/UE4Game/ShadowTrackerExtra/ShadowTrackerExtra/Saved";

        cancellationToken.ThrowIfCancellationRequested();
        _adb.Shell($"am force-stop {packageName}");
        await Task.Delay(AppConstants.Timeouts.ForceStopSettleDelayMilliseconds, cancellationToken);

        if (!await _adb.PushAsync(_storage.PendingSavPath, $"{remoteDataRoot}/SaveGames/Active.sav", cancellationToken))
        {
            return OperationResult.Fail("Could not apply the graphics file to GameLoop.");
        }

        var localShadowPath = _storage.ShadowSettingsPath;
        if (File.Exists(localShadowPath) && !await _adb.PushAsync(localShadowPath, $"{remoteDataRoot}/Config/Android/UserCustom.ini", cancellationToken))
        {
            return OperationResult.Fail("Could not apply the shadow setting to GameLoop.");
        }

        return OperationResult.Ok("Configuration files deployed.");
    }

    private void RelaunchPubgActivity(string packageName)
    {
        _adb.Shell($"am start -n {packageName}/com.epicgames.ue4.SplashActivity");
    }

    private async Task<OperationResult> ApplyKoreanFullHdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(CurrentPackage))
        {
            return OperationResult.Fail("PUBG Mobile KR is not connected.");
        }

        var dataPath = $"/sdcard/Android/data/{CurrentPackage}";
        var obbPath = $"/sdcard/Android/obb/{CurrentPackage}";
        var configPath = $"{dataPath}/files/UE4Game/ShadowTrackerExtra/ShadowTrackerExtra/Saved/Config/Android/UserCustom.ini";
        var temporaryAccountBackupPath = "/sdcard/mk_safe_folder";
        var appAccountDataPath = $"/data/data/{CurrentPackage}";

        var koreanResolutionAssetPath = _storage.KoreanResolutionAssetPath;
        if (!File.Exists(koreanResolutionAssetPath) || !await _adb.PushAsync(koreanResolutionAssetPath, configPath, cancellationToken))
        {
            return OperationResult.Fail("Could not apply the PUBG KR resolution file.");
        }

        BackupAccountCredentials(appAccountDataPath, temporaryAccountBackupPath);

        cancellationToken.ThrowIfCancellationRequested();
        BackupRemoteFolder(dataPath, cancellationToken);
        BackupRemoteFolder(obbPath, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        ResetPackageAndGrantPermissions(CurrentPackage);

        cancellationToken.ThrowIfCancellationRequested();
        RestoreRemoteFolder(dataPath, cancellationToken);
        RestoreRemoteFolder(obbPath, cancellationToken);

        RestoreAccountCredentials(temporaryAccountBackupPath, appAccountDataPath);
        RelaunchPubgActivity(CurrentPackage);
        _adb.Shell($"rm -r {temporaryAccountBackupPath}");

        return OperationResult.Ok("Graphics settings applied and PUBG KR set to 1080p.");
    }

    private void BackupAccountCredentials(string appAccountDataPath, string temporaryBackupPath)
    {
        _adb.Shell($"mkdir -p {temporaryBackupPath}");
        _adb.Shell($"cp -r {appAccountDataPath}/shared_prefs {temporaryBackupPath}/shared_prefs");
        _adb.Shell($"cp -r {appAccountDataPath}/databases {temporaryBackupPath}/databases");
    }

    private void ResetPackageAndGrantPermissions(string packageName)
    {
        _adb.Shell($"pm clear {packageName}");
        _adb.Shell($"pm grant {packageName} android.permission.READ_EXTERNAL_STORAGE");
        _adb.Shell($"pm grant {packageName} android.permission.WRITE_EXTERNAL_STORAGE");
    }

    private void RestoreAccountCredentials(string temporaryBackupPath, string appAccountDataPath)
    {
        _adb.Shell($"cp -r {temporaryBackupPath}/shared_prefs {appAccountDataPath}/shared_prefs");
        _adb.Shell($"cp -r {temporaryBackupPath}/databases {appAccountDataPath}/databases");
    }

    private void BackupRemoteFolder(string remotePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backupPath = remotePath + ".MKbackup";
        var exists = _adb.Shell($"[ -d {remotePath} ] && echo 1 || echo 0").Trim() == "1";
        var backupExists = _adb.Shell($"[ -d {backupPath} ] && echo 1 || echo 0").Trim() == "1";
        if (!backupExists && exists)
        {
            _adb.Shell($"mv {remotePath} {backupPath}");
        }
        else if (backupExists && exists)
        {
            _adb.Shell($"rm -r {remotePath}");
        }
    }

    private void RestoreRemoteFolder(string remotePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backupPath = remotePath + ".MKbackup";
        if (_adb.Shell($"[ -d {backupPath} ] && echo 1 || echo 0").Trim() == "1")
        {
            _adb.Shell($"mv {backupPath} {remotePath}");
        }
    }

    public static bool IsGameLoopRunning()
    {
        return AppConstants.Emulator.RunningCheckProcessNames.Any(name => Process.GetProcessesByName(name).Length > 0);
    }

    private void PrepareWorkingFiles() => _storage.PrepareWorkingFiles();

    #region Internal & Private Seams (Preserved for Tests & Backward Compatibility)

    private byte ReadProperty(string name)
    {
        if (_activeSavContent is null)
        {
            return 0;
        }

        return new Ue4SavEditor(_activeSavContent).ReadProperty(name);
    }

    private bool ChangeProperty(string name, byte value)
    {
        if (_activeSavContent is null)
        {
            return false;
        }

        return new Ue4SavEditor(_activeSavContent).ChangeProperty(name, value);
    }

    private static byte[] CreateHeader(string propertyName) => Ue4SavEditor.CreateHeader(propertyName);

    private static int FindSequence(byte[] source, byte[] sequence) => Ue4SavEditor.FindSequence(source, sequence);

    private static string EncodeCVar(string name, string value) => UnrealCVarCodec.EncodeCVar(name, value);

    private static string DecodeCVar(string encoded) => UnrealCVarCodec.DecodeCVar(encoded);

    private OperationResult UpdateShadow(bool enable) => UnrealCVarCodec.UpdateShadowFile(_storage.ShadowSettingsPath, enable);

    #endregion
}
