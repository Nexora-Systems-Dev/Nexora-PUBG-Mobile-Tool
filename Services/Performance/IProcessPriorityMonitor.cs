namespace Nexora.Services.Performance;

/// <summary>
/// Keeps High priority applied to GameLoop processes across emulator launches
/// via a periodic background monitor loop.
/// </summary>
public interface IProcessPriorityMonitor
{
    void Start(string? gameLoopRoot);

    /// <summary>
    /// Signals the monitor loop to stop without awaiting its completion.
    /// </summary>
    void Stop();

    /// <summary>
    /// Signals cancellation and awaits monitor loop termination up to the configured stop timeout.
    /// </summary>
    Task StopMonitorAsync(CancellationToken cancellationToken = default);
}
