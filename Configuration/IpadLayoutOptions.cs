namespace Nexora.Configuration;

/// <summary>
/// Options for iPad view resolution and keymap patching paths.
/// </summary>
public sealed class IpadLayoutOptions
{
    public const string SectionName = "IpadLayout";

    public string LayoutMapPath { get; init; } =
        Path.Combine(AppContext.BaseDirectory, AppConstants.Assets.DirectoryName, "ipad_layout_map.json");

    public string KeymapDirectory { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AndroidTbox");

    public string KeymapFileName { get; init; } = "TVM_100.xml";

    public string BackupExtension { get; init; } = ".mkbackup";

    public string GetKeymapFilePath() => Path.Combine(KeymapDirectory, KeymapFileName);

    public string GetBackupFilePath() => GetKeymapFilePath() + BackupExtension;
}
