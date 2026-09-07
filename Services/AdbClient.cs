using System.Text;

namespace Nexora.Services;

public sealed class AdbClient
{
    private const string PreferredDeviceSerial = "emulator-5554";
    private readonly ProcessRunner _runner;
    private readonly string _adbPath;
    private string? _deviceSerial;

    public AdbClient(ProcessRunner runner, RegistryService registry)
    {
        _runner = runner;
        _adbPath = FindAdbPath(registry);
    }

    public string AdbPath => _adbPath;
    public string DeviceSerial => _deviceSerial ?? PreferredDeviceSerial;

    public ProcessResult Run(params string[] arguments)
    {
        return _runner.Run(_adbPath, arguments, TimeSpan.FromSeconds(20));
    }

    public ProcessResult RunLong(TimeSpan timeout, params string[] arguments)
    {
        return _runner.Run(_adbPath, arguments, timeout);
    }

    public string Shell(string command)
    {
        return Run("-s", DeviceSerial, "shell", command).StandardOutput.Trim();
    }

    public ProcessResult ShellResult(string command)
    {
        return Run("-s", DeviceSerial, "shell", command);
    }

    public bool Pull(string remotePath, string localPath)
    {
        return TransferWithRetry("pull", remotePath, localPath);
    }

    public bool Push(string localPath, string remotePath)
    {
        return TransferWithRetry("push", localPath, remotePath);
    }

    private bool TransferWithRetry(string operation, string source, string destination)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var result = Run("-s", DeviceSerial, operation, source, destination);
            var transferred = result.Succeeded &&
                (operation.Equals("push", StringComparison.OrdinalIgnoreCase) || File.Exists(destination));
            if (transferred)
            {
                return true;
            }

            if (attempt == 3)
            {
                return false;
            }

            // GameLoop can briefly restart its Android bridge while PUBG is
            // force-stopped/relaunched. Re-select the live serial before the
            // next transfer instead of treating that short race as a hard
            // graphics-apply failure.
            Thread.Sleep(1000);
            TrySelectDevice();
        }

        return false;
    }

    public bool WaitForBoot(CancellationToken cancellationToken)
    {
        if (!TrySelectDevice())
        {
            return false;
        }

        for (var attempt = 0; attempt < 60; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = Run("-s", DeviceSerial, "shell", "getprop", "dev.bootcomplete");
            if (result.Succeeded && result.StandardOutput.Trim() == "1")
            {
                return true;
            }

            Thread.Sleep(1000);
        }

        return false;
    }

    private bool TrySelectDevice()
    {
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
            string.Equals(serial, PreferredDeviceSerial, StringComparison.OrdinalIgnoreCase))
            ?? connectedSerials.FirstOrDefault(serial =>
                serial.EndsWith(":5555", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(_deviceSerial))
        {
            return true;
        }

        // GameLoop exposes its Android bridge on TCP 5555 on some installations.
        // Establish the local connection only when the emulator is already running.
        Run("connect", "127.0.0.1:5555");
        devices = Run("devices");
        connectedSerials = devices.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2 && string.Equals(parts[1], "device", StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[0])
            .ToList();

        _deviceSerial = connectedSerials.FirstOrDefault(serial =>
            string.Equals(serial, PreferredDeviceSerial, StringComparison.OrdinalIgnoreCase))
            ?? connectedSerials.FirstOrDefault(serial =>
                serial.EndsWith(":5555", StringComparison.OrdinalIgnoreCase));

        return !string.IsNullOrWhiteSpace(_deviceSerial);
    }

    public IReadOnlyList<string> FindInstalledPackages(IEnumerable<string> packageNames)
    {
        var installed = new List<string>();
        foreach (var packageName in packageNames)
        {
            var result = Run("-s", DeviceSerial, "shell", "pm", "list", "packages", packageName);
            if (result.Succeeded && result.StandardOutput.Contains(packageName, StringComparison.OrdinalIgnoreCase))
            {
                installed.Add(packageName);
            }
        }

        return installed;
    }

    public static void KillAdb()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "/F", "/IM", "adb.exe" }
            })?.WaitForExit(5000);
        }
        catch
        {
            // A missing adb process is the same state as a successfully stopped one.
        }
    }

    private static string FindAdbPath(RegistryService registry)
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "adb.exe")
        };

        foreach (var branch in new[] { "UI", "AppMarket" })
        {
            var installPath = registry.GetLocalString("InstallPath", branch);
            if (!string.IsNullOrWhiteSpace(installPath))
            {
                candidates.Add(Path.Combine(installPath, "adb.exe"));
                candidates.Add(Path.Combine(installPath, "adb", "adb.exe"));
            }
        }

        // Some GameLoop installations do not expose InstallPath in the
        // registry branch used by the original tool. Keep the same local
        // discovery behavior by checking the standard UI locations too.
        foreach (var programFiles in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(programFiles, "TxGameAssistant", "ui", "adb.exe"));
            candidates.Add(Path.Combine(programFiles, "TxGameAssistant", "UI", "adb.exe"));
        }

        var existing = candidates.FirstOrDefault(File.Exists);
        return existing ?? "adb.exe";
    }
}
