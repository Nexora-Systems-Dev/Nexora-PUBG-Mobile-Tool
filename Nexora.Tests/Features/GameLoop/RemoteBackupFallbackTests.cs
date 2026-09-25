using System.Reflection;
using FluentAssertions;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.GameLoop.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.GameLoop;
using Nexora.Infrastructure.Files;

namespace Nexora.Tests.Features.GameLoop;

/// <summary>
/// Proves the remote KR backup/restore path is bilingual across the
/// `.MKbackup` → `.nexora-backup` rename: new backups are written with the
/// new suffix, restores prefer it, and legacy-only devices still restore.
/// A scripted fake ADB answers directory probes from an in-memory set and
/// applies `mv`/`rm -r` to that set, so no emulator is needed.
/// </summary>
public sealed class RemoteBackupFallbackTests
{
    private const string DataPath = "/sdcard/Android/data/com.pubg.krmobile";

    [Fact]
    public async Task BackupRemoteFolder_WritesNewSuffix_WhenNoBackupExists()
    {
        var adb = new ScriptedFakeAdb(directories: [DataPath]);
        var applier = CreateApplier(adb);

        await InvokeBackup(applier, DataPath);

        adb.Commands.Should().Contain($"mv {DataPath} {DataPath}.nexora-backup");
        adb.DirectoryExists($"{DataPath}.nexora-backup").Should().BeTrue();
        adb.DirectoryExists(DataPath).Should().BeFalse();
    }

    [Fact]
    public async Task BackupRemoteFolder_RemovesRemote_WhenLegacyBackupAlreadyExists()
    {
        var adb = new ScriptedFakeAdb(directories: [DataPath, $"{DataPath}.MKbackup"]);
        var applier = CreateApplier(adb);

        await InvokeBackup(applier, DataPath);

        adb.Commands.Should().Contain($"rm -r {DataPath}");
        adb.Commands.Should().NotContain(command => command.StartsWith("mv ", StringComparison.Ordinal));
        adb.DirectoryExists($"{DataPath}.MKbackup").Should().BeTrue("the legacy backup must be preserved");
    }

    [Fact]
    public async Task RestoreRemoteFolder_PrefersNewBackup_WhenBothExist()
    {
        var adb = new ScriptedFakeAdb(directories: [$"{DataPath}.nexora-backup", $"{DataPath}.MKbackup"]);
        var applier = CreateApplier(adb);

        await InvokeRestore(applier, DataPath);

        adb.Commands.Should().Contain($"mv {DataPath}.nexora-backup {DataPath}");
        adb.DirectoryExists($"{DataPath}.MKbackup").Should().BeTrue("the legacy orphan is left in place, never auto-migrated");
    }

    [Fact]
    public async Task RestoreRemoteFolder_FallsBackToLegacyBackup_WhenNewIsAbsent()
    {
        var adb = new ScriptedFakeAdb(directories: [$"{DataPath}.MKbackup"]);
        var applier = CreateApplier(adb);

        await InvokeRestore(applier, DataPath);

        adb.Commands.Should().Contain($"mv {DataPath}.MKbackup {DataPath}");
        adb.DirectoryExists(DataPath).Should().BeTrue();
    }

    [Fact]
    public async Task RestoreRemoteFolder_DoesNothing_WhenNeitherBackupExists()
    {
        var adb = new ScriptedFakeAdb(directories: []);
        var applier = CreateApplier(adb);

        await InvokeRestore(applier, DataPath);

        adb.Commands.Should().NotContain(command => command.StartsWith("mv ", StringComparison.Ordinal));
    }

    private static GraphicsSettingsApplier CreateApplier(ScriptedFakeAdb adb) =>
        new(
            adb,
            new GameLoopWorkingStorage(new PhysicalFileSystem(), new GameLoopWorkRootProvider()),
            new PhysicalFileSystem(),
            new GameLoopSession());

    private static Task InvokeBackup(GraphicsSettingsApplier applier, string remotePath) =>
        InvokePrivate(applier, "BackupRemoteFolder", remotePath);

    private static Task InvokeRestore(GraphicsSettingsApplier applier, string remotePath) =>
        InvokePrivate(applier, "RestoreRemoteFolder", remotePath);

    private static async Task InvokePrivate(GraphicsSettingsApplier applier, string methodName, string remotePath)
    {
        var method = typeof(GraphicsSettingsApplier).GetMethod(methodName + "Async", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull($"production method '{methodName}Async' must exist");

        // The helpers take the private issue log by reference, so it is built by
        // reflection rather than new'ed here.
        var logType = typeof(GraphicsSettingsApplier).GetNestedType("DeviceIssueLog", BindingFlags.NonPublic);
        logType.Should().NotBeNull("the applier must keep its per-apply issue log");
        var log = Activator.CreateInstance(logType!, nonPublic: true);

        var abort = await (Task<string?>)method!.Invoke(applier, [remotePath, log, CancellationToken.None])!;
        abort.Should().BeNull("a scripted ADB that answers every probe must never report a destructive abort");
    }

    private sealed class ScriptedFakeAdb(IEnumerable<string> directories) : IAdbClient
    {
        private readonly HashSet<string> _directories = new(directories, StringComparer.Ordinal);

        public List<string> Commands { get; } = new();

        public string DeviceSerial => "emulator-5554";

        public void RefreshAdbPath()
        {
        }

        public ProcessResult Run(params string[] arguments) => new(0, "", "", false);

        public Task<bool> PullAsync(string remotePath, string localPath, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(false);

        public Task<bool> PushAsync(string localPath, string remotePath, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(false);

        public Task<bool> WaitForBootAsync(CancellationToken cancellationToken, IProgress<string>? progress = null) => Task.FromResult(true);

        public IReadOnlyList<string> FindInstalledPackages(IEnumerable<string> packageNames, CancellationToken cancellationToken) =>
            [];

        public Task<IReadOnlyList<string>> FindInstalledPackagesAsync(IEnumerable<string> packageNames, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(FindInstalledPackages(packageNames, cancellationToken));

        public void StopAdb()
        {
        }

        public Task StopAdbAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public bool DirectoryExists(string path) => _directories.Contains(path);

        public string Shell(string command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Shell(command);
        }

        public string Shell(string command)
        {
            Commands.Add(command);
            if (command.StartsWith("[ -d ", StringComparison.Ordinal))
            {
                var path = command.Substring("[ -d ".Length, command.IndexOf(" ]", StringComparison.Ordinal) - "[ -d ".Length);
                return _directories.Contains(path) ? "1" : "0";
            }

            if (command.StartsWith("mv ", StringComparison.Ordinal))
            {
                var parts = command.Substring(3).Split(' ', 2);
                if (_directories.Remove(parts[0])) _directories.Add(parts[1]);
                return "";
            }

            if (command.StartsWith("rm -r ", StringComparison.Ordinal))
            {
                _directories.Remove(command.Substring("rm -r ".Length));
                return "";
            }

            return "";
        }

        /// <summary>
        /// The applier now goes through the async surface, and it is the exit
        /// code there — not the stdout trim — that decides success, so the
        /// scripted answer is wrapped in a succeeded result.
        /// </summary>
        public Task<ProcessResult> ShellAsync(string command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ProcessResult(0, Shell(command), string.Empty, false));
        }
    }
}
