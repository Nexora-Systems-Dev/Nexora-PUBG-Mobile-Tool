using FluentAssertions;
using Nexora.Configuration;
using Nexora.Features.SystemTools;
using Nexora.Features.SystemTools.Network;
using Nexora.Services;
using Nexora.Services.Performance;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

public sealed class AsyncHygieneTests
{
    [Fact]
    public async Task CleanTempAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var registry = new RegistryService();
        var service = new TempCleanupService(registry);

        var act = () => service.CleanTempAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CleanTempAsync_WithCustomOptions_SweepsDirectoryAsynchronously()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "NexoraAsyncCleanTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var testFile = Path.Combine(tempRoot, "async_scratch.tmp");
            File.WriteAllText(testFile, "temporary data");

            var options = new TempCleanupOptions
            {
                TargetDirectories = new[] { tempRoot },
                ShaderCacheFolderName = "NonExistent"
            };

            var registry = new RegistryService();
            var service = new TempCleanupService(registry, options);

            var result = await service.CleanTempAsync();

            result.Success.Should().BeTrue();
            File.Exists(testFile).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public async Task GetSnapshotAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var runner = new ProcessRunner();
        var service = new HardwareDetectionService(runner);

        var act = () => service.GetSnapshotAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetSnapshotAsync_CompletesAndReturnsValidHardwareSnapshot()
    {
        var runner = new ProcessRunner();
        var service = new HardwareDetectionService(runner);

        var snapshot = await service.GetSnapshotAsync();

        snapshot.Should().NotBeNull();
        snapshot.LogicalCores.Should().BeGreaterThan(0);
        snapshot.PhysicalCores.Should().BeGreaterThan(0);
        snapshot.TotalMemoryGb.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PingDnsAsync_WithCancelledToken_ReturnsNullGracefully()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var runner = new ProcessRunner();
        var service = new NetworkToolsService(runner);

        var ping = await service.PingDnsAsync("8.8.8.8", cts.Token);
        ping.Should().BeNull();
    }

    [Fact]
    public async Task PingDnsAsync_WithInvalidHost_ReturnsNull()
    {
        var runner = new ProcessRunner();
        var service = new NetworkToolsService(runner);

        var ping = await service.PingDnsAsync("999.999.999.999");
        ping.Should().BeNull();
    }

    [Fact]
    public async Task PerformanceEngineFacade_AsyncSeams_ExecuteSuccessfully()
    {
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        var pathResolver = new GameLoopPathResolver(registry);
        var processService = new GameLoopProcessService(runner, pathResolver);
        var priorityStore = new ProcessPrioritySnapshotStore();
        var priorityApplier = new ProcessPriorityApplier(priorityStore, processService);
        var facade = new PerformanceEngineFacade(
            runner,
            registry,
            registry,
            processService,
            new TempCleanupService(registry),
            new ProcessPriorityService(priorityStore, priorityApplier, new ProcessPriorityMonitor(priorityStore, priorityApplier)));

        var snapshot = await facade.GetHardwareSnapshotAsync();
        snapshot.Should().NotBeNull();

        var restoreResult = await facade.RestorePerformanceSessionAsync();
        restoreResult.Should().NotBeNull();
    }
}
