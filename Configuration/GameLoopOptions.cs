namespace Nexora.Configuration;

/// <summary>
/// Options for GameLoop ADB bridge settings, registry branches, and operational timeouts.
/// </summary>
public sealed class GameLoopOptions
{
    public const string SectionName = "GameLoop";

    public AdbSettings Adb { get; init; } = new();

    public RegistrySettings Registry { get; init; } = new();

    public TimeoutSettings Timeouts { get; init; } = new();

    public sealed class AdbSettings
    {
        public string FileName { get; init; } = "adb.exe";

        public string PreferredSerial { get; init; } = "emulator-5554";

        /// <summary>TCP port suffix used by GameLoop Android bridge connections.</summary>
        public string TcpPortSuffix { get; init; } = ":5555";

        public string LoopbackEndpoint { get; init; } = "127.0.0.1:5555";
    }

    public sealed class RegistrySettings
    {
        public string BranchAppMarket { get; init; } = "AppMarket";

        public string BranchUI { get; init; } = "UI";

        public string ValueInstallPath { get; init; } = "InstallPath";

        public string ValueAdbDisable { get; init; } = "AdbDisable";
    }

    public sealed class TimeoutSettings
    {
        /// <summary>Default cap for any spawned process (<see cref="Infrastructure.Processes.ProcessRunner"/>).</summary>
        public TimeSpan DefaultProcessTimeout { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>Timeout for a single ADB command round-trip.</summary>
        public TimeSpan AdbCommandTimeout { get; init; } = TimeSpan.FromSeconds(20);

        /// <summary>Timeout waiting for taskkill to exit when stopping ADB, in milliseconds.</summary>
        public int AdbKillWaitMilliseconds { get; init; } = 5000;

        public int AdbTransferMaxAttempts { get; init; } = 3;

        public TimeSpan AdbTransferRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

        public int AdbBootPollAttempts { get; init; } = 60;

        public TimeSpan AdbBootPollDelay { get; init; } = TimeSpan.FromSeconds(1);

        /// <summary>Pause after force-stopping the game package, in milliseconds.</summary>
        public int ForceStopSettleDelayMilliseconds { get; init; } = 200;

        public TimeSpan HardwareDetectionTimeout { get; init; } = TimeSpan.FromSeconds(20);

        public TimeSpan DnsChangeTimeout { get; init; } = TimeSpan.FromSeconds(45);

        public TimeSpan NvidiaImportTimeout { get; init; } = TimeSpan.FromSeconds(60);

        public TimeSpan TaskkillTimeout { get; init; } = TimeSpan.FromSeconds(10);

        public int DnsPingAttempts { get; init; } = 5;

        public int DnsPingTimeoutMilliseconds { get; init; } = 1000;

        public TimeSpan MonitorInterval { get; init; } = TimeSpan.FromSeconds(2);

        public TimeSpan MonitorStopTimeout { get; init; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Maximum time to wait for performance session restoration during application shutdown.
        /// </summary>
        public TimeSpan ShutdownRestoreTimeout { get; init; } = TimeSpan.FromSeconds(5);
    }
}
