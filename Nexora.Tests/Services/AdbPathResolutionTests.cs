using FluentAssertions;
using Nexora.Services;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

/// <summary>
/// Pins the P0.5 contract: <see cref="AdbClient"/> construction performs no
/// installation probing, and <see cref="AdbClient.RefreshAdbPath"/> re-probes
/// on demand so a GameLoop install/repair is picked up at runtime.
/// </summary>
public sealed class AdbPathResolutionTests
{
    [Fact]
    public void Constructor_PerformsNoPathResolution()
    {
        var resolver = new ThrowingPathResolver();

        var act = () => new AdbClient(new ProcessRunner(), resolver);

        act.Should().NotThrow("construction must stay pure with no registry, process, or filesystem reads");
    }

    [Fact]
    public void FindAdbPath_ReturnsBareFileName_AsIntentionalPathFallback()
    {
        var client = new AdbClient(new ProcessRunner(), new CountingPathResolver());

        // Nothing resolves: the bare exe name is the documented PATH fallback,
        // and Run() below turns a miss into an explicit error (never silent).
        client.FindAdbPath().Should().Be("adb.exe");
    }

    [Fact]
    public void Run_ReturnsActionableError_WhenAdbExecutableIsMissing()
    {
        var gameLoop = new Nexora.Configuration.GameLoopOptions
        {
            Adb = new Nexora.Configuration.GameLoopOptions.AdbSettings { FileName = "nexora-nonexistent-adb-test.exe" }
        };
        var client = new AdbClient(new ProcessRunner(), new CountingPathResolver(), gameLoop: gameLoop);

        var result = client.Run("version");

        result.Succeeded.Should().BeFalse();
        result.StandardError.Should().Contain("ADB executable could not be started");
        result.StandardError.Should().Contain("NEXORA_GAMELOOP_ROOT");
    }

    [Fact]
    public void RefreshAdbPath_ReprobesInstallation_OnDemand()
    {
        var resolver = new CountingPathResolver();
        var client = new AdbClient(new ProcessRunner(), resolver);

        resolver.Calls.Should().Be(0, "construction must not probe the installation");
        client.RefreshAdbPath();
        resolver.Calls.Should().Be(3, "one probe pass covers the UI, AppMarket, and root lookups");
        client.RefreshAdbPath();
        resolver.Calls.Should().Be(6, "each refresh must re-probe rather than reuse the cached path");
    }

    private sealed class ThrowingPathResolver : IGameLoopPathResolver
    {
        public string? GetRootFromRegistry() => throw new InvalidOperationException("Must not resolve during construction.");

        public string? GetRoot() => throw new InvalidOperationException("Must not resolve during construction.");

        public string? GetUiPath() => throw new InvalidOperationException("Must not resolve during construction.");

        public string? GetAppMarketPath() => throw new InvalidOperationException("Must not resolve during construction.");

        public bool IsGameLoopPath(string? executablePath, string? gameLoopRoot) => throw new InvalidOperationException("Must not resolve during construction.");
    }

    private sealed class CountingPathResolver : IGameLoopPathResolver
    {
        public int Calls { get; private set; }

        public string? GetRootFromRegistry()
        {
            Calls++;
            return null;
        }

        public string? GetRoot()
        {
            Calls++;
            return null;
        }

        public string? GetUiPath()
        {
            Calls++;
            return null;
        }

        public string? GetAppMarketPath()
        {
            Calls++;
            return null;
        }

        public bool IsGameLoopPath(string? executablePath, string? gameLoopRoot) => false;
    }
}
