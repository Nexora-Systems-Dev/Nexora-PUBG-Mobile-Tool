using Nexora.Configuration;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Features.GameLoop;

/// <summary>
/// Manages local working file cache and seeds template assets from the installation root.
/// </summary>
public sealed class GameLoopWorkingStorage
{
    private readonly IFileSystem _fileSystem;
    private readonly EmulatorOptions _emulator;
    private readonly string _assetRoot;
    private readonly string _workRoot;

    public GameLoopWorkingStorage(IFileSystem fileSystem, IWorkRootProvider roots, EmulatorOptions? emulator = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        if (roots is null) throw new ArgumentNullException(nameof(roots));
        _emulator = emulator ?? new EmulatorOptions();
        _assetRoot = roots.AssetRoot;
        _workRoot = roots.WorkRoot;
    }

    public string AssetRoot => _assetRoot;
    public string WorkRoot => _workRoot;

    public string PreviousSavPath => Path.Combine(_workRoot, _emulator.Assets.PreviousSavFileName);
    public string PendingSavPath => Path.Combine(_workRoot, _emulator.Assets.PendingSavFileName);
    public string ShadowSettingsPath => Path.Combine(_workRoot, _emulator.Assets.ShadowSettingsFileName);
    public string ConnectionProbePath => Path.Combine(_workRoot, _emulator.Assets.ConnectionProbeFileName);
    public string KoreanResolutionAssetPath => Path.Combine(_assetRoot, _emulator.Assets.KoreanResolutionFileName);

    public void EnsureDirectoryCreated()
    {
        _fileSystem.CreateDirectory(_workRoot);
    }

    /// <summary>
    /// Copies default template assets from the installation directory to the working folder if missing.
    /// </summary>
    public void PrepareWorkingFiles()
    {
        EnsureDirectoryCreated();
        var filesToSeed = new[]
        {
            _emulator.Assets.PreviousSavFileName,
            _emulator.Assets.PendingSavFileName,
            _emulator.Assets.ShadowSettingsFileName,
            _emulator.Assets.ConnectionProbeFileName
        };

        foreach (var name in filesToSeed)
        {
            var source = Path.Combine(_assetRoot, name);
            var destination = Path.Combine(_workRoot, name);
            if (_fileSystem.Exists(source) && !_fileSystem.Exists(destination))
            {
                _fileSystem.Copy(source, destination);
            }
        }
    }
}
