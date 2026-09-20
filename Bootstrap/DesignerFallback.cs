using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Features.Updates.Application;
using Nexora.Features.Updates.Infrastructure;
using Nexora.Features.Optimizer.Application;
using Nexora.Infrastructure.Files;
using Nexora.Infrastructure.GameLoop;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;

namespace Nexora.Bootstrap;

/// <summary>
/// The designer-path service graph: the fallback composition the shell (and,
/// by the same shape, the page designer ctors) builds when the DI container
/// did not provide the services — the XAML designer and direct construction.
/// </summary>
internal sealed record DesignerServiceGraph(
    IGameLoopPerformanceEngine PerformanceEngine,
    IUpdateService Updates);

/// <summary>
/// Builds the <see cref="DesignerServiceGraph"/>. Mirrors the container's
/// singletons: one runner, one registry, one process service, one temp
/// cleanup, one shared priority store behind the engine — so the designer
/// path never forks identities the container keeps single.
/// </summary>
internal static class DesignerFallback
{
    public static DesignerServiceGraph Create()
    {
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        // Single process/temp identity in the designer path: one process
        // service and one temp cleanup shared by every fallback below,
        // mirroring the DI singletons used when the container provides them.
        var pathResolver = new GameLoopPathResolver(registry);
        var processSvc = new GameLoopProcessService(runner, pathResolver);
        var tempSvc = new TempCleanupService(registry);
        // Same shared-store composition the DI container builds for the trio.
        var priorityStore = new ProcessPrioritySnapshotStore();
        var priorityApplier = new ProcessPriorityApplier(priorityStore, processSvc);
        var processPriority = new ProcessPriorityService(priorityStore, priorityApplier, new ProcessPriorityMonitor(priorityStore, priorityApplier));

        return new DesignerServiceGraph(
            new PerformanceEngineFacade(runner, registry, registry, processSvc, tempSvc, processPriority),
            new UpdateService(runner));
    }
}
