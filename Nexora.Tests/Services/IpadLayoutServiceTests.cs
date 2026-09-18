using System.Diagnostics;
using FluentAssertions;
using Nexora.Configuration;
using Nexora.Features.Layout;
using Nexora.Services;
using Nexora.Services.Performance;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

/// <summary>
/// Verifies that IpadLayoutService handles missing keymaps and backups safely.
/// </summary>
public sealed class IpadLayoutServiceTests
{
    [Fact]
    public void SetIpadResolution_Fails_WhenKeymapFileDoesNotExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"NexoraIpadTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var options = new IpadLayoutOptions { KeymapDirectory = tempDir };
            var registry = new RegistryService();
            var service = new IpadLayoutService(registry, new PhysicalFileSystem(), options);

            var result = service.SetIpadResolution(1440, 1080);

            result.Success.Should().BeFalse();
            result.Message.Should().Be("GameLoop keymap file was not found.");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ResetIpadResolution_Fails_WhenBackupDoesNotExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"NexoraIpadTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var options = new IpadLayoutOptions { KeymapDirectory = tempDir };
            var registry = new RegistryService();
            var service = new IpadLayoutService(registry, new PhysicalFileSystem(), options);

            var result = service.ResetIpadResolution();

            result.Success.Should().BeFalse();
            result.Message.Should().Be("No saved iPad resolution was found.");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ResetIpadResolution_RestoresFromLegacyBackup_AndKeepsLegacyFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"NexoraIpadTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var options = new IpadLayoutOptions { KeymapDirectory = tempDir };
            File.WriteAllText(options.GetKeymapFilePath(), "<patched />");
            File.WriteAllText(options.GetLegacyBackupFilePath(), "<original />");
            var service = new IpadLayoutService(new RegistryService(), new PhysicalFileSystem(), options);

            var result = service.ResetIpadResolution();

            result.Success.Should().BeTrue();
            File.ReadAllText(options.GetKeymapFilePath()).Should().Be("<original />");
            File.Exists(options.GetLegacyBackupFilePath()).Should().BeTrue("the legacy backup must never be auto-deleted");
            File.Exists(options.GetBackupFilePath()).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ResetIpadResolution_PrefersNewBackup_WhenBothExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"NexoraIpadTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var options = new IpadLayoutOptions { KeymapDirectory = tempDir };
            File.WriteAllText(options.GetKeymapFilePath(), "<patched />");
            File.WriteAllText(options.GetBackupFilePath(), "<new />");
            File.WriteAllText(options.GetLegacyBackupFilePath(), "<legacy />");
            var service = new IpadLayoutService(new RegistryService(), new PhysicalFileSystem(), options);

            var result = service.ResetIpadResolution();

            result.Success.Should().BeTrue();
            File.ReadAllText(options.GetKeymapFilePath()).Should().Be("<new />");
            File.Exists(options.GetBackupFilePath()).Should().BeFalse();
            File.Exists(options.GetLegacyBackupFilePath()).Should().BeTrue("the legacy orphan is left in place, never auto-migrated");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SetIpadResolution_RewritesPositionSwitchCoordinates_ForSupportedPackage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"NexoraIpadTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var options = new IpadLayoutOptions { KeymapDirectory = tempDir, LayoutMapPath = Path.Combine(tempDir, "map.json") };
            File.WriteAllText(options.GetKeymapFilePath(),
                "<Item ApkName=\"com.tencent.ig\"><KeyMapMode Name=\"Battle\">" +
                "<KeyMappingEx ItemName=\"Fire\"><SwitchOperation Description=\"Tap\" EnablePositionSwitch=\"0:10,20,30,40\"/>" +
                "<Point Point_X=\"1\" Point_Y=\"2\"/></KeyMappingEx></KeyMapMode></Item>" +
                "<Item ApkName=\"com.example.other\"><KeyMapMode Name=\"Battle\">" +
                "<KeyMappingEx ItemName=\"Fire\"><SwitchOperation Description=\"Tap\" EnablePositionSwitch=\"0:10,20,30,40\"/>" +
                "</KeyMappingEx></KeyMapMode></Item>");
            File.WriteAllText(options.LayoutMapPath, "{\"Battle\":{\"Fire\":{\"Tap\":[[\"100\",\"200\"]]}}}");
            var service = new IpadLayoutService(new MemoryRegistryService(), new PhysicalFileSystem(), options);

            var result = service.SetIpadResolution(1440, 1080);

            result.Success.Should().BeTrue();
            var updated = File.ReadAllText(options.GetKeymapFilePath());
            updated.Should().Contain("EnablePositionSwitch=\"0:10,100,30,200\"");
            updated.Should().Contain("EnablePositionSwitch=\"0:10,20,30,40\"");
            File.Exists(options.GetBackupFilePath()).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SetIpadResolution_RewritesBasicMappingPoints()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"NexoraIpadTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var options = new IpadLayoutOptions { KeymapDirectory = tempDir, LayoutMapPath = Path.Combine(tempDir, "map.json") };
            File.WriteAllText(options.GetKeymapFilePath(),
                "<Item ApkName=\"com.tencent.ig\"><KeyMapMode Name=\"Battle\">" +
                "<KeyMapping ItemName=\"Jump\" Point_X=\"1\" Point_Y=\"2\">" +
                "<SwitchOperation EnableSwitch=\"Tap\" Point_X=\"3\" Point_Y=\"4\"/></KeyMapping>" +
                "</KeyMapMode></Item>");
            File.WriteAllText(options.LayoutMapPath, "{\"Battle\":{\"Jump\":{\"Tap\":[[\"100\",\"200\"]]}}}");
            var service = new IpadLayoutService(new MemoryRegistryService(), new PhysicalFileSystem(), options);

            var result = service.SetIpadResolution(1440, 1080);

            result.Success.Should().BeTrue();
            var updated = File.ReadAllText(options.GetKeymapFilePath());
            updated.Should().Contain("Point_X=\"100\"");
            updated.Should().Contain("Point_Y=\"200\"");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SetIpadResolution_Rejects_WhenGameLoopProcessesAreRunning()
    {
        // The guard moved in from the dissolved SystemToolsFacade: the
        // service itself must refuse to patch while GameLoop holds the
        // keymap file. The current test-host process stands in for a
        // running emulator instance.
        using var running = Process.GetCurrentProcess();
        var service = new IpadLayoutService(
            new RegistryService(),
            new PhysicalFileSystem(),
            processService: new FixedProcessService(new List<Process> { running }));

        var result = service.SetIpadResolution(1920, 1440);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Close GameLoop before applying iPad View");
    }

    [Fact]
    public void SetIpadResolution_PassesGuard_WhenNoGameLoopProcessesAreRunning()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"NexoraIpadTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var options = new IpadLayoutOptions { KeymapDirectory = tempDir };
            var service = new IpadLayoutService(
                new RegistryService(),
                new PhysicalFileSystem(),
                options,
                new FixedProcessService(new List<Process>()));

            var result = service.SetIpadResolution(1440, 1080);

            result.Success.Should().BeFalse();
            result.Message.Should().Be("GameLoop keymap file was not found.");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private sealed class FixedProcessService : IGameLoopProcessService
    {
        private readonly List<Process> _processes;

        public FixedProcessService(List<Process> processes) => _processes = processes;

        public OperationResult KillGameLoopProcesses(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Kill path is not exercised by the guard tests.");

        public List<Process> FindGameLoopProcesses(string? gameLoopRoot = null) => _processes;

        public string? GetGameLoopRootFromRegistry() => null;

        public string? GetGameLoopRoot() => null;

        public string? GetGameLoopUiPath() => null;
    }

    private sealed class MemoryRegistryService : IUserRegistry
    {
        private readonly Dictionary<string, int> _userDwords = new(StringComparer.OrdinalIgnoreCase);

        public int? GetUserDword(string name) => _userDwords.TryGetValue(name, out var value) ? value : null;

        public bool SetUserDword(string name, int value)
        {
            _userDwords[name] = value;
            return true;
        }

        public int? GetAppSettingDword(string name) => null;

        public void SetAppSettingDword(string name, int value)
        {
        }

        public void DeleteAppSetting(string name)
        {
        }

        public bool SetCurrentUserString(string subKeyPath, string name, string value) => false;

        public string? GetCurrentUserString(string subKeyPath, string name) => null;
    }
}
