using FluentAssertions;
using Nexora.Services;
using Nexora.Services.Performance;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

public sealed class WindowsToolsServiceTests
{
    [Fact]
    public void WindowsToolsService_ConstructsWithoutThrowing()
    {
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        var facade = new WindowsToolsService(runner, registry);

        facade.Should().NotBeNull();
    }

    [Theory]
    [InlineData(@"C:\Program Files\TxGameAssistant\AppMarket\AppMarket.exe", @"C:\Program Files\TxGameAssistant", true)]
    [InlineData(@"D:\Emulators\TxGameAssistant\ui\AndroidEmulator.exe", null, true)]
    [InlineData(@"C:\Windows\System32\cmd.exe", @"C:\Program Files\TxGameAssistant", false)]
    [InlineData(@"C:\Windows\System32\notepad.exe", null, false)]
    [InlineData(null, null, false)]
    [InlineData("", "", false)]
    public void GameLoopProcessService_IsGameLoopPath_MatchesExpectedPaths(
        string? executablePath,
        string? gameLoopRoot,
        bool expected)
    {
        var result = GameLoopProcessService.IsGameLoopPath(executablePath, gameLoopRoot);
        result.Should().Be(expected);
    }

    [Fact]
    public void GameLoopProcessService_GetGameLoopRoot_ReturnsNullOrValidDirectory()
    {
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        var service = new GameLoopProcessService(runner, registry);

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

        var processService = new GameLoopProcessService(runner, registry);
        var nvidiaService = new NvidiaOptimizerService(runner, registry);
        var defenderService = new DefenderExclusionService(runner, registry);
        var registryOptimizer = new GameLoopRegistryOptimizer(runner, registry);

        processService.Should().NotBeNull();
        nvidiaService.Should().NotBeNull();
        defenderService.Should().NotBeNull();
        registryOptimizer.Should().NotBeNull();
    }
}
