using FluentAssertions;
using Nexora.Configuration;
using Nexora.Services;
using Nexora.Shared.Infrastructure;
using Xunit;

namespace Nexora.Tests.Services;

/// <summary>
/// Verifies that IpadLayoutService handles missing keymaps and backups safely.
/// </summary>
public sealed class IpadLayoutServiceTests
{
    [Fact]
    public void Apply_Fails_WhenKeymapFileDoesNotExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"NexoraIpadTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var options = new IpadLayoutOptions { KeymapDirectory = tempDir };
            var registry = new RegistryService();
            var service = new IpadLayoutService(registry, options);

            var result = service.Apply(1440, 1080);

            result.Success.Should().BeFalse();
            result.Message.Should().Be("GameLoop keymap file was not found.");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Reset_Fails_WhenBackupDoesNotExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"NexoraIpadTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var options = new IpadLayoutOptions { KeymapDirectory = tempDir };
            var registry = new RegistryService();
            var service = new IpadLayoutService(registry, options);

            var result = service.Reset();

            result.Success.Should().BeFalse();
            result.Message.Should().Be("No saved iPad resolution was found.");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
