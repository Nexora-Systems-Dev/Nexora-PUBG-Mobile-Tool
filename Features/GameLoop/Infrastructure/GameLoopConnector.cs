namespace Nexora.Features.GameLoop.Infrastructure;
using Nexora.Configuration;

using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Nexora.Features.Graphics.Domain;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Infrastructure.Registry;
using Nexora.Shared.Contracts;

/// <summary>
/// Establishes GameLoop ADB connectivity and loads PUBG Mobile graphics profiles.
/// </summary>
public sealed class GameLoopConnector
{
    private readonly IUserRegistry _registry;
    private readonly IAdbClient _adb;
    private readonly GameLoopWorkingStorage _storage;
    private readonly IFileSystem _fileSystem;
    private readonly GameLoopSession _session;
    private readonly IGameLoopProcessService _processes;
    private readonly GameLoopOptions _gameLoop;

    public GameLoopConnector(
        IUserRegistry registry,
        IAdbClient adb,
        GameLoopWorkingStorage storage,
        IFileSystem fileSystem,
        GameLoopSession session,
        IGameLoopProcessService processService,
        GameLoopOptions? gameLoop = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _processes = processService ?? throw new ArgumentNullException(nameof(processService));
        _gameLoop = gameLoop ?? new GameLoopOptions();
    }

    /// <summary>
    /// Checks prerequisites, waits for ADB boot, and detects installed PUBG Mobile packages.
    /// Phase reports flow to the caller's progress sink; the sync prerequisite
    /// checks below stay on-thread (named in the F4 report) because they are
    /// in-process reads with no child processes behind them.
    /// </summary>
    public async Task<ConnectionResult> ConnectAsync(CancellationToken cancellationToken, IProgress<string>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _session.Reset();

        progress?.Report("Checking GameLoop status...");
        var registryFailure = CheckAdbRegistryStatus();
        if (registryFailure is not null) return registryFailure;

        var livenessFailure = CheckGameLoopRunning();
        if (livenessFailure is not null) return livenessFailure;

        progress?.Report("Waiting for the emulator to finish booting...");
        if (!await EnsureBootCompletedAsync(cancellationToken, progress))
        {
            return new ConnectionResult(false, "GameLoop ADB did not finish booting.", Array.Empty<PubgVersion>());
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("Reading the connection probe...");
        if (!await PullConnectionProbeAsync(cancellationToken, progress))
        {
            return new ConnectionResult(false, "Could not connect to GameLoop ADB.", Array.Empty<PubgVersion>());
        }

        progress?.Report("Detecting installed versions...");
        var installedVersions = await DetectInstalledVersionsAsync(cancellationToken, progress);
        if (installedVersions.Count == 0)
        {
            return new ConnectionResult(false, "No supported PUBG Mobile version was found.", installedVersions);
        }

        // ADB connection is established; graphics profile loading is best-effort.
        _session.MarkAdbConnected();
        return await SelectVersionAsync(installedVersions, cancellationToken);
    }

    /// <summary>
    /// Reads the GameLoop ADB registry flag and auto-enables ADB when disabled.
    /// </summary>
    /// <returns>A failure result when connection cannot proceed; null when ADB is enabled.</returns>
    private ConnectionResult? CheckAdbRegistryStatus()
    {
        var adbStatus = _registry.GetUserDword(_gameLoop.Registry.ValueAdbDisable);
        if (adbStatus is null)
        {
            return new ConnectionResult(false, "Could not read GameLoop ADB status.", Array.Empty<PubgVersion>());
        }

        if (adbStatus == 1)
        {
            _registry.SetUserDword(_gameLoop.Registry.ValueAdbDisable, 0);
            return new ConnectionResult(false, "ADB was enabled. Restart GameLoop and try again.", Array.Empty<PubgVersion>());
        }

        return null;
    }

    /// <summary>
    /// Verifies a path-checked GameLoop emulator process is running.
    /// </summary>
    /// <returns>A failure result when GameLoop is not running; null otherwise.</returns>
    private ConnectionResult? CheckGameLoopRunning()
    {
        if (IsGameLoopRunning()) return null;
        return new ConnectionResult(false, "GameLoop is not running.", Array.Empty<PubgVersion>());
    }

    /// <summary>
    /// Waits for the ADB bridge to finish booting, stopping ADB on failure.
    /// </summary>
    private async Task<bool> EnsureBootCompletedAsync(CancellationToken cancellationToken, IProgress<string>? progress)
    {
        if (await _adb.WaitForBootAsync(cancellationToken, progress)) return true;
        await _adb.StopAdbAsync(cancellationToken);
        return false;
    }

    /// <summary>
    /// Pulls the connection probe file, stopping ADB on failure.
    /// </summary>
    private async Task<bool> PullConnectionProbeAsync(CancellationToken cancellationToken, IProgress<string>? progress)
    {
        _storage.EnsureDirectoryCreated();
        if (await _adb.PullAsync("/default.prop", _storage.ConnectionProbePath, cancellationToken, progress)) return true;
        await _adb.StopAdbAsync(cancellationToken);
        return false;
    }

    /// <summary>
    /// Detects installed PUBG Mobile packages and maps them to known versions.
    /// </summary>
    private async Task<List<PubgVersion>> DetectInstalledVersionsAsync(CancellationToken cancellationToken, IProgress<string>? progress)
    {
        var installedPackages = await _adb.FindInstalledPackagesAsync(PubgVersionCatalog.PubgVersions.Keys, cancellationToken, progress);
        return installedPackages
            .Where(PubgVersionCatalog.PubgVersions.ContainsKey)
            .Select(package => new PubgVersion(package, PubgVersionCatalog.PubgVersions[package]))
            .ToList();
    }

    /// <summary>
    /// Loads the single installed version, or asks the user to select one.
    /// </summary>
    private async Task<ConnectionResult> SelectVersionAsync(List<PubgVersion> installedVersions, CancellationToken cancellationToken)
    {
        if (installedVersions.Count == 1)
        {
            var loadResult = await LoadVersionAsync(installedVersions[0].PackageName, cancellationToken);
            return loadResult.Success
                ? new ConnectionResult(true, $"Using {installedVersions[0].DisplayName}.", installedVersions)
                : new ConnectionResult(true,
                    $"Connected to {installedVersions[0].DisplayName}. Graphics profile unavailable: {loadResult.Message}",
                    installedVersions);
        }

        return new ConnectionResult(true, "Select the PUBG Mobile version to use.", installedVersions);
    }

    /// <summary>
    /// Loads the graphics configuration files for the specified package.
    /// </summary>
    public async Task<OperationResult> LoadVersionAsync(string packageName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!AppConstants.Validation.IsValidAndroidPackageName(packageName))
        {
            return OperationResult.Fail("Invalid PUBG package name.");
        }

        if (!PubgVersionCatalog.PubgVersions.ContainsKey(packageName))
        {
            return OperationResult.Fail("Unsupported PUBG Mobile version.");
        }

        _storage.PrepareWorkingFiles();
        var remoteSavPath = RemotePaths.For(packageName).ActiveSavPath;

        if (!await _adb.PullAsync(remoteSavPath, _storage.PreviousSavPath, cancellationToken))
        {
            return OperationResult.Fail("Could not read the PUBG graphics file from GameLoop.");
        }

        try
        {
            _session.LoadVersion(_fileSystem.ReadAllBytes(_storage.PreviousSavPath), packageName);

            var remoteShadowPath = RemotePaths.For(packageName).UserCustomIniPath;
            await _adb.PullAsync(remoteShadowPath, _storage.ShadowSettingsPath, cancellationToken);

            return OperationResult.Ok($"Connected to {PubgVersionCatalog.PubgVersions[packageName]}.");
        }
        catch (IOException ex)
        {
            return OperationResult.Fail($"Could not read the graphics file: {ex.Message}");
        }
    }

    /// <summary>
    /// Reports whether any path-verified GameLoop emulator process is running,
    /// delegating to the single process-discovery home
    /// (<see cref="IGameLoopProcessService"/>) instead of trusting bare process
    /// names. Returned processes are disposed before returning.
    /// </summary>
    private bool IsGameLoopRunning()
    {
        var processes = _processes.FindGameLoopProcesses();
        try
        {
            return processes.Count > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
