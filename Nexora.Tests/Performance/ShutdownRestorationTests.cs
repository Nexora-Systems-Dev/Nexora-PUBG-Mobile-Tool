using System.Diagnostics;
using FluentAssertions;
using Nexora.Configuration;
using Nexora.Services;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Performance;

/// <summary>
/// Verifies performance session restoration during application shutdown, ensuring
/// power scheme and process priority restorations complete within bounded timeouts.
/// </summary>
public sealed class ShutdownRestorationTests
{
    [Fact]
    public void ShutdownRestoreTimeout_IsConfiguredToSafeBound()
    {
        // 5 seconds: adequate for powercfg + priority restore, safe against indefinite hangs.
        AppConstants.Timeouts.ShutdownRestoreTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RestorePerformanceSessionAsync_CompletesPromptly()
    {
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        var toolsService = new WindowsToolsService(runner, registry);

        var stopwatch = Stopwatch.StartNew();
        var task = toolsService.RestorePerformanceSessionAsync();

        // Must complete well within the shutdown restore timeout limit
        var result = await task;
        stopwatch.Stop();

        result.Should().NotBeNull();
        stopwatch.Elapsed.Should().BeLessThan(AppConstants.Timeouts.ShutdownRestoreTimeout);
    }

    [Fact]
    public async Task RestorePerformanceSessionAsync_CanBeSynchronouslyAwaited_WithTimeout()
    {
        // Simulates the exact Window_Closing shutdown sequence:
        // task.Wait(AppConstants.Timeouts.ShutdownRestoreTimeout)
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        var toolsService = new WindowsToolsService(runner, registry);

        var completedInTime = await Task.Run(() =>
        {
            var restoreTask = toolsService.RestorePerformanceSessionAsync();
            return restoreTask.Wait(AppConstants.Timeouts.ShutdownRestoreTimeout);
        });

        completedInTime.Should().BeTrue();
    }
}
