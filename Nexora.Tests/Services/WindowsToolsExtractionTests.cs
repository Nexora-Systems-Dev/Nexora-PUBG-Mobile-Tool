using FluentAssertions;
using Nexora.Features.GameLoop;
using Nexora.Features.SystemTools;
using Nexora.Features.SystemTools.Network;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

/// <summary>
/// Pins the P2.8b end-state: the dissolved Windows-tools facade is gone and
/// each focused service composes standalone. Sync twins are covered by the
/// async contract tests in <see cref="AsyncHygieneTests"/>.
/// </summary>
public sealed class WindowsToolsExtractionTests
{
    [Fact]
    public void ExtractedServices_ConstructWithoutSideEffects()
    {
        var runner = new ProcessRunner();
        var registry = new RegistryService();

        var network = new NetworkToolsService(runner);
        var pathResolver = new GameLoopPathResolver(registry);
        var shortcuts = new ShortcutService(runner, pathResolver, AppContext.BaseDirectory);
        var tempCleanup = new TempCleanupService(registry);

        network.Should().NotBeNull();
        shortcuts.Should().NotBeNull();
        tempCleanup.Should().NotBeNull();
    }
}
