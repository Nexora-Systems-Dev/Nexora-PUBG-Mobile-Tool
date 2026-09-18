using FluentAssertions;
using Nexora.Configuration;
using Nexora.Features.GameLoop;
using Nexora.Features.Layout;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

/// <summary>
/// Proves the P4.1 filesystem seam: domain services route all IO through the
/// injected <see cref="IFileSystem"/>, so an in-memory fake fully substitutes
/// the disk, and <see cref="PhysicalFileSystem"/> delegates to System.IO.
/// </summary>
public sealed class FileSystemAbstractionTests
{
    [Fact]
    public void IpadLayoutService_ResetIpadResolution_RoutesThroughInjectedFileSystem()
    {
        // The directories do NOT exist on disk: success proves every IO call
        // went through the fake.
        var options = new IpadLayoutOptions { KeymapDirectory = @"Z:\NexoraFsSeamTest\Keymaps" };
        var fake = new MemoryFileSystem();
        fake.WriteAllText(options.GetBackupFilePath(), "<backup />");
        var service = new IpadLayoutService(new RegistryService(), fake, options);

        var result = service.ResetIpadResolution();

        result.Success.Should().BeTrue();
        fake.Calls.Should().Contain($"Copy:{options.GetBackupFilePath()}>{options.GetKeymapFilePath()}");
        fake.Calls.Should().Contain($"Delete:{options.GetBackupFilePath()}");
    }

    [Fact]
    public void GameLoopWorkingStorage_PrepareWorkingFiles_SeedsThroughInjectedFileSystem()
    {
        var fake = new MemoryFileSystem();
        const string assetRoot = @"Z:\NexoraFsSeamTest\Assets";
        const string workRoot = @"Z:\NexoraFsSeamTest\Work";
        var storage = new GameLoopWorkingStorage(fake, new StubRoots(assetRoot, workRoot));
        var assets = new EmulatorOptions().Assets;
        foreach (var name in new[]
        {
            assets.PreviousSavFileName,
            assets.PendingSavFileName,
            assets.ShadowSettingsFileName,
            assets.ConnectionProbeFileName
        })
        {
            fake.WriteAllText(Path.Combine(assetRoot, name), "seed");
        }

        storage.PrepareWorkingFiles();

        foreach (var name in new[]
        {
            assets.PreviousSavFileName,
            assets.PendingSavFileName,
            assets.ShadowSettingsFileName,
            assets.ConnectionProbeFileName
        })
        {
            fake.Exists(Path.Combine(workRoot, name)).Should().BeTrue();
        }
    }

    [Fact]
    public void GameLoopWorkRootProvider_ComposesDocumentedLocations()
    {
        IWorkRootProvider provider = new GameLoopWorkRootProvider();
        var assets = new EmulatorOptions().Assets;

        provider.AssetRoot.Should().EndWith(assets.DirectoryName);
        provider.WorkRoot.Should().EndWith(assets.WorkFolderName);
        Path.IsPathRooted(provider.AssetRoot).Should().BeTrue();
        Path.IsPathRooted(provider.WorkRoot).Should().BeTrue();
    }

    [Fact]
    public void PhysicalFileSystem_DelegatesToDisk()
    {
        var root = Path.Combine(Path.GetTempPath(), "NexoraFsSeamTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            IFileSystem fileSystem = new PhysicalFileSystem();
            var file = Path.Combine(root, "probe.txt");
            var copy = Path.Combine(root, "copy.txt");

            fileSystem.CreateDirectory(root);
            fileSystem.Exists(file).Should().BeFalse();
            fileSystem.WriteAllText(file, "a\nb");
            fileSystem.ReadAllText(file).Should().Be("a\nb");
            fileSystem.ReadLines(file).Should().Equal("a", "b");
            fileSystem.Copy(file, copy);
            fileSystem.Exists(copy).Should().BeTrue();
            fileSystem.WriteAllBytes(file, new byte[] { 0x01 });
            fileSystem.ReadAllBytes(file).Should().Equal(0x01);
            fileSystem.Delete(copy);
            fileSystem.Exists(copy).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubRoots(string assetRoot, string workRoot) : IWorkRootProvider
    {
        public string AssetRoot { get; } = assetRoot;

        public string WorkRoot { get; } = workRoot;
    }

    private sealed class MemoryFileSystem : IFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Calls { get; } = new();

        public bool Exists(string path) => _files.ContainsKey(path);

        public byte[] ReadAllBytes(string path) => System.Text.Encoding.UTF8.GetBytes(ReadAllText(path));

        public void WriteAllBytes(string path, byte[] bytes) =>
            WriteAllText(path, System.Text.Encoding.UTF8.GetString(bytes));

        public string ReadAllText(string path) => _files[path];

        public void WriteAllText(string path, string contents) => _files[path] = contents;

        public string[] ReadAllLines(string path) => ReadLines(path).ToArray();

        public void WriteAllLines(string path, IEnumerable<string> lines) =>
            WriteAllText(path, string.Join('\n', lines));

        public IEnumerable<string> ReadLines(string path) =>
            ReadAllText(path).Split('\n').Select(line => line.TrimEnd('\r'));

        public void Copy(string sourceFileName, string destinationFileName, bool overwrite = false)
        {
            Calls.Add($"Copy:{sourceFileName}>{destinationFileName}");
            _files[destinationFileName] = _files[sourceFileName];
        }

        public void Delete(string path)
        {
            Calls.Add($"Delete:{path}");
            _files.Remove(path);
        }

        public void CreateDirectory(string path) => Calls.Add($"Create:{path}");
    }
}
