namespace Nexora.Configuration;

using System.Text.RegularExpressions;

/// <summary>
/// Single source of truth for operational constants: application identity,
/// update endpoints, process timeout thresholds, ADB connection details,
/// emulator process names, registry keys, and asset file names. Services
/// must reference these instead of keeping their own hardcoded copies so a
/// value can never drift between call sites.
/// </summary>
public static class AppConstants
{
    public const string ApplicationName = "Nexora PUBG Mobile Tool";

    /// <summary>
    /// Current release tag. Keep in sync with <c>&lt;Version&gt;</c> in
    /// <c>Nexora.csproj</c> (without the <c>v</c> prefix) and the About view,
    /// which renders this value at startup.
    /// </summary>
    public const string CurrentVersion = "v1.0.9";

    public static class Update
    {
        public const string Repository = "mohammad-emad-dev/Nexora-PUBG-Mobile-Tool";
        public const string ReleasesUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
        public const string UserAgent = "Nexora-PUBG-Mobile-Tool";
        public const string Runtime = "win-x64";
        public const string StagingPrefix = "NexoraUpdate-";
        public const string ArchiveFileName = "update.zip";
        public const string ExtractionFolderName = "extracted";
        public const string PortableExecutableName = ApplicationName + ".exe";

        public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(12);

        /// <summary>
        /// Staging trees older than this are treated as orphans from a
        /// killed or crashed run and swept on the next update attempt. Kept
        /// deliberately long so a concurrently running update from another
        /// app instance is never mistaken for an orphan.
        /// </summary>
        public static readonly TimeSpan StaleStagingMaxAge = TimeSpan.FromHours(24);
    }

    public static class Timeouts
    {
        /// <summary>Default cap for any spawned process (<see cref="Shared.Kernel.ProcessRunner"/>).</summary>
        public static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromSeconds(30);

        /// <summary>Cap for a single ADB command round-trip.</summary>
        public static readonly TimeSpan AdbCommandTimeout = TimeSpan.FromSeconds(20);

        /// <summary>How long the ADB teardown waits for taskkill to exit, in milliseconds.</summary>
        public const int AdbKillWaitMilliseconds = 5000;

        public const int AdbTransferMaxAttempts = 3;
        public static readonly TimeSpan AdbTransferRetryDelay = TimeSpan.FromSeconds(1);

        public const int AdbBootPollAttempts = 60;
        public static readonly TimeSpan AdbBootPollDelay = TimeSpan.FromSeconds(1);

        /// <summary>Settle pause after force-stopping the game package, in milliseconds.</summary>
        public const int ForceStopSettleDelayMilliseconds = 200;

        public static readonly TimeSpan HardwareDetectionTimeout = TimeSpan.FromSeconds(20);
        public static readonly TimeSpan DnsChangeTimeout = TimeSpan.FromSeconds(45);
        public static readonly TimeSpan NvidiaImportTimeout = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan TaskkillTimeout = TimeSpan.FromSeconds(10);

        public const int DnsPingAttempts = 5;
        public const int DnsPingTimeoutMilliseconds = 1000;

        public static readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan MonitorStopTimeout = TimeSpan.FromSeconds(2);
    }

    public static class Adb
    {
        public const string FileName = "adb.exe";
        public const string PreferredSerial = "emulator-5554";

        /// <summary>GameLoop exposes its Android bridge on this TCP port on some installations.</summary>
        public const string TcpPortSuffix = ":5555";
        public const string LoopbackEndpoint = "127.0.0.1:5555";
    }

    public static class Emulator
    {
        public const string InstallFolderName = "TxGameAssistant";
        public const string AppMarketFileName = "AppMarket.exe";

        /// <summary>Extensionless names for <c>Process.GetProcessesByName</c> liveness checks.</summary>
        public static readonly string[] RunningCheckProcessNames =
        {
            "AndroidEmulatorEx", "AndroidEmulatorEn", "AndroidEmulator"
        };

        /// <summary>Extensionless names eligible for High-priority tuning.</summary>
        public static readonly string[] PerformanceProcessNames =
        {
            "aow_exe", "AndroidEmulatorEn", "AndroidEmulatorEx", "AndroidEmulator", "AndroidRenderer"
        };

        /// <summary>Full image names scanned by the force-close pass.</summary>
        public static readonly string[] ProcessImageNames =
        {
            "aow_exe.exe", "AndroidEmulatorEn.exe", "AndroidEmulator.exe", "AndroidEmulatorEx.exe",
            "AndroidRenderer.exe", "TBSWebRenderer.exe", "syzs_dl_svr.exe", "AppMarket.exe",
            "QMEmulatorService.exe", "GameLoader.exe", "TSettingCenter.exe", "Auxillary.exe",
            "TP3Helper.exe", "GameDownload.exe", "TInst.exe", "TxGaDcc.exe"
        };

        /// <summary>
        /// Images safe to match by name when Windows denies reading the
        /// executable path. Must stay a subset of <see cref="ProcessImageNames"/>;
        /// never add a generic Windows process here.
        /// </summary>
        public static readonly string[] SafeFallbackImageNames =
        {
            "aow_exe.exe", "AndroidEmulatorEn.exe", "AndroidEmulator.exe", "AndroidEmulatorEx.exe",
            "AndroidRenderer.exe", "TBSWebRenderer.exe", "syzs_dl_svr.exe", "QMEmulatorService.exe",
            "GameLoader.exe", "TP3Helper.exe", "GameDownload.exe"
        };

        /// <summary>Images tuned by the GameLoop registry/GPU optimization pass.</summary>
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
        /// Strict Android package name: two or more dot-separated segments,
        /// each starting with a lowercase letter. Anything else is rejected
        /// before the value can reach an ADB shell command or a file path.
        /// </summary>
        public const string AndroidPackageNamePattern = @"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$";

        private static readonly Regex AndroidPackageNameRegex = new(AndroidPackageNamePattern, RegexOptions.Compiled);

        public static bool IsValidAndroidPackageName(string? value) =>
            !string.IsNullOrWhiteSpace(value) && AndroidPackageNameRegex.IsMatch(value);
    }
}
