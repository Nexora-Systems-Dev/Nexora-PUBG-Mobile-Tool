using Nexora.Configuration;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

public sealed class AdbClient : IAdbClient
{
    /// <summary>
    /// How long a resolved ADB path is reused before re-probing the installation.
    /// Short enough that a GameLoop install/repair is picked up promptly, long
    /// enough that per-command registry/process scans don't tax every call.
    /// </summary>
    private static readonly TimeSpan AdbPathCacheTtl = TimeSpan.FromMinutes(1);

    private readonly IProcessRunner _runner;
    private readonly IGameLoopPathResolver _paths;
    private readonly GameLoopOptions _gameLoop;
    private readonly EmulatorOptions _emulator;
    private readonly object _adbPathLock = new();
    private string? _adbPath;
    private DateTime _adbPathResolvedAtUtc;
    private string? _deviceSerial;

    public AdbClient(IProcessRunner runner, IGameLoopPathResolver pathResolver, GameLoopOptions? gameLoop = null, EmulatorOptions? emulator = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _paths = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _gameLoop = gameLoop ?? new GameLoopOptions();
        _emulator = emulator ?? new EmulatorOptions();
    }

    public string DeviceSerial => _deviceSerial ?? _gameLoop.Adb.PreferredSerial;

    public ProcessResult Run(params string[] arguments)
    {
        var adbPath = AdbPath;
        var result = _runner.Run(adbPath, arguments, _gameLoop.Timeouts.AdbCommandTimeout);
        if (!result.TimedOut && result.ExitCode == -1)
        {
            // The executable never ran (missing file, bad path): fail loudly
            // with the repair hint instead of a bare process-start error.
            return new ProcessResult(
                result.ExitCode,
                result.StandardOutput,
                $"ADB executable could not be started at '{adbPath}'. Repair the GameLoop installation or set NEXORA_GAMELOOP_ROOT to a custom install directory. Details: {result.StandardError}",
                result.TimedOut);
        }

        return result;
    }

    /// <summary>
    /// Re-probes the GameLoop installation for adb immediately, so a
    /// repair/reinstall is picked up without waiting for cache expiry.
    /// </summary>
    public void RefreshAdbPath()
    {
        lock (_adbPathLock)
        {
            _adbPath = FindAdbPath();
            _adbPathResolvedAtUtc = DateTime.UtcNow;
        }
    }

    private string AdbPath
    {
        get
        {
            lock (_adbPathLock)
            {
                if (_adbPath is null || DateTime.UtcNow - _adbPathResolvedAtUtc >= AdbPathCacheTtl)
                {
                    _adbPath = FindAdbPath();
                    _adbPathResolvedAtUtc = DateTime.UtcNow;
                }

                return _adbPath;
            }
        }
    }

    public string Shell(string command)
    {
        return Run("-s", DeviceSerial, "shell", command).StandardOutput.Trim();
    }

    public string Shell(string command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Shell(command);
    }

    public Task<bool> PullAsync(string remotePath, string localPath, CancellationToken cancellationToken)
    {
        return TransferWithRetryAsync("pull", remotePath, localPath, cancellationToken);
    }

    public Task<bool> PushAsync(string localPath, string remotePath, CancellationToken cancellationToken)
    {
        return TransferWithRetryAsync("push", localPath, remotePath, cancellationToken);
    }

    private async Task<bool> TransferWithRetryAsync(string operation, string source, string destination, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _gameLoop.Timeouts.AdbTransferMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = Run("-s", DeviceSerial, operation, source, destination);
            var transferred = result.Succeeded &&
                (operation.Equals("push", StringComparison.OrdinalIgnoreCase) || File.Exists(destination));
            if (transferred)
            {
                return true;
            }

            if (attempt == _gameLoop.Timeouts.AdbTransferMaxAttempts)
            {
                return false;
            }

            // Refresh device serial before retrying in case the bridge restarted.
            await Task.Delay(_gameLoop.Timeouts.AdbTransferRetryDelay, cancellationToken);
            TrySelectDevice(cancellationToken);
        }

        return false;
    }

    public async Task<bool> WaitForBootAsync(CancellationToken cancellationToken)
    {
        // Check cancellation before device selection runs child adb processes.
        cancellationToken.ThrowIfCancellationRequested();
        if (!TrySelectDevice(cancellationToken))
        {
            return false;
        }

        for (var attempt = 0; attempt < _gameLoop.Timeouts.AdbBootPollAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = Run("-s", DeviceSerial, "shell", "getprop", "dev.bootcomplete");
            if (result.Succeeded && result.StandardOutput.Trim() == "1")
            {
                return true;
            }

            if (!result.Succeeded ||
                result.StandardError.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                result.StandardOutput.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                TrySelectDevice(cancellationToken);
            }

            await Task.Delay(_gameLoop.Timeouts.AdbBootPollDelay, cancellationToken);
        }

        return false;
    }

    private bool TrySelectDevice(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var devices = Run("devices");
        if (!devices.Succeeded)
        {
            return false;
        }

        var connectedSerials = ParseDeviceSerials(devices.StandardOutput);

        _deviceSerial = SelectPreferredSerial(connectedSerials);

        if (!string.IsNullOrWhiteSpace(_deviceSerial))
        {
            return true;
        }

        // Connect to local loopback port if GameLoop exposes bridge on TCP 5555.
        cancellationToken.ThrowIfCancellationRequested();
        Run("connect", _gameLoop.Adb.LoopbackEndpoint);
        cancellationToken.ThrowIfCancellationRequested();
        devices = Run("devices");
        connectedSerials = ParseDeviceSerials(devices.StandardOutput);

        _deviceSerial = SelectPreferredSerial(connectedSerials);

        return !string.IsNullOrWhiteSpace(_deviceSerial);
    }

    /// <summary>
    /// Parses `adb devices` output into the serials of entries in the "device" state,
    /// skipping the header line and ignoring offline/unauthorized entries.
    /// </summary>
    internal static List<string> ParseDeviceSerials(string output)
    {
        return output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2 && string.Equals(parts[1], "device", StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[0])
            .ToList();
    }

    /// <summary>
    /// Prefers the configured serial, falling back to any serial with the TCP port suffix.
    /// </summary>
    private string? SelectPreferredSerial(List<string> connectedSerials)
    {
        return connectedSerials.FirstOrDefault(serial =>
            string.Equals(serial, _gameLoop.Adb.PreferredSerial, StringComparison.OrdinalIgnoreCase))
            ?? connectedSerials.FirstOrDefault(serial =>
                serial.EndsWith(_gameLoop.Adb.TcpPortSuffix, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> FindInstalledPackages(IEnumerable<string> packageNames, CancellationToken cancellationToken)
    {
        var installed = new List<string>();
        foreach (var packageName in packageNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!AppConstants.Validation.IsValidAndroidPackageName(packageName))
            {
                throw new ArgumentException($"Invalid Android package name: '{packageName}'.", nameof(packageNames));
            }

            var result = Run("-s", DeviceSerial, "shell", "pm", "list", "packages", packageName);
            if (result.Succeeded && result.StandardOutput.Contains(packageName, StringComparison.OrdinalIgnoreCase))
            {
                installed.Add(packageName);
            }
        }

        return installed;
    }

    public void StopAdb() => KillAdb(_runner, _gameLoop);

    public static void KillAdb(IProcessRunner runner, GameLoopOptions? gameLoop = null)
    {
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        gameLoop ??= new GameLoopOptions();
        try
        {
            runner.Run(
                AppConstants.Tools.TaskkillFileName,
                new[] { "/F", "/IM", gameLoop.Adb.FileName },
                TimeSpan.FromMilliseconds(gameLoop.Timeouts.AdbKillWaitMilliseconds));
        }
        catch
        {
            // Ignore errors if ADB was not running.
        }
    }

    /// <summary>
    /// Probes the GameLoop installation for adb: bundled assets, then the
    /// resolver's UI / AppMarket / root lookups. Intentional last resort is the
    /// bare <c>adb.exe</c> PATH fallback — <see cref="Run"/> turns a missing
    /// executable into an explicit "ADB executable could not be started"
    /// failure instead of silently using a wrong drive.
    /// </summary>
    internal string FindAdbPath()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, _emulator.Assets.DirectoryName, _gameLoop.Adb.FileName)
        };

        // The resolver already walks registry → running processes → ProgramFiles,
        // so these three lookups cover every source the old candidate list probed.
        foreach (var baseDir in new[] { _paths.GetUiPath(), _paths.GetAppMarketPath(), _paths.GetRoot() })
        {
            if (string.IsNullOrWhiteSpace(baseDir)) continue;
            candidates.Add(Path.Combine(baseDir, _gameLoop.Adb.FileName));
            candidates.Add(Path.Combine(baseDir, "adb", _gameLoop.Adb.FileName));
        }

        return candidates.FirstOrDefault(File.Exists) ?? _gameLoop.Adb.FileName;
    }
}
