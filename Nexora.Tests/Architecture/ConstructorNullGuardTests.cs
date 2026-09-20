using FluentAssertions;
using Nexora.Features.Network.Application;
using Nexora.Features.Network.Domain;
using Nexora.Features.Optimizer.Application;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;
using Nexora.Infrastructure.GameLoop;
using Nexora.Infrastructure.Files;

namespace Nexora.Tests.Architecture;

/// <summary>
/// Pins the constructor null-guard convention
/// (<c>?? throw new ArgumentNullException(...)</c>) for services whose
/// ctors were missing it.
/// </summary>
public sealed class ConstructorNullGuardTests
{
    [Fact]
    public void TempCleanupService_Throws_WhenRegistryNull()
    {
        var act = () => new TempCleanupService(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("registry");
    }

    [Fact]
    public void HardwareDetectionService_Throws_WhenRunnerNull()
    {
        var act = () => new HardwareDetectionService(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("runner");
    }

    [Fact]
    public void NetworkToolsService_Throws_WhenRunnerNull()
    {
        var act = () => new NetworkToolsService(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("runner");
    }

    [Fact]
    public void PowerSessionService_Throws_WhenRunnerNull()
    {
        var act = () => new PowerSessionService(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("runner");
    }

    [Fact]
    public void IpadLayoutService_Throws_WhenRegistryNull()
    {
        var act = () => new IpadLayoutService(null!, new PhysicalFileSystem());

        act.Should().Throw<ArgumentNullException>().WithParameterName("registry");
    }

    [Fact]
    public void ProcessPriorityService_Throws_WhenStoreNull()
    {
        var processService = new GameLoopProcessService(new ProcessRunner(), new GameLoopPathResolver(new RegistryService()));
        var applier = new ProcessPriorityApplier(new ProcessPrioritySnapshotStore(), processService);

        var act = () => new ProcessPriorityService(null!, applier, new ProcessPriorityMonitor(new ProcessPrioritySnapshotStore(), applier));

        act.Should().Throw<ArgumentNullException>().WithParameterName("store");
    }

    [Fact]
    public void ProcessPriorityService_Throws_WhenApplierNull()
    {
        var store = new ProcessPrioritySnapshotStore();
        var processService = new GameLoopProcessService(new ProcessRunner(), new GameLoopPathResolver(new RegistryService()));
        var monitor = new ProcessPriorityMonitor(store, new ProcessPriorityApplier(store, processService));

        var act = () => new ProcessPriorityService(store, null!, monitor);

        act.Should().Throw<ArgumentNullException>().WithParameterName("applier");
    }

    [Fact]
    public void ProcessPriorityService_Throws_WhenMonitorNull()
    {
        var store = new ProcessPrioritySnapshotStore();
        var applier = new ProcessPriorityApplier(store, new GameLoopProcessService(new ProcessRunner(), new GameLoopPathResolver(new RegistryService())));

        var act = () => new ProcessPriorityService(store, applier, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("monitor");
    }

    [Fact]
    public void PerformanceEngineFacade_Throws_WhenProcessPriorityNull()
    {
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        var processService = new GameLoopProcessService(runner, new GameLoopPathResolver(registry));

        var act = () => new PerformanceEngineFacade(runner, registry, registry, processService, new TempCleanupService(registry), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("processPriority");
    }

    [Fact]
    public void GameLoopRegistryOptimizer_Throws_WhenUserRegistryNull()
    {
        var registry = new RegistryService();
        var processService = new GameLoopProcessService(new ProcessRunner(), new GameLoopPathResolver(registry));

        var act = () => new GameLoopRegistryOptimizer(null!, registry, processService);

        act.Should().Throw<ArgumentNullException>().WithParameterName("userRegistry");
    }

    [Fact]
    public void GameLoopRegistryOptimizer_Throws_WhenMachineRegistryNull()
    {
        var registry = new RegistryService();
        var processService = new GameLoopProcessService(new ProcessRunner(), new GameLoopPathResolver(registry));

        var act = () => new GameLoopRegistryOptimizer(registry, null!, processService);

        act.Should().Throw<ArgumentNullException>().WithParameterName("machineRegistry");
    }
}
