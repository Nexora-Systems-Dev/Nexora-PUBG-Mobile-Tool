using Nexora.Configuration;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

public sealed class AdbClient : IAdbClient
{
    private readonly IProcessRunner _runner;
    private readonly string _adbPath;
    private string? _deviceSerial;

    public AdbClient(IProcessRunner runner, IRegistryService registry)
    {
        _runner = runner;
        _adbPath = FindAdbPath(registry);
    }

    public string DeviceSerial => _deviceSerial ?? AppConstants.Adb.PreferredSerial;

    public ProcessResult Run(params string[] arguments)
    {
        return _runner.Run(_adbPath, arguments, AppConstants.Timeouts.AdbCommandTimeout);
    }

    public string Shell(string command)
    {
        return Run("-s", DeviceSerial, "shell", command).StandardOutput.Trim();
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
        for (var attempt = 1; attempt <= AppConstants.Timeouts.AdbTransferMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = Run("-s", DeviceSerial, operation, source, destination);
            var transferred = result.Succeeded &&
                (operation.Equals("push", StringComparison.OrdinalIgnoreCase) || File.Exists(destination));
            if (transferred)
            {
                return true;
            }

            if (attempt == AppConstants.Timeouts.AdbTransferMaxAttempts)
            {
                return false;
            }

            // Refresh device serial before retrying in case the bridge restarted.
            await Task.Delay(AppConstants.Timeouts.AdbTransferRetryDelay, cancellationToken);
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

        for (var attempt = 0; attempt < AppConstants.Timeouts.AdbBootPollAttempts; attempt++)
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

            await Task.Delay(AppConstants.Timeouts.AdbBootPollDelay, cancellationToken);
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

        var connectedSerials = devices.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2 && string.Equals(parts[1], "device", StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[0])
            .ToList();

        _deviceSerial = connectedSerials.FirstOrDefault(serial =>
            string.Equals(serial, AppConstants.Adb.PreferredSerial, StringComparison.OrdinalIgnoreCase))
            ?? connectedSerials.FirstOrDefault(serial =>
                serial.EndsWith(AppConstants.Adb.TcpPortSuffix, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(_deviceSerial))
        {
            return true;
        }

        // Connect to local loopback port if GameLoop exposes bridge on TCP 5555.
        cancellationToken.ThrowIfCancellationRequested();
        Run("connect", AppConstants.Adb.LoopbackEndpoint);
        cancellationToken.ThrowIfCancellationRequested();
        devices = Run("devices");
        connectedSerials = devices.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2 && string.Equals(parts[1], "device", StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[0])
            .ToList();

        _deviceSerial = connectedSerials.FirstOrDefault(serial =>
            string.Equals(serial, AppConstants.Adb.PreferredSerial, StringComparison.OrdinalIgnoreCase))
            ?? connectedSerials.FirstOrDefault(serial =>
                serial.EndsWith(AppConstants.Adb.TcpPortSuffix, StringComparison.OrdinalIgnoreCase));

        return !string.IsNullOrWhiteSpace(_deviceSerial);
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

    public void StopAdb() => KillAdb(_runner);

    public static void KillAdb(IProcessRunner? runner = null)
    {
        try
        {
            var processRunner = runner ?? new ProcessRunner();
            processRunner.Run(
                AppConstants.Tools.TaskkillFileName,
                new[] { "/F", "/IM", AppConstants.Adb.FileName },
                TimeSpan.FromMilliseconds(AppConstants.Timeouts.AdbKillWaitMilliseconds));
        }
        catch
        {
            // Ignore errors if ADB was not running.
        }
    }

    private static string FindAdbPath(IRegistryService registry)
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, AppConstants.Assets.DirectoryName, AppConstants.Adb.FileName)
        };

        foreach (var branch in new[] { AppConstants.Registry.BranchUI, AppConstants.Registry.BranchAppMarket })
        {
            var installPath = registry.GetLocalString(AppConstants.Registry.ValueInstallPath, branch);
            if (!string.IsNullOrWhiteSpace(installPath))
            {
                candidates.Add(Path.Combine(installPath, AppConstants.Adb.FileName));
                candidates.Add(Path.Combine(installPath, "adb", AppConstants.Adb.FileName));
            }
        }

        // Fall back to active emulator process paths if registry lookup did not locate ADB.
        foreach (var name in AppConstants.Emulator.RunningCheckProcessNames)
        {
            try
            {
                var procs = System.Diagnostics.Process.GetProcessesByName(name);
                foreach (var p in procs)
                {
                    try
                    {
                        var modPath = p.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(modPath))
                        {
                            var dir = Path.GetDirectoryName(modPath);
                            if (!string.IsNullOrWhiteSpace(dir))
                            {
                                candidates.Add(Path.Combine(dir, AppConstants.Adb.FileName));
                                candidates.Add(Path.Combine(dir, "adb", AppConstants.Adb.FileName));
                            }
                        }
                    }
                    catch
                    {
                        // Ignore processes where module path access is denied.
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch
            {
                // Ignore process enumeration errors.
            }
        }

        // Check standard Program Files installation paths.
        foreach (var programFiles in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(programFiles, AppConstants.Emulator.InstallFolderName, "ui", AppConstants.Adb.FileName));
            candidates.Add(Path.Combine(programFiles, AppConstants.Emulator.InstallFolderName, "UI", AppConstants.Adb.FileName));
        }

        var existing = candidates.FirstOrDefault(File.Exists);
        return existing ?? AppConstants.Adb.FileName;
    }
}
