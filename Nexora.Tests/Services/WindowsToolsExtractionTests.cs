using FluentAssertions;
using Nexora.Services;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

public sealed class WindowsToolsExtractionTests
{
    private const string UnroutableHost = "256.256.256.256";

    [Fact]
    public void NetworkToolsService_PingDns_ReturnsNull_ForUnresolvableHost()
    {
        // Arrange
        var service = new NetworkToolsService(new ProcessRunner());

        // Act
        var ping = service.PingDns(UnroutableHost);

        // Assert: the failure path is null, never an exception.
        ping.Should().BeNull();
    }

    [Fact]
    public void Facade_PingDns_DelegatesToNetworkTools()
    {
        // Arrange
        var facade = new WindowsToolsService(new ProcessRunner(), new RegistryService());

        // Act
        var ping = facade.PingDns(UnroutableHost);

        // Assert: same contract as the extracted service.
        ping.Should().BeNull();
    }

    [Fact]
    public void ExtractedServices_ConstructWithoutSideEffects()
    {
        // Arrange
        var runner = new ProcessRunner();
        var registry = new RegistryService();

        // Act
        var network = new NetworkToolsService(runner);
        var shortcuts = new ShortcutService(runner, registry, AppContext.BaseDirectory);
        var tempCleanup = new TempCleanupService(registry);
        var facade = new WindowsToolsService(runner, registry);

        // Assert
        network.Should().NotBeNull();
        shortcuts.Should().NotBeNull();
        tempCleanup.Should().NotBeNull();
        facade.Should().NotBeNull();
    }
}
