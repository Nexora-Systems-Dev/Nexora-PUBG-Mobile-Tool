using FluentAssertions;
using Nexora.Configuration;
using Xunit;

namespace Nexora.Tests.Configuration;

/// <summary>
/// Guards the P2-Step-1 single source of truth: the application version,
/// update endpoints, timeout thresholds, and emulator process names must
/// live in <see cref="AppConstants"/> exactly once, well-formed, so no
/// service drifts with its own hardcoded copy.
/// </summary>
public sealed class AppConstantsTests
{
    [Fact]
    public void CurrentVersion_HasTagFormat()
    {
        // Arrange & Act
        var version = AppConstants.CurrentVersion;

        // Assert: matches the version tag scheme used by GitHub releases.
        version.Should().MatchRegex(@"^v\d+\.\d+\.\d+$");
    }

    [Fact]
    public void UpdateReleasesUrl_IsAbsoluteHttpsEndpoint()
    {
        // Arrange & Act
        var uri = new Uri(AppConstants.Update.ReleasesUrl);

        // Assert
        uri.Scheme.Should().Be(Uri.UriSchemeHttps);
        uri.AbsoluteUri.Should().Contain(AppConstants.Update.Repository);
    }

    [Fact]
    public void PortableExecutableName_DerivesFromApplicationName()
    {
        // Arrange & Act & Assert
        AppConstants.Update.PortableExecutableName.Should().Be($"{AppConstants.ApplicationName}.exe");
    }

    [Fact]
    public void Timeouts_ArePositive()
    {
        // Arrange & Act
        var timeouts = new[]
        {
            AppConstants.Timeouts.DefaultProcessTimeout,
            AppConstants.Timeouts.AdbCommandTimeout,
            AppConstants.Timeouts.AdbTransferRetryDelay,
            AppConstants.Timeouts.AdbBootPollDelay,
            AppConstants.Timeouts.HardwareDetectionTimeout,
            AppConstants.Timeouts.DnsChangeTimeout,
            AppConstants.Timeouts.NvidiaImportTimeout,
            AppConstants.Timeouts.TaskkillTimeout,
            AppConstants.Timeouts.MonitorInterval,
            AppConstants.Timeouts.MonitorStopTimeout,
            AppConstants.Update.HttpTimeout,
        };

        // Assert
        timeouts.Should().OnlyContain(timeout => timeout > TimeSpan.Zero);
    }

    [Fact]
    public void RetryCounts_ArePositive()
    {
        // Arrange & Act & Assert
        AppConstants.Timeouts.AdbTransferMaxAttempts.Should().BeGreaterThan(0);
        AppConstants.Timeouts.AdbBootPollAttempts.Should().BeGreaterThan(0);
        AppConstants.Timeouts.DnsPingAttempts.Should().BeGreaterThan(0);
        AppConstants.Timeouts.AdbKillWaitMilliseconds.Should().BeGreaterThan(0);
        AppConstants.Timeouts.ForceStopSettleDelayMilliseconds.Should().BeGreaterThan(0);
        AppConstants.Timeouts.DnsPingTimeoutMilliseconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EmulatorImageNames_AreDistinctExecutables()
    {
        // Arrange & Act
        var names = AppConstants.Emulator.ProcessImageNames;

        // Assert
        names.Should().NotBeEmpty();
        names.Should().OnlyContain(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        names.Select(name => name.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void SafeFallbackImageNames_AreSubsetOfProcessImageNames()
    {
        // Arrange & Act & Assert: the fallback may only name processes the
        // force-close scan already knows, never a generic Windows process.
        AppConstants.Emulator.SafeFallbackImageNames.Should().BeSubsetOf(
            AppConstants.Emulator.ProcessImageNames, "fallback names must be known GameLoop images");
    }

    [Fact]
    public void RegistryImageNames_AreSubsetOfProcessImageNames()
    {
        // Arrange & Act & Assert
        AppConstants.Emulator.RegistryImageNames.Should().BeSubsetOf(
            AppConstants.Emulator.ProcessImageNames, "registry tuning must target known GameLoop images");
    }

    [Fact]
    public void PerformanceProcessNames_MatchKnownImagesWithoutExtension()
    {
        // Arrange & Act
        var imageStems = AppConstants.Emulator.ProcessImageNames
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Assert
        AppConstants.Emulator.PerformanceProcessNames.Should().OnlyContain(name => imageStems.Contains(name));
        AppConstants.Emulator.RunningCheckProcessNames.Should().OnlyContain(name => imageStems.Contains(name));
    }
}
