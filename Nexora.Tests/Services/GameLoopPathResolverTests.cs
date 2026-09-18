using FluentAssertions;
using Nexora.Services;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

/// <summary>
/// Pins the P1.2 dedup: <see cref="GameLoopPathResolver.GetRoot"/> must
/// resolve through <see cref="GameLoopPathResolver.GetRootFromRegistry"/>
/// instead of reimplementing the registry lookup.
/// </summary>
public sealed class GameLoopPathResolverTests
{
    [Fact]
    public void GetRoot_ReturnsRegistryRoot_WhenRegistryResolves()
    {
        var installation = CreateUniqueDirectory();
        try
        {
            var uiDir = Directory.CreateDirectory(Path.Combine(installation, "UI")).FullName;
            var resolver = new GameLoopPathResolver(new FixedRegistryService(uiDir), emulator: NoCustomRoot());

            // Delegation proof: the full pipeline returns exactly what the registry-only lookup returns.
            resolver.GetRoot().Should().Be(resolver.GetRootFromRegistry());
            resolver.GetRoot().Should().Be(installation.TrimEnd(Path.DirectorySeparatorChar));
        }
        finally
        {
            StagingDirectoryGC.TryDeleteDirectory(installation);
        }
    }

    [Fact]
    public void GetRootFromRegistry_ReturnsNull_WhenRegistryEmpty()
    {
        var resolver = new GameLoopPathResolver(new FixedRegistryService(null), emulator: NoCustomRoot());

        resolver.GetRootFromRegistry().Should().BeNull();
    }

    [Fact]
    public void GetRoot_ReturnsCustomRoot_WhenOverrideBeatsRegistry()
    {
        var customRoot = CreateUniqueDirectory();
        var registryRoot = CreateUniqueDirectory();
        try
        {
            var uiDir = Directory.CreateDirectory(Path.Combine(registryRoot, "UI")).FullName;
            var resolver = new GameLoopPathResolver(
                new FixedRegistryService(uiDir),
                emulator: WithCustomRoot(customRoot));

            resolver.GetRoot().Should().Be(Path.GetFullPath(customRoot));
        }
        finally
        {
            StagingDirectoryGC.TryDeleteDirectory(customRoot);
            StagingDirectoryGC.TryDeleteDirectory(registryRoot);
        }
    }

    [Fact]
    public void GetRoot_FallsBackToRegistry_WhenCustomRootIsStale()
    {
        var installation = CreateUniqueDirectory();
        try
        {
            var uiDir = Directory.CreateDirectory(Path.Combine(installation, "UI")).FullName;
            var stale = Path.Combine(installation, "Moved-Away");
            var resolver = new GameLoopPathResolver(
                new FixedRegistryService(uiDir),
                emulator: WithCustomRoot(stale));

            resolver.GetRoot().Should().Be(installation.TrimEnd(Path.DirectorySeparatorChar));
        }
        finally
        {
            StagingDirectoryGC.TryDeleteDirectory(installation);
        }
    }

    [Fact]
    public void GetAppMarketPath_ReturnsNull_WhenDerivedDirectoryDoesNotExist()
    {
        var installation = CreateUniqueDirectory();
        try
        {
            var uiDir = Directory.CreateDirectory(Path.Combine(installation, "UI")).FullName;
            var resolver = new GameLoopPathResolver(new FixedRegistryService(uiDir, mirrorUiToMarket: false), emulator: NoCustomRoot());

            resolver.GetAppMarketPath().Should().BeNull();
        }
        finally
        {
            StagingDirectoryGC.TryDeleteDirectory(installation);
        }
    }

    [Fact]
    public void GetAppMarketPath_ReturnsMarketDirectory_WhenItExists()
    {
        var installation = CreateUniqueDirectory();
        try
        {
            var uiDir = Directory.CreateDirectory(Path.Combine(installation, "UI")).FullName;
            var marketDir = Directory.CreateDirectory(Path.Combine(installation, "AppMarket")).FullName;
            var resolver = new GameLoopPathResolver(new FixedRegistryService(uiDir, mirrorUiToMarket: false), emulator: NoCustomRoot());

            resolver.GetAppMarketPath().Should().Be(Path.GetFullPath(marketDir));
        }
        finally
        {
            StagingDirectoryGC.TryDeleteDirectory(installation);
        }
    }

    [Fact]
    public void GetAppMarketPath_ReturnsCustomMarket_WhenOverrideSet()
    {
        var customRoot = CreateUniqueDirectory();
        try
        {
            var marketDir = Directory.CreateDirectory(Path.Combine(customRoot, "AppMarket")).FullName;
            var resolver = new GameLoopPathResolver(
                new FixedRegistryService(null),
                emulator: WithCustomRoot(customRoot));

            resolver.GetAppMarketPath().Should().Be(Path.GetFullPath(marketDir));
        }
        finally
        {
            StagingDirectoryGC.TryDeleteDirectory(customRoot);
        }
    }

    private static Nexora.Configuration.EmulatorOptions NoCustomRoot() =>
        new() { Emulator = new Nexora.Configuration.EmulatorOptions.EmulatorSettings { CustomInstallRoot = null } };

    private static Nexora.Configuration.EmulatorOptions WithCustomRoot(string path) =>
        new() { Emulator = new Nexora.Configuration.EmulatorOptions.EmulatorSettings { CustomInstallRoot = path } };

    private static string CreateUniqueDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "NexoraResolverTest-" + Guid.NewGuid().ToString("N"));
        return Directory.CreateDirectory(path).FullName;
    }

    private sealed class FixedRegistryService(string? installPath, string? marketPath = null, bool mirrorUiToMarket = true) : IMachineRegistry
    {
        public string? GetLocalString(string name, string? branch = null)
        {
            // UI branch reads return the install path; the AppMarket branch
            // returns null by default so derivation paths are exercised,
            // unless a market path (or mirroring) is requested.
            if (string.Equals(branch, "UI", StringComparison.OrdinalIgnoreCase)) return installPath;
            return marketPath ?? (mirrorUiToMarket ? installPath : null);
        }

        public bool SetLocalMachineDword(string subKeyPath, string name, int value) => false;

        public int? GetLocalMachineDword(string subKeyPath, string name) => null;
    }
}
