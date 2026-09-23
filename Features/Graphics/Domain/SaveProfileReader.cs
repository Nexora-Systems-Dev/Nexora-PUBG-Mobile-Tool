using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.GameLoop.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Features.Graphics.Domain;

/// <summary>
/// Reads the loaded PUBG Mobile graphics profile: quality, frame rate, style, and shadow setting.
/// </summary>
public sealed class SaveProfileReader
{
    private readonly IAdbClient _adb;
    private readonly GameLoopWorkingStorage _storage;
    private readonly IFileSystem _fileSystem;
    private readonly GameLoopSession _session;

    public SaveProfileReader(
        IAdbClient adb,
        GameLoopWorkingStorage storage,
        IFileSystem fileSystem,
        GameLoopSession session)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public string? GetGraphicsQuality() => PubgVersionCatalog.QualityName(ReadProperty("BattleRenderQuality"));

    public string? GetFrameRate() => PubgVersionCatalog.FrameRateName(ReadProperty("BattleFPS"));

    public string? GetGraphicsStyle() => PubgVersionCatalog.StyleName(ReadProperty("BattleRenderStyle"));

    /// <summary>
    /// Retrieves the current shadow setting ("Enable" or "Disable") from UserCustom.ini,
    /// or null when the profile is unreadable (no package, missing file, pull failed,
    /// marker absent). Unreadable must never masquerade as a confident "Disable".
    /// </summary>
    public async Task<string?> GetShadowAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_session.CurrentPackage))
        {
            return null;
        }

        var localShadowPath = _storage.ShadowSettingsPath;
        if (!await EnsureLocalShadowFileAsync(localShadowPath, cancellationToken))
        {
            return null;
        }

        foreach (var line in _fileSystem.ReadLines(localShadowPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(UnrealCVarCodec.CVarPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            // Decode the full payload instead of matching raw hex: the reader
            // stays correct if the cipher rotates, and corrupt lines (odd or
            // non-hex input) decode to empty and are skipped, never misread.
            var decoded = UnrealCVarCodec.DecodeCVar(trimmed[UnrealCVarCodec.CVarPrefix.Length..]);
            if (decoded.StartsWith("r.ShadowQuality=", StringComparison.Ordinal))
            {
                return decoded == "r.ShadowQuality=1" ? "Enable" : "Disable";
            }
        }

        return null;
    }

    /// <summary>
    /// Ensures the local UserCustom.ini is available, pulling it from the device
    /// when the working copy is absent. A failed pull returns false so the reader
    /// reports unreadable instead of scanning a stale or missing file.
    /// </summary>
    private async Task<bool> EnsureLocalShadowFileAsync(string localShadowPath, CancellationToken cancellationToken)
    {
        if (_fileSystem.Exists(localShadowPath))
        {
            return true;
        }

        var remoteShadowPath = RemotePaths.For(_session.CurrentPackage).UserCustomIniPath;
        return await _adb.PullAsync(remoteShadowPath, localShadowPath, cancellationToken);
    }

    private byte ReadProperty(string name)
    {
        if (_session.ActiveSavContent is null)
        {
            return 0;
        }

        return new Ue4SavEditor(_session.ActiveSavContent).ReadProperty(name);
    }
}
