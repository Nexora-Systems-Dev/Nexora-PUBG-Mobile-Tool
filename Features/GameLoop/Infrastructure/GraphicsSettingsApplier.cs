namespace Nexora.Features.GameLoop.Infrastructure;
using Nexora.Configuration;

using Nexora.Shared.Kernel;
using Nexora.Features.Graphics.Domain;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Shared.Contracts;
using Nexora.Infrastructure.Processes;

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

        // Non-KR apply: the only remaining device command is the relaunch,
        // which cannot lose user data, so a failure is reported in the message
        // rather than turning a successful file deployment into a failure.
        var log = new DeviceIssueLog();
        await RelaunchPubgActivityAsync(packageName, log, cancellationToken);

        return log.Issues.Count == 0
            ? OperationResult.Ok("Graphics settings applied successfully.")
            : OperationResult.Ok($"Graphics settings applied successfully. Issues: {log.Describe()}");
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

    /// <summary>
    /// Runs a device command that mutates or destroys user data (the KR
    /// sequence's <c>pm clear</c> / <c>mv</c> / <c>cp -r</c> / <c>rm -r</c>).
    /// The first one that fails aborts the whole apply with a message naming the
    /// command: a failed account copy must never reach the restore step, and the
    /// success message must not be issued over a command that never ran.
    /// </summary>
    /// <returns>Null when the command ran; the failure message otherwise.</returns>
    private async Task<string?> RunDestructiveAsync(string command, DeviceIssueLog log, CancellationToken cancellationToken)
    {
        var result = await _adb.ShellAsync(command, cancellationToken);
        if (result.Succeeded)
        {
            return null;
        }

        return $"Command '{command}' failed: {ProcessText.GetError(result)}{log.DescribeSuffix()}";
    }

    /// <summary>
    /// Runs a device command that cannot lose user data (<c>mkdir</c>,
    /// <c>pm grant</c>, <c>am start</c>, <c>am force-stop</c>). A failure is
    /// reported in the result message rather than aborting, because the apply's
    /// outcome no longer depends on it.
    /// </summary>
    private async Task RunNonDestructiveAsync(string command, DeviceIssueLog log, CancellationToken cancellationToken)
    {
        var result = await _adb.ShellAsync(command, cancellationToken);
        if (!result.Succeeded)
        {
            log.Issues.Add($"Command '{command}' failed: {ProcessText.GetError(result)}");
        }
    }

    private async Task<OperationResult> DeployConfigurationFilesAsync(string packageName, CancellationToken cancellationToken)
    {
        var remoteDataRoot = RemotePaths.For(packageName).SavedRoot;

        cancellationToken.ThrowIfCancellationRequested();
        var log = new DeviceIssueLog();
        await RunNonDestructiveAsync($"am force-stop {packageName}", log, cancellationToken);
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

        return OperationResult.Ok(log.Issues.Count == 0
            ? "Configuration files deployed."
            : $"Configuration files deployed. Issues: {log.Describe()}");
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

        var log = new DeviceIssueLog();

        // Account credentials are copied off before anything destructive runs,
        // because pm clear below removes them. A failed copy aborts before the
        // clear, so the restore step can never run against an empty backup.
        var abort = await BackupAccountCredentialsAsync(appAccountDataPath, temporaryAccountBackupPath, log, cancellationToken);
        if (abort is not null)
        {
            return OperationResult.Fail(abort);
        }

        cancellationToken.ThrowIfCancellationRequested();
        abort = await BackupRemoteFolderAsync(dataPath, log, cancellationToken);
        if (abort is not null)
        {
            return OperationResult.Fail(abort);
        }

        abort = await BackupRemoteFolderAsync(obbPath, log, cancellationToken);
        if (abort is not null)
        {
            return OperationResult.Fail(abort);
        }

        // The reset below destroys the package's data. Every folder decision so
        // far rested on a read-only probe, so if not one probe answered, the
        // device state is unknown and the wipe must not run on a guess.
        if (log.NothingWasVerified)
        {
            return OperationResult.Fail(
                "Could not verify the PUBG KR folders on the device, so the package reset was skipped to avoid clearing data with no confirmed backup." + log.DescribeSuffix());
        }

        cancellationToken.ThrowIfCancellationRequested();
        abort = await ResetPackageAndGrantPermissionsAsync(packageName, log, cancellationToken);
        if (abort is not null)
        {
            return OperationResult.Fail(abort);
        }

        cancellationToken.ThrowIfCancellationRequested();
        abort = await RestoreRemoteFolderAsync(dataPath, log, cancellationToken);
        if (abort is not null)
        {
            return OperationResult.Fail(abort);
        }

        abort = await RestoreRemoteFolderAsync(obbPath, log, cancellationToken);
        if (abort is not null)
        {
            return OperationResult.Fail(abort);
        }

        // The account copy back is destructive too: a failed restore is not the
        // outcome the user is being told about, so it must not be reported as a
        // 1080p success.
        abort = await RestoreAccountCredentialsAsync(temporaryAccountBackupPath, appAccountDataPath, log, cancellationToken);
        if (abort is not null)
        {
            return OperationResult.Fail(abort);
        }

        await RunNonDestructiveAsync($"am start -n {packageName}/com.epicgames.ue4.SplashActivity", log, cancellationToken);

        // The temporary backup is our own scratch folder, not user data, but a
        // failed cleanup leaves account files behind on the device, so it is
        // destructive-classified and aborts rather than being quietly noted.
        abort = await RunDestructiveAsync($"rm -r {temporaryAccountBackupPath}", log, cancellationToken);
        if (abort is not null)
        {
            return OperationResult.Fail(abort);
        }

        return log.Issues.Count == 0
            ? OperationResult.Ok("Graphics settings applied and PUBG KR set to 1080p.")
            : OperationResult.Ok($"Graphics settings applied and PUBG KR set to 1080p. Issues: {log.Describe()}");
    }

    private async Task<string?> BackupAccountCredentialsAsync(string appAccountDataPath, string temporaryBackupPath, DeviceIssueLog log, CancellationToken cancellationToken)
    {
        // mkdir and the copy-out build the backup the restore step depends on,
        // so both are destructive-classified: a failure here is a failure of the
        // whole account-restore contract.
        var abort = await RunDestructiveAsync($"mkdir -p {temporaryBackupPath}", log, cancellationToken);
        if (abort is not null)
        {
            return abort;
        }

        abort = await RunDestructiveAsync($"cp -r {appAccountDataPath}/shared_prefs {temporaryBackupPath}/shared_prefs", log, cancellationToken);
        if (abort is not null)
        {
            return abort;
        }

        return await RunDestructiveAsync($"cp -r {appAccountDataPath}/databases {temporaryBackupPath}/databases", log, cancellationToken);
    }

    private async Task<string?> ResetPackageAndGrantPermissionsAsync(string packageName, DeviceIssueLog log, CancellationToken cancellationToken)
    {
        // pm clear wipes the package's data: the first failure aborts so the
        // restore phase never runs against a package in an unknown state.
        var abort = await RunDestructiveAsync($"pm clear {packageName}", log, cancellationToken);
        if (abort is not null)
        {
            return abort;
        }

        // Grants repair a cleared package and cannot lose data, so they are
        // reported rather than fatal.
        await RunNonDestructiveAsync($"pm grant {packageName} android.permission.READ_EXTERNAL_STORAGE", log, cancellationToken);
        await RunNonDestructiveAsync($"pm grant {packageName} android.permission.WRITE_EXTERNAL_STORAGE", log, cancellationToken);

        return null;
    }

    private async Task<string?> RestoreAccountCredentialsAsync(string temporaryBackupPath, string appAccountDataPath, DeviceIssueLog log, CancellationToken cancellationToken)
    {
        var abort = await RunDestructiveAsync($"cp -r {temporaryBackupPath}/shared_prefs {appAccountDataPath}/shared_prefs", log, cancellationToken);
        if (abort is not null)
        {
            return abort;
        }

        return await RunDestructiveAsync($"cp -r {temporaryBackupPath}/databases {appAccountDataPath}/databases", log, cancellationToken);
    }

    private Task RelaunchPubgActivityAsync(string packageName, DeviceIssueLog log, CancellationToken cancellationToken) =>
        RunNonDestructiveAsync($"am start -n {packageName}/com.epicgames.ue4.SplashActivity", log, cancellationToken);

    private async Task<string?> BackupRemoteFolderAsync(string remotePath, DeviceIssueLog log, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backupPath = remotePath + RemoteBackupExtension;
        var legacyBackupPath = remotePath + LegacyRemoteBackupExtension;
        var exists = await RemoteFolderExistsAsync(remotePath, log, cancellationToken);
        var backupExists = await RemoteFolderExistsAsync(backupPath, log, cancellationToken);
        var legacyBackupExists = await RemoteFolderExistsAsync(legacyBackupPath, log, cancellationToken);
        // Either backup already preserves the original, so only a missing
        // pair snapshots; a present pair means re-apply over restored data.
        // Every branch requires the probe to have answered, so an unanswered
        // probe falls through to the caller's "nothing was verified" gate
        // instead of being read as a confident "folder absent".
        if (exists is { Answered: true, Exists: true })
        {
            if (backupExists is { Answered: true, Exists: false } && legacyBackupExists is { Answered: true, Exists: false })
            {
                return await RunDestructiveAsync($"mv {remotePath} {backupPath}", log, cancellationToken);
            }

            if (backupExists is { Answered: true, Exists: true } || legacyBackupExists is { Answered: true, Exists: true })
            {
                return await RunDestructiveAsync($"rm -r {remotePath}", log, cancellationToken);
            }

            // Folder present but the backup pair only partly known: skipping the
            // backup while the wipe below proceeds would clear data with no
            // confirmed backup, so abort instead of guessing. (All-unanswered
            // defers to the caller's nothing-verified gate, which owns that message.)
            return $"Could not verify the backup state of '{remotePath}' on the device; the backup was skipped so the package reset below cannot run on a guess.";
        }

        if (exists is { Answered: false } && (backupExists.Answered || legacyBackupExists.Answered))
        {
            // Presence itself unknown while sibling probes answered: the folder
            // below may hold data the wipe must not touch unseen.
            return $"Could not verify whether '{remotePath}' exists on the device; the backup was skipped so the package reset below cannot run on a guess.";
        }

        return null;
    }

    private async Task<string?> RestoreRemoteFolderAsync(string remotePath, DeviceIssueLog log, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backupPath = remotePath + RemoteBackupExtension;
        var legacyBackupPath = remotePath + LegacyRemoteBackupExtension;
        // New wins when both exist (a legacy orphan is left in place, never
        // auto-migrated); legacy restores keep pre-rename users working.
        // The second probe only runs when no new backup exists, so the
        // common path costs the same single round trip as before.
        var newBackup = await RemoteFolderExistsAsync(backupPath, log, cancellationToken);
        if (newBackup is { Answered: true, Exists: true })
        {
            return await RunDestructiveAsync($"mv {backupPath} {remotePath}", log, cancellationToken);
        }

        var legacyBackup = await RemoteFolderExistsAsync(legacyBackupPath, log, cancellationToken);
        if (legacyBackup is { Answered: true, Exists: true })
        {
            return await RunDestructiveAsync($"mv {legacyBackupPath} {remotePath}", log, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// A single read-only shell round trip asking whether a remote directory
    /// exists. The answer is a <see cref="RemoteProbe"/> rather than a bool on
    /// purpose: through the old sync shell, a failed probe and an answered "no"
    /// both came back falsy, so a broken device looked like an empty one. The
    /// caller distinguishes them and only acts on an answer.
    /// </summary>
    private async Task<RemoteProbe> RemoteFolderExistsAsync(string remotePath, DeviceIssueLog log, CancellationToken cancellationToken)
    {
        var result = await _adb.ShellAsync($"[ -d {remotePath} ] && echo 1 || echo 0", cancellationToken);
        if (!result.Succeeded)
        {
            log.Issues.Add($"Could not verify whether '{remotePath}' exists: {ProcessText.GetError(result)}");
            return log.RecordUnanswered();
        }

        return log.RecordAnswered(result.StandardOutput.Trim() == "1");
    }

    /// <summary>
    /// One apply's device-side report: the non-fatal command failures to fold
    /// into the result message, and the count of read-only probes that answered,
    /// which is what tells "the device said no" from "the device never
    /// answered". Kept per apply, never as instance state, so concurrent
    /// applies through the shared applier cannot cross-record.
    /// </summary>
    private sealed class DeviceIssueLog
    {
        public List<string> Issues { get; } = new();

        public int ProbesAttempted { get; private set; }

        public int ProbesAnswered { get; private set; }

        /// <summary>Every probe ran and none answered: the device state is unknown.</summary>
        public bool NothingWasVerified => ProbesAttempted > 0 && ProbesAnswered == 0;

        public RemoteProbe RecordAnswered(bool exists)
        {
            ProbesAttempted++;
            ProbesAnswered++;
            return new RemoteProbe(true, exists);
        }

        public RemoteProbe RecordUnanswered()
        {
            ProbesAttempted++;
            return new RemoteProbe(false, false);
        }

        public string Describe() => string.Join("; ", Issues);

        /// <summary>The issues, formatted to follow an existing sentence.</summary>
        public string DescribeSuffix() => Issues.Count == 0 ? string.Empty : $" Issues: {Describe()}";
    }

    /// <summary>
    /// The outcome of a read-only existence probe. <see cref="Answered"/> is the
    /// half the caller must check first: an unanswered probe carries no
    /// information about the folder at all.
    /// </summary>
    private readonly record struct RemoteProbe(bool Answered, bool Exists);

    private bool ChangeProperty(string name, byte value)
    {
        if (_session.ActiveSavContent is null)
        {
            return false;
        }

        return new Ue4SavEditor(_session.ActiveSavContent).ChangeProperty(name, value);
    }
}
