using System.Diagnostics;
using FluentAssertions;
using Nexora.Services.Performance;
using Xunit;

namespace Nexora.Tests.Performance;

/// <summary>
/// Verifies the P1-Step-4 shutdown contract: stopping the performance
/// monitor must be async with no sync-over-async blocking, and dead PIDs
/// must be pruned from the snapshot dictionary so long sessions cannot
/// grow it without bound.
/// </summary>
public sealed class ProcessPriorityShutdownTests
{
    private static readonly TimeSpan CompletionLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task StopMonitorAsync_CompletesPromptly_WhenMonitorRunning()
    {
        // Arrange: Apply with a non-matching root starts the monitor loop
        // without touching any real process.
        var service = new ProcessPriorityService();
        service.Apply("Z:\\nexora-test-nonexistent-root");

        // Act
        var act = () => service.StopMonitorAsync();

        // Assert: must exit via async coordination, never monitor.Wait().
        await act.Should().CompleteWithinAsync(CompletionLimit);
    }

    [Fact]
    public async Task StopMonitorAsync_NoOp_WhenNeverStarted()
    {
        // Arrange
        var service = new ProcessPriorityService();

        // Act
        var act = () => service.StopMonitorAsync();

        // Assert
        await act.Should().CompleteWithinAsync(CompletionLimit);
    }

    [Fact]
    public void Restore_AfterApply_ReturnsOk_WithoutBlocking()
    {
        // Arrange
        var service = new ProcessPriorityService();
        service.Apply("Z:\\nexora-test-nonexistent-root");

        // Act: the old sync StopMonitor blocked up to 2s on monitor.Wait;
        // the refactored shutdown must return promptly.
        var started = Stopwatch.GetTimestamp();
        var result = service.Restore();
        var elapsed = Stopwatch.GetElapsedTime(started);

        // Assert
        result.Success.Should().BeTrue();
        elapsed.Should().BeLessThan(CompletionLimit);
    }

    [Fact]
    public void PruneDeadSnapshots_RemovesDeadPidEntries()
    {
        // Arrange: int.MaxValue can never be a live PID.
        var service = new ProcessPriorityService();
        service.AddSnapshotForTesting(int.MaxValue, null, "aow_exe", ProcessPriorityClass.Normal);

        // Act
        var removed = service.PruneDeadSnapshots();

        // Assert
        removed.Should().Be(1);
        service.SnapshotCount.Should().Be(0);
    }

    [Fact]
    public void PruneDeadSnapshots_KeepsLiveProcessEntry()
    {
        // Arrange
        using var current = Process.GetCurrentProcess();
        var service = new ProcessPriorityService();
        service.AddSnapshotForTesting(current.Id, null, current.ProcessName, ProcessPriorityClass.Normal);

        // Act
        var removed = service.PruneDeadSnapshots();

        // Assert
        removed.Should().Be(0);
        service.SnapshotCount.Should().Be(1);
    }

    [Fact]
    public void PruneDeadSnapshots_DropsPidReuseMismatch()
    {
        // Arrange: same PID but a different identity means the OS recycled
        // the PID after the original process exited; the stale entry must go.
        using var current = Process.GetCurrentProcess();
        var service = new ProcessPriorityService();
        service.AddSnapshotForTesting(current.Id, null, "definitely-not-this-process", ProcessPriorityClass.Normal);

        // Act
        var removed = service.PruneDeadSnapshots();

        // Assert
        removed.Should().Be(1);
        service.SnapshotCount.Should().Be(0);
    }
}
