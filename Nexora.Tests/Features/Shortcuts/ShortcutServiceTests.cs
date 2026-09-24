using FluentAssertions;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.GameLoop.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;
using Nexora.Features.Shortcuts.Application;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;
using Nexora.Infrastructure.GameLoop;

namespace Nexora.Tests.Features.Shortcuts;

/// <summary>
/// Verifies shortcut creation fails loudly when the GameLoop path is
/// unresolvable, instead of silently targeting a guessed drive.
/// </summary>
public sealed class ShortcutServiceTests
{
    [Fact]
    public void CreateShortcut_ReturnsFailure_WhenMarketPathUnresolvable()
    {
        var service = new ShortcutService(
            new UnusedRunner(),
            new GameLoopPathResolver(new NullRegistryService()),
            AppContext.BaseDirectory);

        var result = service.CreateShortcut("PUBG Mobile Global", "com.tencent.ig");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("could not be resolved from the registry");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../evil")]
    [InlineData("com.evil; rm -rf /")]
    public void GetIcon_ReturnsNull_ForInvalidPackageName(string? packageName)
    {
        var service = new ShortcutService(
            new UnusedRunner(),
            new GameLoopPathResolver(new NullRegistryService()),
            AppContext.BaseDirectory);

        service.GetIcon(packageName).Should().BeNull();
    }

    [Fact]
    public void GetIcon_ReturnsNull_WhenNoAssetExists()
    {
        var service = new ShortcutService(
            new UnusedRunner(),
            new GameLoopPathResolver(new NullRegistryService()),
            AppContext.BaseDirectory);

        service.GetIcon("a.b").Should().BeNull();
    }

    private sealed class NullRegistryService : IMachineRegistry
    {
        public string? GetLocalString(string name, string? branch = null) => null;

        public bool SetLocalMachineDword(string subKeyPath, string name, int value) => false;

        public int? GetLocalMachineDword(string subKeyPath, string name) => null;
    }

    private sealed class UnusedRunner : IProcessRunner
    {
        public ProcessResult Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null) =>
            throw new InvalidOperationException("Process runner must not be used when path resolution fails.");

        public ProcessResult RunPowerShell(string script, TimeSpan? timeout = null) =>
            throw new InvalidOperationException("Process runner must not be used when path resolution fails.");

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, TimeSpan? timeout, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Process runner must not be used when path resolution fails.");

        public bool StartDetachedElevated(string fileName, string arguments = "") =>
            throw new InvalidOperationException("Process runner must not be used when path resolution fails.");
    }
}
