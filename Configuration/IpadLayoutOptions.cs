namespace Nexora.Configuration;

/// <summary>
/// Strongly typed options configuring file paths and naming for iPad view resolution and keymap patching.
/// </summary>
public sealed class IpadLayoutOptions
{
    public const string SectionName = "IpadLayout";

    /// <summary>
    /// Path to the bundled or custom iPad layout coordinate map JSON file.
    /// </summary>
    public string LayoutMapPath { get; init; } =
        Path.Combine(AppContext.BaseDirectory, AppConstants.Assets.DirectoryName, "ipad_layout_map.json");

    /// <summary>
    /// Base directory where GameLoop stores its emulator keymap configuration.
    /// Defaults to %APPDATA%\AndroidTbox.
    /// </summary>
    public string KeymapDirectory { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AndroidTbox");

    /// <summary>
    /// Keymap XML file name. Defaults to TVM_100.xml.
    /// </summary>
    public string KeymapFileName { get; init; } = "TVM_100.xml";

    /// <summary>
    /// File extension appended to the keymap file to create the backup copy.
    /// </summary>
    public string BackupExtension { get; init; } = ".mkbackup";

    /// <summary>
    /// Returns the full path to the active keymap XML file.
    /// </summary>
    public string GetKeymapFilePath() => Path.Combine(KeymapDirectory, KeymapFileName);

    /// <summary>
    /// Returns the full path to the backup keymap file.
    /// </summary>
    public string GetBackupFilePath() => GetKeymapFilePath() + BackupExtension;
}
