using FluentAssertions;
using Nexora.Configuration;
using Nexora.Features.Optimizer.Application;
using Xunit;
using Nexora.Infrastructure.Registry;

namespace Nexora.Tests.Configuration;

public sealed class OptionsTests
{
    [Fact]
    public void TempCleanupOptions_Defaults_AreConfigured()
    {
        var options = new TempCleanupOptions();

        options.TargetDirectories.Should().NotBeNull();
        options.TargetDirectories.Should().HaveCount(3);
        options.TargetDirectories.Should().Contain(Path.GetTempPath());
        options.ShaderCacheFolderName.Should().Be("ShaderCache");
        TempCleanupOptions.SectionName.Should().Be("TempCleanup");
    }

    [Fact]
    public void TempCleanupOptions_CustomValues_RetainOverrides()
    {
        var customDirs = new[] { @"C:\TestDir1", @"C:\TestDir2" };
        var options = new TempCleanupOptions
        {
            TargetDirectories = customDirs,
            ShaderCacheFolderName = "CustomCache"
        };

        options.TargetDirectories.Should().BeEquivalentTo(customDirs);
        options.ShaderCacheFolderName.Should().Be("CustomCache");
    }

    [Fact]
    public void IpadLayoutOptions_Defaults_AreConfigured()
    {
        var options = new IpadLayoutOptions();

        IpadLayoutOptions.SectionName.Should().Be("IpadLayout");
        options.KeymapFileName.Should().Be("TVM_100.xml");
        options.BackupExtension.Should().Be(".nexora-backup");
        options.LegacyBackupExtension.Should().Be(".mkbackup");
        options.KeymapDirectory.Should().EndWith("AndroidTbox");
        options.LayoutMapPath.Should().EndWith("ipad_layout_map.json");
        options.GetKeymapFilePath().Should().Be(Path.Combine(options.KeymapDirectory, "TVM_100.xml"));
        options.GetBackupFilePath().Should().Be(Path.Combine(options.KeymapDirectory, "TVM_100.xml.nexora-backup"));
        options.GetLegacyBackupFilePath().Should().Be(Path.Combine(options.KeymapDirectory, "TVM_100.xml.mkbackup"));
    }

    [Fact]
    public void IpadLayoutOptions_CustomPaths_AreComputedCorrectly()
    {
        var customDir = @"D:\CustomAppdata\Tbox";
        var options = new IpadLayoutOptions
        {
            KeymapDirectory = customDir,
            KeymapFileName = "Custom_100.xml",
            BackupExtension = ".bak",
            LayoutMapPath = @"D:\Assets\custom_map.json"
        };

        options.GetKeymapFilePath().Should().Be(Path.Combine(customDir, "Custom_100.xml"));
        options.GetBackupFilePath().Should().Be(Path.Combine(customDir, "Custom_100.xml.bak"));
        options.LayoutMapPath.Should().Be(@"D:\Assets\custom_map.json");
    }

    [Fact]
    public void UpdateOptions_Defaults_AreConfigured()
    {
        var options = new UpdateOptions();

        UpdateOptions.SectionName.Should().Be("Update");
        options.Repository.Should().Be("Nexora-Systems-Dev/Nexora-PUBG-Mobile-Tool");
        options.UserAgent.Should().Be("Nexora-PUBG-Mobile-Tool");
        options.Runtime.Should().Be("win-x64");
        options.StagingPrefix.Should().Be("NexoraUpdate-");
        options.ArchiveFileName.Should().Be("update.zip");
        options.ExtractionFolderName.Should().Be("extracted");
        options.PortableExecutableName.Should().Be($"{AppConstants.ApplicationName}.exe");
        options.HttpTimeout.Should().Be(TimeSpan.FromSeconds(12));
        options.ExpectedPublisher.Should().Be("Nexora");
        options.StaleStagingMaxAge.Should().Be(TimeSpan.FromHours(24));

        var uri = new Uri(options.ReleasesUrl);
        uri.Scheme.Should().Be(Uri.UriSchemeHttps);
        uri.AbsoluteUri.Should().Contain(options.Repository);
    }

    [Fact]
    public void UpdateOptions_CustomRepository_FlowsIntoReleasesUrl()
    {
        var options = new UpdateOptions { Repository = "Example/Repo" };

        options.ReleasesUrl.Should().Be("https://api.github.com/repos/Example/Repo/releases/latest");
    }

    [Fact]
    public void GameLoopOptions_Defaults_AreConfigured()
    {
        var options = new GameLoopOptions();

        GameLoopOptions.SectionName.Should().Be("GameLoop");
        options.Adb.FileName.Should().Be("adb.exe");
        options.Adb.PreferredSerial.Should().Be("emulator-5554");
        options.Adb.TcpPortSuffix.Should().Be(":5555");
        options.Adb.LoopbackEndpoint.Should().Be("127.0.0.1:5555");
        options.Registry.BranchAppMarket.Should().Be("AppMarket");
        options.Registry.BranchUI.Should().Be("UI");
        options.Registry.ValueInstallPath.Should().Be("InstallPath");
        options.Registry.ValueAdbDisable.Should().Be("AdbDisable");
    }

    [Fact]
    public void GameLoopOptions_Timeouts_ArePositive()
    {
        var timeouts = new GameLoopOptions().Timeouts;
        var spans = new[]
        {
            timeouts.DefaultProcessTimeout,
            timeouts.AdbCommandTimeout,
            timeouts.AdbTransferRetryDelay,
            timeouts.AdbBootPollDelay,
            timeouts.HardwareDetectionTimeout,
            timeouts.DnsChangeTimeout,
            timeouts.NvidiaImportTimeout,
            timeouts.TaskkillTimeout,
            timeouts.MonitorInterval,
            timeouts.MonitorStopTimeout,
            new UpdateOptions().HttpTimeout,
        };

        spans.Should().OnlyContain(timeout => timeout > TimeSpan.Zero);
    }

    [Fact]
    public void GameLoopOptions_RetryCounts_ArePositive()
    {
        var timeouts = new GameLoopOptions().Timeouts;

        timeouts.AdbTransferMaxAttempts.Should().BeGreaterThan(0);
        timeouts.AdbBootPollAttempts.Should().BeGreaterThan(0);
        timeouts.DnsPingAttempts.Should().BeGreaterThan(0);
        timeouts.AdbKillWaitMilliseconds.Should().BeGreaterThan(0);
        timeouts.ForceStopSettleDelayMilliseconds.Should().BeGreaterThan(0);
        timeouts.DnsPingTimeoutMilliseconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GameLoopOptions_CustomValues_RetainOverrides()
    {
        var options = new GameLoopOptions
        {
            Timeouts = new GameLoopOptions.TimeoutSettings
            {
                MonitorInterval = TimeSpan.FromSeconds(9),
                DnsPingAttempts = 2
            }
        };

        options.Timeouts.MonitorInterval.Should().Be(TimeSpan.FromSeconds(9));
        options.Timeouts.DnsPingAttempts.Should().Be(2);
        options.Timeouts.ShutdownRestoreTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void EmulatorOptions_Defaults_AreConfigured()
    {
        var options = new EmulatorOptions();

        EmulatorOptions.SectionName.Should().Be("Emulator");
        options.Emulator.InstallFolderName.Should().Be("TxGameAssistant");
        options.Emulator.AppMarketFileName.Should().Be("AppMarket.exe");
        options.Assets.DirectoryName.Should().Be("Assets");
        options.Assets.IconsDirectoryName.Should().Be("Icons");
        options.Assets.WorkFolderName.Should().Be(AppConstants.ApplicationName);
    }

    [Fact]
    public void EmulatorOptions_ImageNames_AreDistinctExecutables()
    {
        var names = new EmulatorOptions().Emulator.ProcessImageNames;

        names.Should().NotBeEmpty();
        names.Should().OnlyContain(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        names.Select(name => name.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EmulatorOptions_SafeFallbackImageNames_AreSubsetOfProcessImageNames()
    {
        // The fallback may only name processes the force-close scan already
        // knows, never a generic Windows process.
        var emulator = new EmulatorOptions().Emulator;

        emulator.SafeFallbackImageNames.Should().BeSubsetOf(
            emulator.ProcessImageNames, "fallback names must be known GameLoop images");
    }

    [Fact]
    public void EmulatorOptions_RegistryImageNames_AreSubsetOfProcessImageNames()
    {
        var emulator = new EmulatorOptions().Emulator;

        emulator.RegistryImageNames.Should().BeSubsetOf(
            emulator.ProcessImageNames, "registry tuning must target known GameLoop images");
    }

    [Fact]
    public void EmulatorOptions_NvidiaProfileImageNames_AreSubsetOfProcessImageNames()
    {
        var emulator = new EmulatorOptions().Emulator;

        emulator.NvidiaProfileImageNames.Should().NotBeEmpty("the NVIDIA profile needs at least one target");
        emulator.NvidiaProfileImageNames.Should().BeSubsetOf(
            emulator.ProcessImageNames, "the NVIDIA profile must target known GameLoop images");
    }

    [Fact]
    public void EmulatorOptions_ProcessNames_MatchKnownImagesWithoutExtension()
    {
        var emulator = new EmulatorOptions().Emulator;
        var imageStems = emulator.ProcessImageNames
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        emulator.PerformanceProcessNames.Should().OnlyContain(name => imageStems.Contains(name));
        emulator.RunningCheckProcessNames.Should().OnlyContain(name => imageStems.Contains(name));
    }

    [Fact]
    public async Task TempCleanupService_WithCustomOptions_SweepsConfiguredDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "NexoraCleanupTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var testFile = Path.Combine(tempRoot, "scratch.tmp");
            File.WriteAllText(testFile, "test data");
            var subDir = Path.Combine(tempRoot, "subfolder");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "inner.tmp"), "inner data");

            var options = new TempCleanupOptions
            {
                TargetDirectories = new[] { tempRoot },
                ShaderCacheFolderName = "NonExistentShaderCache"
            };

            var registry = new RegistryService();
            var service = new TempCleanupService(registry, options);

            var result = await service.CleanTempAsync();

            result.Success.Should().BeTrue();
            File.Exists(testFile).Should().BeFalse();
            Directory.Exists(subDir).Should().BeFalse();
            Directory.Exists(tempRoot).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }
}
