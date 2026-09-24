namespace Nexora.Configuration;

/// <summary>
/// Options for emulator installation identity: process and image names,
/// install folder names, and managed asset file names.
/// </summary>
public sealed class EmulatorOptions
{
    public const string SectionName = "Emulator";

    public EmulatorSettings Emulator { get; init; } = new();

    public AssetSettings Assets { get; init; } = new();

    public sealed class EmulatorSettings
    {
        public string InstallFolderName { get; init; } = "TxGameAssistant";

        /// <summary>
        /// Manual GameLoop root override (tier-0, checked before registry).
        /// Validated (must exist on disk) and honored by GetRoot/GetUiPath/GetAppMarketPath.
        /// Defaults to NEXORA_GAMELOOP_ROOT so users with custom D:\/E:\ installs
        /// can persist an override without waiting for a settings UI.
        /// </summary>
        public string? CustomInstallRoot { get; init; } = Environment.GetEnvironmentVariable("NEXORA_GAMELOOP_ROOT");

        public string AppMarketFileName { get; init; } = "AppMarket.exe";

        /// <summary>Process names used for GameLoop liveness checks.</summary>
        public string[] RunningCheckProcessNames { get; init; } =
        {
            "AndroidEmulatorEx", "AndroidEmulatorEn", "AndroidEmulator", "AppMarket", "aow_exe"
        };

        /// <summary>Process names targeted for high-priority tuning.</summary>
        public string[] PerformanceProcessNames { get; init; } =
        {
            "aow_exe", "AndroidEmulatorEn", "AndroidEmulatorEx", "AndroidEmulator", "AndroidRenderer"
        };

        /// <summary>Image names targeted during the force-close pass.</summary>
        public string[] ProcessImageNames { get; init; } =
        {
            "aow_exe.exe", "AndroidEmulatorEn.exe", "AndroidEmulator.exe", "AndroidEmulatorEx.exe",
            "AndroidRenderer.exe", "TBSWebRenderer.exe", "syzs_dl_svr.exe", "AppMarket.exe",
            "QMEmulatorService.exe", "GameLoader.exe", "TSettingCenter.exe", "Auxillary.exe",
            "TP3Helper.exe", "GameDownload.exe", "TInst.exe", "TxGaDcc.exe"
        };

        /// <summary>
        /// Image names matched by name when process executable paths are inaccessible.
        /// </summary>
        public string[] SafeFallbackImageNames { get; init; } =
        {
            "aow_exe.exe", "AndroidEmulatorEn.exe", "AndroidEmulator.exe", "AndroidEmulatorEx.exe",
            "AndroidRenderer.exe", "TBSWebRenderer.exe", "syzs_dl_svr.exe", "QMEmulatorService.exe",
            "GameLoader.exe", "TP3Helper.exe", "GameDownload.exe"
        };

        /// <summary>Image names targeted by registry and GPU optimization passes.</summary>
        public string[] RegistryImageNames { get; init; } =
        {
            "AndroidEmulator.exe", "AndroidEmulatorEn.exe", "AndroidEmulatorEx.exe",
            "aow_exe.exe", "AndroidRenderer.exe"
        };

        /// <summary>
        /// Image names retargeted by the NVIDIA profile pass, in preference
        /// order (the first one found names the profile).
        /// AndroidRenderer.exe is covered by GPU routing and IFEO instead.
        /// </summary>
        public string[] NvidiaProfileImageNames { get; init; } =
        {
            "AndroidEmulatorEx.exe", "AndroidEmulatorEn.exe", "AndroidEmulator.exe", "aow_exe.exe"
        };
    }

    public sealed class AssetSettings
    {
        public string DirectoryName { get; init; } = "Assets";

        public string IconsDirectoryName { get; init; } = "Icons";

        public string WorkFolderName { get; init; } = AppConstants.ApplicationName;

        public string PreviousSavFileName { get; init; } = "nexora-previous.mkvip";

        public string PendingSavFileName { get; init; } = "nexora-pending.mkvip";

        public string ShadowSettingsFileName { get; init; } = "nexora-shadow.mkvip";

        public string ConnectionProbeFileName { get; init; } = "nexora-probe.mkvip";

        public string KoreanResolutionFileName { get; init; } = "nexora-kr.ini";

        public string NvidiaProfileFileName { get; init; } = "nexora.nip";

        public string NvidiaInspectorFileName { get; init; } = "nvidiaProfileInspector.exe";
    }
}
