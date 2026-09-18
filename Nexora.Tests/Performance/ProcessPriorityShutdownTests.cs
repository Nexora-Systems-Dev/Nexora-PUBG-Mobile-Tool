using System.Diagnostics;
using FluentAssertions;
using Nexora.Services.Performance;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Performance;

/// <summary>
/// Verifies asynchronous shutdown coordination and dead PID snapshot pruning for the process priority monitor.
/// </summary>
public sealed class ProcessPriorityShutdownTests
{
    private static readonly TimeSpan CompletionLimit = TimeSpan.FromSeconds(10);

    private static ProcessPriorityService CreateService()
    {
        var store = new ProcessPrioritySnapshotStore();
        var processService = new GameLoopProcessService(new ProcessRunner(), new GameLoopPathResolver(new RegistryService()));
        var applier = new ProcessPriorityApplier(store, processService);
        return new ProcessPriorityService(store, applier, new ProcessPriorityMonitor(store, applier));
    }

    [Fact]
    public async Task StopMonitorAsync_CompletesPromptly_WhenMonitorRunning()
    {
        // Apply with a non-matching root starts the monitor loop
        // without touching any real process.
        var service = CreateService();
        service.Apply("Z:\\nexora-test-nonexistent-root");

        var act = () => service.StopMonitorAsync();

        // Must exit via async coordination, never monitor.Wait().
        await act.Should().CompleteWithinAsync(CompletionLimit);
    }

    [Fact]
    public async Task StopMonitorAsync_NoOp_WhenNeverStarted()
    {
        var service = CreateService();

        var act = () => service.StopMonitorAsync();

        await act.Should().CompleteWithinAsync(CompletionLimit);
    }

    [Fact]
    public void Restore_AfterApply_ReturnsOk_WithoutBlocking()
    {
        var service = CreateService();
        service.Apply("Z:\\nexora-test-nonexistent-root");

        // Ensure StopMonitor returns promptly without blocking.
        var started = Stopwatch.GetTimestamp();
        var result = service.Restore();
        var elapsed = Stopwatch.GetElapsedTime(started);

        result.Success.Should().BeTrue();
        elapsed.Should().BeLessThan(CompletionLimit);
    }

    [Fact]
    public void PruneDeadSnapshots_RemovesDeadPidEntries()
    {
        // Int.MaxValue can never be a live PID.
        var service = CreateService();
        service.AddSnapshotForTesting(int.MaxValue, null, "aow_exe", ProcessPriorityClass.Normal);

        var removed = service.PruneDeadSnapshots();

        removed.Should().Be(1);
        service.SnapshotCount.Should().Be(0);
    }

    [Fact]
    public void PruneDeadSnapshots_KeepsLiveProcessEntry()
    {
        using var current = Process.GetCurrentProcess();
        var service = CreateService();
        service.AddSnapshotForTesting(current.Id, null, current.ProcessName, ProcessPriorityClass.Normal);

        var removed = service.PruneDeadSnapshots();

        removed.Should().Be(0);
        service.SnapshotCount.Should().Be(1);
    }

    [Fact]
    public void PruneDeadSnapshots_DropsPidReuseMismatch()
    {
        // Same PID but a different identity means the OS recycled
        // the PID after the original process exited; the stale entry must go.
        using var current = Process.GetCurrentProcess();
        var service = CreateService();
        service.AddSnapshotForTesting(current.Id, null, "definitely-not-this-process", ProcessPriorityClass.Normal);

        var removed = service.PruneDeadSnapshots();

        removed.Should().Be(1);
        service.SnapshotCount.Should().Be(0);
    }
}
