using Nexora.Configuration;

namespace Nexora.Features.GameLoop;

/// <summary>
/// Manages local working file cache and seeds template assets from the installation root.
/// </summary>
public sealed class GameLoopWorkingStorage
{
    private readonly string _assetRoot;
    private readonly string _workRoot;

    public GameLoopWorkingStorage(string? assetRoot = null, string? workRoot = null)
    {
        _assetRoot = assetRoot ?? Path.Combine(AppContext.BaseDirectory, AppConstants.Assets.DirectoryName);
        _workRoot = workRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppConstants.Assets.WorkFolderName);
    }

    public string AssetRoot => _assetRoot;
    public string WorkRoot => _workRoot;

    public string PreviousSavPath => Path.Combine(_workRoot, AppConstants.Assets.PreviousSavFileName);
    public string PendingSavPath => Path.Combine(_workRoot, AppConstants.Assets.PendingSavFileName);
    public string ShadowSettingsPath => Path.Combine(_workRoot, AppConstants.Assets.ShadowSettingsFileName);
    public string ConnectionProbePath => Path.Combine(_workRoot, AppConstants.Assets.ConnectionProbeFileName);
    public string KoreanResolutionAssetPath => Path.Combine(_assetRoot, AppConstants.Assets.KoreanResolutionFileName);

    public void EnsureDirectoryCreated()
    {
        Directory.CreateDirectory(_workRoot);
    }

    /// <summary>
    /// Copies default template assets from the installation directory to the working folder if missing.
    /// </summary>
    public void PrepareWorkingFiles()
    {
        EnsureDirectoryCreated();
        var filesToSeed = new[]
        {
            AppConstants.Assets.PreviousSavFileName,
            AppConstants.Assets.PendingSavFileName,
            AppConstants.Assets.ShadowSettingsFileName,
            AppConstants.Assets.ConnectionProbeFileName
        };

        foreach (var name in filesToSeed)
        {
            var source = Path.Combine(_assetRoot, name);
            var destination = Path.Combine(_workRoot, name);
            if (File.Exists(source) && !File.Exists(destination))
            {
                File.Copy(source, destination);
            }
        }
    }
}
