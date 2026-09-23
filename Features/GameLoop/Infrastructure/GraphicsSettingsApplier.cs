namespace Nexora.Features.GameLoop.Infrastructure;
using Nexora.Configuration;

using Nexora.Shared.Kernel;
using Nexora.Features.Graphics.Domain;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Shared.Contracts;

/// <summary>
/// Applies graphics settings to the active PUBG Mobile installation and deploys
/// the resulting configuration files to the GameLoop emulator.
/// </summary>
public sealed class GraphicsSettingsApplier
{
    private readonly IAdbClient _adb;
    private readonly GameLoopWorkingStorage _storage;
    private readonly IFileSystem _fileSystem;
    private readonly GameLoopSession _session;
    private readonly ShadowSettingsStore _shadowStore;
    private readonly GameLoopOptions _gameLoop;

    /// <summary>
    /// On-device backup suffix for KR folders. The pre-rename
    /// <see cref="LegacyRemoteBackupExtension"/> is honored as a permanent
    /// read fallback (check new, then old) but never written.
    /// </summary>
    private const string RemoteBackupExtension = ".nexora-backup";

    private const string LegacyRemoteBackupExtension = ".MKbackup";

    public GraphicsSettingsApplier(
        IAdbClient adb,
        GameLoopWorkingStorage storage,
        IFileSystem fileSystem,
        GameLoopSession session,
        GameLoopOptions? gameLoop = null)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _shadowStore = new ShadowSettingsStore(fileSystem);
        _gameLoop = gameLoop ?? new GameLoopOptions();
    }

    /// <summary>
    /// Applies selected graphics settings to the active PUBG Mobile installation.
    /// </summary>
    public async Task<OperationResult> ApplyGraphicsAsync(GraphicsSelection selection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_session.IsConnected || _session.ActiveSavContent is null || string.IsNullOrWhiteSpace(_session.CurrentPackage))
        {
            return OperationResult.Fail("Connect to GameLoop first.");
        }

        var activeSavContent = _session.ActiveSavContent;
        var packageName = _session.CurrentPackage;

        if (!PubgVersionCatalog.TryResolveGraphicsValues(selection, out var qualityByte, out var fpsByte, out var styleByte))
        {
            return OperationResult.Fail("One or more graphics settings are invalid.");
        }

        var savUpdateResult = UpdateGraphicsSavProperties(qualityByte, fpsByte, styleByte);
        if (!savUpdateResult.Success)
        {
            return savUpdateResult;
        }

        var shadowResult = _shadowStore.UpdateFile(_storage.ShadowSettingsPath, selection.EnableShadow);
        if (!shadowResult.Success)
        {
            return shadowResult;
        }

        _storage.PrepareWorkingFiles();
        _fileSystem.WriteAllBytes(_storage.PendingSavPath, activeSavContent);

        var deployResult = await DeployConfigurationFilesAsync(packageName, cancellationToken);
        if (!deployResult.Success)
        {
            return deployResult;
        }

        if (selection.EnableKoreanFullHd && packageName.Equals(PubgVersionCatalog.KoreanPackage, StringComparison.OrdinalIgnoreCase))
        {
            return await ApplyKoreanFullHdAsync(cancellationToken);
        }

        RelaunchPubgActivity(packageName, cancellationToken);
        return OperationResult.Ok("Graphics settings applied successfully.");
    }

    private OperationResult UpdateGraphicsSavProperties(byte qualityByte, byte fpsByte, byte styleByte)
    {
        // BattleRenderQuality is primary; optional lobby fields may not exist in all versions.
        if (!TryUpdateAll(new[] { "ArtQuality", "LobbyRenderQuality", "BattleRenderQuality" }, qualityByte))
        {
            return OperationResult.Fail("Could not update the graphics quality in the PUBG profile.");
        }

        if (!TryUpdateAll(new[] { "FPSLevel", "BattleFPS", "LobbyFPS" }, fpsByte))
        {
            return OperationResult.Fail("Could not update the frame rate in the PUBG profile.");
        }

        if (!ChangeProperty("BattleRenderStyle", styleByte))
        {
            return OperationResult.Fail("Could not update the graphics style.");
        }

        return OperationResult.Ok("Graphics settings updated in save buffer.");
    }

    /// <summary>
    /// Writes the byte to every listed property the save actually contains,
    /// returning whether any were updated. Every candidate is attempted — the
    /// non-short-circuiting OR is load-bearing: stopping at the first hit would
    /// leave a version's other fallback fields holding a stale value.
    /// </summary>
    private bool TryUpdateAll(string[] propertyNames, byte value)
    {
        var updated = false;
        foreach (var property in propertyNames)
        {
            updated |= ChangeProperty(property, value);
        }

        return updated;
    }

    private async Task<OperationResult> DeployConfigurationFilesAsync(string packageName, CancellationToken cancellationToken)
    {
        var remoteDataRoot = RemotePaths.For(packageName).SavedRoot;

        cancellationToken.ThrowIfCancellationRequested();
        _adb.Shell($"am force-stop {packageName}", cancellationToken);
        await Task.Delay(_gameLoop.Timeouts.ForceStopSettleDelayMilliseconds, cancellationToken);

        if (!await _adb.PushAsync(_storage.PendingSavPath, $"{remoteDataRoot}/SaveGames/Active.sav", cancellationToken))
        {
            return OperationResult.Fail("Could not apply the graphics file to GameLoop.");
        }

        var localShadowPath = _storage.ShadowSettingsPath;
        if (_fileSystem.Exists(localShadowPath) && !await _adb.PushAsync(localShadowPath, $"{remoteDataRoot}/Config/Android/UserCustom.ini", cancellationToken))
        {
            return OperationResult.Fail("Could not apply the shadow setting to GameLoop.");
        }

        return OperationResult.Ok("Configuration files deployed.");
    }

    private async Task<OperationResult> ApplyKoreanFullHdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_session.CurrentPackage))
        {
            return OperationResult.Fail("PUBG Mobile KR is not connected.");
        }

        var packageName = _session.CurrentPackage;
        var dataPath = $"/sdcard/Android/data/{packageName}";
        var obbPath = $"/sdcard/Android/obb/{packageName}";
        var configPath = RemotePaths.For(packageName).UserCustomIniPath;
        var temporaryAccountBackupPath = "/sdcard/mk_safe_folder";
        var appAccountDataPath = $"/data/data/{packageName}";

        var koreanResolutionAssetPath = _storage.KoreanResolutionAssetPath;
        if (!_fileSystem.Exists(koreanResolutionAssetPath) || !await _adb.PushAsync(koreanResolutionAssetPath, configPath, cancellationToken))
        {
            return OperationResult.Fail("Could not apply the PUBG KR resolution file.");
        }

        BackupAccountCredentials(appAccountDataPath, temporaryAccountBackupPath, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        BackupRemoteFolder(dataPath, cancellationToken);
        BackupRemoteFolder(obbPath, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        ResetPackageAndGrantPermissions(packageName, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        RestoreRemoteFolder(dataPath, cancellationToken);
        RestoreRemoteFolder(obbPath, cancellationToken);

        RestoreAccountCredentials(temporaryAccountBackupPath, appAccountDataPath, cancellationToken);
        RelaunchPubgActivity(packageName, cancellationToken);
        _adb.Shell($"rm -r {temporaryAccountBackupPath}", cancellationToken);

        return OperationResult.Ok("Graphics settings applied and PUBG KR set to 1080p.");
    }

    private void BackupAccountCredentials(string appAccountDataPath, string temporaryBackupPath, CancellationToken cancellationToken)
    {
        _adb.Shell($"mkdir -p {temporaryBackupPath}", cancellationToken);
        _adb.Shell($"cp -r {appAccountDataPath}/shared_prefs {temporaryBackupPath}/shared_prefs", cancellationToken);
        _adb.Shell($"cp -r {appAccountDataPath}/databases {temporaryBackupPath}/databases", cancellationToken);
    }

    private void ResetPackageAndGrantPermissions(string packageName, CancellationToken cancellationToken)
    {
        _adb.Shell($"pm clear {packageName}", cancellationToken);
        _adb.Shell($"pm grant {packageName} android.permission.READ_EXTERNAL_STORAGE", cancellationToken);
        _adb.Shell($"pm grant {packageName} android.permission.WRITE_EXTERNAL_STORAGE", cancellationToken);
    }

    private void RestoreAccountCredentials(string temporaryBackupPath, string appAccountDataPath, CancellationToken cancellationToken)
    {
        _adb.Shell($"cp -r {temporaryBackupPath}/shared_prefs {appAccountDataPath}/shared_prefs", cancellationToken);
        _adb.Shell($"cp -r {temporaryBackupPath}/databases {appAccountDataPath}/databases", cancellationToken);
    }

    private void RelaunchPubgActivity(string packageName, CancellationToken cancellationToken)
    {
        _adb.Shell($"am start -n {packageName}/com.epicgames.ue4.SplashActivity", cancellationToken);
    }

    private void BackupRemoteFolder(string remotePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backupPath = remotePath + RemoteBackupExtension;
        var legacyBackupPath = remotePath + LegacyRemoteBackupExtension;
        var exists = RemoteFolderExists(remotePath, cancellationToken);
        var backupExists = RemoteFolderExists(backupPath, cancellationToken);
        var legacyBackupExists = RemoteFolderExists(legacyBackupPath, cancellationToken);
        // Either backup already preserves the original, so only a missing
        // pair snapshots; a present pair means re-apply over restored data.
        if (!backupExists && !legacyBackupExists && exists)
        {
            _adb.Shell($"mv {remotePath} {backupPath}", cancellationToken);
        }
        else if ((backupExists || legacyBackupExists) && exists)
        {
            _adb.Shell($"rm -r {remotePath}", cancellationToken);
        }
    }

    private void RestoreRemoteFolder(string remotePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backupPath = remotePath + RemoteBackupExtension;
        var legacyBackupPath = remotePath + LegacyRemoteBackupExtension;
        // New wins when both exist (a legacy orphan is left in place, never
        // auto-migrated); legacy restores keep pre-rename users working.
        // The second probe only runs when no new backup exists, so the
        // common path costs the same single round trip as before.
        if (RemoteFolderExists(backupPath, cancellationToken))
        {
            _adb.Shell($"mv {backupPath} {remotePath}", cancellationToken);
        }
        else if (RemoteFolderExists(legacyBackupPath, cancellationToken))
        {
            _adb.Shell($"mv {legacyBackupPath} {remotePath}", cancellationToken);
        }
    }

    /// <summary>
    /// A single shell round trip asking whether a remote directory exists.
    /// </summary>
    private bool RemoteFolderExists(string remotePath, CancellationToken cancellationToken) =>
        _adb.Shell($"[ -d {remotePath} ] && echo 1 || echo 0", cancellationToken).Trim() == "1";

    private bool ChangeProperty(string name, byte value)
    {
        if (_session.ActiveSavContent is null)
        {
            return false;
        }

        return new Ue4SavEditor(_session.ActiveSavContent).ChangeProperty(name, value);
    }
}
