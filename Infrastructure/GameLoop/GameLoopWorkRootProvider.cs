using Nexora.Configuration;
using Nexora.Shared.Kernel;

namespace Nexora.Infrastructure.GameLoop;

/// <summary>
/// Resolves GameLoop working roots from the runtime environment: bundled assets
/// live under the installation directory, mutable working files under local app data.
/// </summary>
public sealed class GameLoopWorkRootProvider : IWorkRootProvider
{
    private readonly EmulatorOptions _emulator;

    public GameLoopWorkRootProvider(EmulatorOptions? emulator = null)
    {
        _emulator = emulator ?? new EmulatorOptions();
    }

    public string AssetRoot => Path.Combine(AppContext.BaseDirectory, _emulator.Assets.DirectoryName);

    public string WorkRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        _emulator.Assets.WorkFolderName);
}
