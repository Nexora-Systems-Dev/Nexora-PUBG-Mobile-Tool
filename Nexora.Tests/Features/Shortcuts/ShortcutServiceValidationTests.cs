using FluentAssertions;
using Nexora.Features.Shortcuts.Application;
using Nexora.Infrastructure.GameLoop;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Features.Shortcuts;

/// <summary>
/// Defense-in-depth pins for shortcut creation input validation: the picker
/// is catalog-gated upstream, but a traversal or injection-shaped value must
/// still fail inside <c>CreateShortcut</c> before any <c>.lnk</c>/<c>.ico</c>
/// is written. The throwing resolver proves the ordering — validation fires
/// before even path resolution — and the recording runner proves no shortcut
/// script ever runs for a rejected value.
/// </summary>
public sealed class ShortcutServiceValidationTests
{
    [Theory]
    [InlineData("../evil")]
    [InlineData("..\\evil")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:\\evil")]
    [InlineData("a:b")]
    [InlineData(" name")]
    [InlineData("name ")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void CreateShortcut_RejectsPathShapedDisplayName_BeforeAnyWrite(string? displayName)
    {
        var service = Build(out var runner);

        var result = service.CreateShortcut(displayName!, "com.tencent.ig");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid shortcut display name");
        runner.PowerShellCalls.Should().Be(0, "a rejected display name must never reach the shortcut script");
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("com.evil; rm -rf /")]
    [InlineData("NoDots")]
    [InlineData("com..evil")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void CreateShortcut_RejectsInvalidPackageName_BeforeAnyWrite(string? packageName)
    {
        var service = Build(out var runner);

        var result = service.CreateShortcut("PUBG Mobile Global", packageName!);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid PUBG package name");
        runner.PowerShellCalls.Should().Be(0, "a rejected package name must never reach the shortcut script");
    }

    [Fact]
    public void CreateShortcut_CatalogNames_PassValidation()
    {
        // "PUBG Mobile Global" carries spaces by design — the display-name
        // rule must not false-positive on real catalog values. With an
        // unresolvable install the failure must come from path resolution,
        // proving validation let the values through.
        var service = new ShortcutService(
            new RecordingRunner(),
            new GameLoopPathResolver(new NullRegistryService()),
            AppContext.BaseDirectory);

        var result = service.CreateShortcut("PUBG Mobile Global", "com.tencent.ig");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("could not be resolved from the registry");
    }

    private static ShortcutService Build(out RecordingRunner runner)
    {
        runner = new RecordingRunner();
        return new ShortcutService(runner, new ThrowingResolver(), AppContext.BaseDirectory);
    }

    /// <summary>Throws when consulted, proving validation runs before resolution.</summary>
    private sealed class ThrowingResolver : IGameLoopPathResolver
    {
        public string? GetRoot() => throw new InvalidOperationException("Path resolution must not run for rejected inputs.");
        public string? GetRootFromRegistry() => throw new InvalidOperationException("Path resolution must not run for rejected inputs.");
        public string? GetUiPath() => throw new InvalidOperationException("Path resolution must not run for rejected inputs.");
        public string? GetAppMarketPath() => throw new InvalidOperationException("Path resolution must not run for rejected inputs.");
        public bool IsGameLoopPath(string? executablePath, string? gameLoopRoot) => throw new InvalidOperationException("Path resolution must not run for rejected inputs.");
    }

    private sealed class NullRegistryService : IMachineRegistry
    {
        public string? GetLocalString(string name, string? branch = null) => null;

        public bool SetLocalMachineDword(string subKeyPath, string name, int value) => false;

        public int? GetLocalMachineDword(string subKeyPath, string name) => null;
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        public int PowerShellCalls { get; private set; }

        public ProcessResult Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null) =>
            new(0, string.Empty, string.Empty, false);

        public ProcessResult RunPowerShell(string script, TimeSpan? timeout = null)
        {
            PowerShellCalls++;
            return new ProcessResult(0, string.Empty, string.Empty, false);
        }

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, TimeSpan? timeout, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, false));

        public bool StartDetachedElevated(string fileName, string arguments = "") => false;
    }
}
