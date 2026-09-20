using System.Diagnostics;
using FluentAssertions;
using Nexora.Configuration;
using Nexora.Features.Optimizer.Application;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;
using Nexora.Infrastructure.GameLoop;

namespace Nexora.Tests.Features.Performance;

/// <summary>
/// Verifies performance session restoration during application shutdown, ensuring
/// power scheme and process priority restorations complete within bounded timeouts.
/// </summary>
public sealed class ShutdownRestorationTests
{
    private static PerformanceEngineFacade CreateEngine()
    {
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        var pathResolver = new GameLoopPathResolver(registry);
        var processService = new GameLoopProcessService(runner, pathResolver);
        var priorityStore = new ProcessPrioritySnapshotStore();
        var priorityApplier = new ProcessPriorityApplier(priorityStore, processService);
        return new PerformanceEngineFacade(
            runner,
            registry,
            registry,
            processService,
            new TempCleanupService(registry),
            new ProcessPriorityService(priorityStore, priorityApplier, new ProcessPriorityMonitor(priorityStore, priorityApplier)));
    }

    [Fact]
    public void ShutdownRestoreTimeout_IsConfiguredToSafeBound()
    {
        // 5 seconds: adequate for powercfg + priority restore, safe against indefinite hangs.
        new GameLoopOptions().Timeouts.ShutdownRestoreTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RestorePerformanceSessionAsync_CompletesPromptly()
    {
        var toolsService = CreateEngine();

        var stopwatch = Stopwatch.StartNew();
        var task = toolsService.RestorePerformanceSessionAsync();

        // Must complete well within the shutdown restore timeout limit
        var result = await task;
        stopwatch.Stop();

        result.Should().NotBeNull();
        stopwatch.Elapsed.Should().BeLessThan(new GameLoopOptions().Timeouts.ShutdownRestoreTimeout);
    }

    [Fact]
    public async Task RestorePerformanceSessionAsync_CompletesWithinBoundedShutdownToken()
    {
        // Mirrors the Window_Closing shutdown sequence: restore runs under a
        // bounded CancellationTokenSource(ShutdownRestoreTimeout), never .Wait().
        var toolsService = CreateEngine();

        using var shutdownCts = new CancellationTokenSource(new GameLoopOptions().Timeouts.ShutdownRestoreTimeout);
        var result = await toolsService.RestorePerformanceSessionAsync(shutdownCts.Token);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task RestorePerformanceSessionAsync_ThrowsImmediately_WhenCancelledBeforeStart()
    {
        var toolsService = CreateEngine();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => toolsService.RestorePerformanceSessionAsync(cts.Token));
    }
}
