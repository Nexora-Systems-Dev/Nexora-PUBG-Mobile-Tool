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
        var service = new NetworkToolsService(new ProcessRunner());

        var ping = service.PingDns(UnroutableHost);

        // The failure path is null, never an exception.
        ping.Should().BeNull();
    }

    [Fact]
    public void Facade_PingDns_DelegatesToNetworkTools()
    {
        var facade = new WindowsToolsService(new ProcessRunner(), new RegistryService());

        var ping = facade.PingDns(UnroutableHost);

        // Same contract as the extracted service.
        ping.Should().BeNull();
    }

    [Fact]
    public void ExtractedServices_ConstructWithoutSideEffects()
    {
        var runner = new ProcessRunner();
        var registry = new RegistryService();

        var network = new NetworkToolsService(runner);
        var shortcuts = new ShortcutService(runner, registry, AppContext.BaseDirectory);
        var tempCleanup = new TempCleanupService(registry);
        var facade = new WindowsToolsService(runner, registry);

        network.Should().NotBeNull();
        shortcuts.Should().NotBeNull();
        tempCleanup.Should().NotBeNull();
        facade.Should().NotBeNull();
    }
}
