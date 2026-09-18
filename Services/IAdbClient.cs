using Nexora.Shared.Kernel;

namespace Nexora.Services;

/// <summary>
/// Contract for low-level Android Debug Bridge (ADB) operations.
/// </summary>
public interface IAdbClient
{
    string DeviceSerial { get; }

    /// <summary>
    /// Re-probes the GameLoop installation for adb immediately, so a
    /// repair/reinstall is picked up without waiting for cache expiry.
    /// </summary>
    void RefreshAdbPath();

    ProcessResult Run(params string[] arguments);

    string Shell(string command);

    /// <summary>
    /// Cancellation-aware shell: throws <see cref="OperationCanceledException"/>
    /// before starting adb when <paramref name="cancellationToken"/> is cancelled.
    /// In-flight adb remains bounded by the configured AdbCommandTimeout.
    /// </summary>
    string Shell(string command, CancellationToken cancellationToken);

    Task<bool> PullAsync(string remotePath, string localPath, CancellationToken cancellationToken);

    Task<bool> PushAsync(string localPath, string remotePath, CancellationToken cancellationToken);

    Task<bool> WaitForBootAsync(CancellationToken cancellationToken);

    IReadOnlyList<string> FindInstalledPackages(IEnumerable<string> packageNames, CancellationToken cancellationToken);

    void StopAdb();
}
