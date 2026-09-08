using FluentAssertions;
using Nexora.Configuration;
using Nexora.Services;
using Nexora.Shared.Infrastructure;
using Xunit;

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
        options.BackupExtension.Should().Be(".mkbackup");
        options.KeymapDirectory.Should().EndWith("AndroidTbox");
        options.LayoutMapPath.Should().EndWith("ipad_layout_map.json");
        options.GetKeymapFilePath().Should().Be(Path.Combine(options.KeymapDirectory, "TVM_100.xml"));
        options.GetBackupFilePath().Should().Be(Path.Combine(options.KeymapDirectory, "TVM_100.xml.mkbackup"));
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
    public void TempCleanupService_WithCustomOptions_SweepsConfiguredDirectory()
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

            var result = service.CleanTemp();

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
