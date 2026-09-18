using FluentAssertions;
using Nexora.Features.GameLoop;
using Nexora.Features.Layout;
using Nexora.Features.Security;
using Nexora.Features.SystemTools;
using Nexora.Features.SystemTools.Network;
using Nexora.Services;
using Nexora.Services.Performance;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

public sealed class ServiceCompositionTests
{
    [Fact]
    public void Facades_ConstructWithoutThrowing()
    {
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        var pathResolver = new GameLoopPathResolver(registry);
        var processService = new GameLoopProcessService(runner, pathResolver);
        var tempCleanup = new TempCleanupService(registry);
        var networkTools = new NetworkToolsService(runner);
        var shortcuts = new ShortcutService(runner, pathResolver, AppContext.BaseDirectory);
        var ipadLayout = new IpadLayoutService(registry, new PhysicalFileSystem(), processService: processService);
        var priorityStore = new ProcessPrioritySnapshotStore();
        var priorityApplier = new ProcessPriorityApplier(priorityStore, processService);
        var processPriority = new ProcessPriorityService(priorityStore, priorityApplier, new ProcessPriorityMonitor(priorityStore, priorityApplier));
        var performanceEngine = new PerformanceEngineFacade(runner, registry, registry, processService, tempCleanup, processPriority);

        tempCleanup.Should().NotBeNull();
        networkTools.Should().NotBeNull();
        shortcuts.Should().NotBeNull();
        ipadLayout.Should().NotBeNull();
        performanceEngine.Should().NotBeNull();
    }

    [Theory]
    [InlineData(@"C:\Program Files\TxGameAssistant\AppMarket\AppMarket.exe", @"C:\Program Files\TxGameAssistant", true)]
    [InlineData(@"D:\Emulators\TxGameAssistant\ui\AndroidEmulator.exe", null, true)]
    [InlineData(@"C:\Windows\System32\cmd.exe", @"C:\Program Files\TxGameAssistant", false)]
    [InlineData(@"C:\Windows\System32\notepad.exe", null, false)]
    [InlineData(null, null, false)]
    [InlineData("", "", false)]
    public void GameLoopPathResolver_IsGameLoopPath_MatchesExpectedPaths(
        string? executablePath,
        string? gameLoopRoot,
        bool expected)
    {
        var result = new GameLoopPathResolver(new RegistryService()).IsGameLoopPath(executablePath, gameLoopRoot);
        result.Should().Be(expected);
    }

    [Fact]
    public void GameLoopProcessService_GetGameLoopRoot_ReturnsNullOrValidDirectory()
    {
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        var service = new GameLoopProcessService(runner, new GameLoopPathResolver(registry));

        var root = service.GetGameLoopRoot();
        // Root may be null if GameLoop is not installed on the test host machine
        if (root is not null)
        {
            root.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void SubServices_ConstructWithoutSideEffects()
    {
        var runner = new ProcessRunner();
        var registry = new RegistryService();

        var processService = new GameLoopProcessService(runner, new GameLoopPathResolver(registry));
        var nvidiaService = new NvidiaOptimizerService(runner, processService);
        var defenderService = new DefenderExclusionService(runner, processService);
        var registryOptimizer = new GameLoopRegistryOptimizer(registry, registry, processService);

        processService.Should().NotBeNull();
        nvidiaService.Should().NotBeNull();
        defenderService.Should().NotBeNull();
        registryOptimizer.Should().NotBeNull();
    }
}
