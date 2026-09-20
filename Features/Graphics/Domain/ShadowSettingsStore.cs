using Nexora.Shared.Kernel;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Graphics.Domain;

/// <summary>
/// Owns the UserCustom.ini file side effects for shadow presets; the CVar
/// transformation itself stays in the pure <see cref="UnrealCVarCodec"/>.
/// </summary>
public sealed class ShadowSettingsStore
{
    private readonly IFileSystem _fileSystem;

    public ShadowSettingsStore(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    /// <summary>
    /// Updates the shadow settings in a local UserCustom.ini file.
    /// </summary>
    public OperationResult UpdateFile(string filePath, bool enable)
    {
        if (!_fileSystem.Exists(filePath))
        {
            return OperationResult.Fail("Could not read the PUBG shadow settings.");
        }

        var lines = _fileSystem.ReadAllLines(filePath);
        if (!UnrealCVarCodec.TryApplyShadowPreset(lines, enable, out var updatedLines))
        {
            return OperationResult.Fail("The PUBG shadow setting was not found.");
        }

        _fileSystem.WriteAllLines(filePath, updatedLines);
        return OperationResult.Ok(enable ? "Shadow enabled." : "Shadow disabled.");
    }
}
