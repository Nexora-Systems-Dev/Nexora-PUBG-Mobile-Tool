namespace Nexora.Features.GameLoop.Application;
using Nexora.Shared.Kernel;
using Nexora.Features.GameLoop.Domain;
using Nexora.Infrastructure.Processes;

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

    Task<bool> PullAsync(string remotePath, string localPath, CancellationToken cancellationToken, IProgress<string>? progress = null);

    Task<bool> PushAsync(string localPath, string remotePath, CancellationToken cancellationToken, IProgress<string>? progress = null);

    Task<bool> WaitForBootAsync(CancellationToken cancellationToken, IProgress<string>? progress = null);

    IReadOnlyList<string> FindInstalledPackages(IEnumerable<string> packageNames, CancellationToken cancellationToken);

    /// <summary>
    /// True-async twin of <see cref="FindInstalledPackages"/> for UI-context
    /// callers: each package probe awaits the offloaded adb wait instead of
    /// blocking the calling thread. Validation still throws synchronously.
    /// </summary>
    Task<IReadOnlyList<string>> FindInstalledPackagesAsync(IEnumerable<string> packageNames, CancellationToken cancellationToken, IProgress<string>? progress = null);

    void StopAdb();

    /// <summary>
    /// True-async twin of <see cref="StopAdb"/> for UI-context callers: the
    /// taskkill wait runs off-thread, bounded by the configured kill timeout.
    /// Best-effort like the sync twin — never throws, including on cancel.
    /// </summary>
    Task StopAdbAsync(CancellationToken cancellationToken);
}
