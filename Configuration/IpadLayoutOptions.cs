namespace Nexora.Configuration;

/// <summary>
/// Options for iPad view resolution and keymap patching paths.
/// </summary>
public sealed class IpadLayoutOptions
{
    public const string SectionName = "IpadLayout";

    public IpadLayoutOptions()
        : this(new EmulatorOptions())
    {
    }

    public IpadLayoutOptions(EmulatorOptions emulator)
    {
        var assetsDirectory = (emulator ?? throw new ArgumentNullException(nameof(emulator))).Assets.DirectoryName;
        LayoutMapPath = Path.Combine(AppContext.BaseDirectory, assetsDirectory, "ipad_layout_map.json");
    }

    public string LayoutMapPath { get; init; }

    public string KeymapDirectory { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AndroidTbox");

    public string KeymapFileName { get; init; } = "TVM_100.xml";

    public string BackupExtension { get; init; } = ".nexora-backup";

    /// <summary>
    /// Legacy backup suffix written by older versions. Permanent read
    /// fallback only: new backups are never written with this suffix and it
    /// is never auto-deleted (same shape as the registry branding fallback).
    /// </summary>
    public string LegacyBackupExtension { get; init; } = ".mkbackup";

    public string GetKeymapFilePath() => Path.Combine(KeymapDirectory, KeymapFileName);

    public string GetBackupFilePath() => GetKeymapFilePath() + BackupExtension;

    public string GetLegacyBackupFilePath() => GetKeymapFilePath() + LegacyBackupExtension;
}
