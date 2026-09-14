namespace Nexora.Configuration;

using System.Text.RegularExpressions;

/// <summary>
/// Operational constants including application identity, endpoints,
/// timeouts, ADB settings, process names, and asset file names.
/// </summary>
public static class AppConstants
{
    public const string ApplicationName = "Nexora PUBG Mobile Tool";

    /// <summary>
    /// Current release tag matching the project version in Nexora.csproj.
    /// </summary>
    public const string CurrentVersion = "v1.0.15";

    public static class Update
    {
        public const string Repository = "Nexora-Systems-Dev/Nexora-PUBG-Mobile-Tool";
        public const string ReleasesUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
        public const string UserAgent = "Nexora-PUBG-Mobile-Tool";
        public const string Runtime = "win-x64";
        public const string StagingPrefix = "NexoraUpdate-";
        public const string ArchiveFileName = "update.zip";
        public const string ExtractionFolderName = "extracted";
        public const string PortableExecutableName = ApplicationName + ".exe";

        public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(12);

        /// <summary>
        /// Expected publisher identifier in the Authenticode certificate subject.
        /// </summary>
        public const string ExpectedPublisher = "Nexora";

        /// <summary>
        /// Maximum age before temporary update staging directories are swept as orphans.
        /// </summary>
        public static readonly TimeSpan StaleStagingMaxAge = TimeSpan.FromHours(24);
    }

    public static class Timeouts
    {
        /// <summary>Default cap for any spawned process (<see cref="Shared.Kernel.ProcessRunner"/>).</summary>
        public static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromSeconds(30);

        /// <summary>Timeout for a single ADB command round-trip.</summary>
        public static readonly TimeSpan AdbCommandTimeout = TimeSpan.FromSeconds(20);

        /// <summary>Timeout waiting for taskkill to exit when stopping ADB, in milliseconds.</summary>
        public const int AdbKillWaitMilliseconds = 5000;

        public const int AdbTransferMaxAttempts = 3;
        public static readonly TimeSpan AdbTransferRetryDelay = TimeSpan.FromSeconds(1);

        public const int AdbBootPollAttempts = 60;
        public static readonly TimeSpan AdbBootPollDelay = TimeSpan.FromSeconds(1);

        /// <summary>Pause after force-stopping the game package, in milliseconds.</summary>
        public const int ForceStopSettleDelayMilliseconds = 200;

        public static readonly TimeSpan HardwareDetectionTimeout = TimeSpan.FromSeconds(20);
        public static readonly TimeSpan DnsChangeTimeout = TimeSpan.FromSeconds(45);
        public static readonly TimeSpan NvidiaImportTimeout = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan TaskkillTimeout = TimeSpan.FromSeconds(10);

        public const int DnsPingAttempts = 5;
        public const int DnsPingTimeoutMilliseconds = 1000;

        public static readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan MonitorStopTimeout = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Maximum time to wait for performance session restoration during application shutdown.
        /// </summary>
        public static readonly TimeSpan ShutdownRestoreTimeout = TimeSpan.FromSeconds(5);
    }

    public static class Adb
    {
        public const string FileName = "adb.exe";
        public const string PreferredSerial = "emulator-5554";

        /// <summary>TCP port suffix used by GameLoop Android bridge connections.</summary>
        public const string TcpPortSuffix = ":5555";
        public const string LoopbackEndpoint = "127.0.0.1:5555";
    }

    public static class Emulator
    {
        public const string InstallFolderName = "TxGameAssistant";
        public const string AppMarketFileName = "AppMarket.exe";

        /// <summary>Process names used for GameLoop liveness checks.</summary>
        public static readonly string[] RunningCheckProcessNames =
        {
            "AndroidEmulatorEx", "AndroidEmulatorEn", "AndroidEmulator"
        };

        /// <summary>Process names targeted for high-priority tuning.</summary>
        public static readonly string[] PerformanceProcessNames =
        {
            "aow_exe", "AndroidEmulatorEn", "AndroidEmulatorEx", "AndroidEmulator", "AndroidRenderer"
        };

        /// <summary>Image names targeted during the force-close pass.</summary>
        public static readonly string[] ProcessImageNames =
        {
            "aow_exe.exe", "AndroidEmulatorEn.exe", "AndroidEmulator.exe", "AndroidEmulatorEx.exe",
            "AndroidRenderer.exe", "TBSWebRenderer.exe", "syzs_dl_svr.exe", "AppMarket.exe",
            "QMEmulatorService.exe", "GameLoader.exe", "TSettingCenter.exe", "Auxillary.exe",
            "TP3Helper.exe", "GameDownload.exe", "TInst.exe", "TxGaDcc.exe"
        };

        /// <summary>
        /// Image names matched by name when process executable paths are inaccessible.
        /// </summary>
        public static readonly string[] SafeFallbackImageNames =
        {
            "aow_exe.exe", "AndroidEmulatorEn.exe", "AndroidEmulator.exe", "AndroidEmulatorEx.exe",
            "AndroidRenderer.exe", "TBSWebRenderer.exe", "syzs_dl_svr.exe", "QMEmulatorService.exe",
            "GameLoader.exe", "TP3Helper.exe", "GameDownload.exe"
        };

        /// <summary>Image names targeted by registry and GPU optimization passes.</summary>
        public static readonly string[] RegistryImageNames =
        {
            "AndroidEmulator.exe", "AndroidEmulatorEn.exe", "AndroidEmulatorEx.exe",
            "aow_exe.exe", "AndroidRenderer.exe"
        };
    }

    public static class Registry
    {
        public const string BranchAppMarket = "AppMarket";
        public const string BranchUI = "UI";
        public const string ValueInstallPath = "InstallPath";
        public const string ValueAdbDisable = "AdbDisable";
    }

    public static class Assets
    {
        public const string DirectoryName = "Assets";
        public const string IconsDirectoryName = "Icons";
        public const string WorkFolderName = ApplicationName;

        public const string PreviousSavFileName = "old.mkvip";
        public const string PendingSavFileName = "new.mkvip";
        public const string ShadowSettingsFileName = "user.mkvip";
        public const string ConnectionProbeFileName = "testADB.mkvip";
        public const string KoreanResolutionFileName = "mk_kr.ini";
        public const string NvidiaProfileFileName = "mk.nip";
        public const string NvidiaInspectorFileName = "nvidiaProfileInspector.exe";

        public const string DefaultAppMarketPath = @"C:\Program Files\TxGameAssistant\AppMarket";
    }

    public static class Tools
    {
        public const string TaskkillFileName = "taskkill.exe";
    }

    public static class Validation
    {
        /// <summary>
        /// Regular expression pattern for valid Android package names.
        /// </summary>
        public const string AndroidPackageNamePattern = @"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$";

        private static readonly Regex AndroidPackageNameRegex = new(AndroidPackageNamePattern, RegexOptions.Compiled);

        public static bool IsValidAndroidPackageName(string? value) =>
            !string.IsNullOrWhiteSpace(value) && AndroidPackageNameRegex.IsMatch(value);
    }
}
