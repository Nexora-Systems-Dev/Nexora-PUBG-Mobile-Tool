using Nexora.Shared.Kernel;

namespace Nexora.Services;

/// <summary>
/// Contract for low-level Android Debug Bridge (ADB) operations.
/// </summary>
public interface IAdbClient
{
    string DeviceSerial { get; }

    ProcessResult Run(params string[] arguments);

    string Shell(string command);

    Task<bool> PullAsync(string remotePath, string localPath, CancellationToken cancellationToken);

    Task<bool> PushAsync(string localPath, string remotePath, CancellationToken cancellationToken);

    Task<bool> WaitForBootAsync(CancellationToken cancellationToken);

    IReadOnlyList<string> FindInstalledPackages(IEnumerable<string> packageNames, CancellationToken cancellationToken);

    void StopAdb();
}
