using System.Reflection;
using FluentAssertions;
using Nexora.Features.GameLoop;
using Nexora.Services;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

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
    public void BackupRemoteFolder_WritesNewSuffix_WhenNoBackupExists()
    {
        var adb = new ScriptedFakeAdb(directories: [DataPath]);
        var applier = CreateApplier(adb);

        InvokeBackup(applier, DataPath);

        adb.Commands.Should().Contain($"mv {DataPath} {DataPath}.nexora-backup");
        adb.DirectoryExists($"{DataPath}.nexora-backup").Should().BeTrue();
        adb.DirectoryExists(DataPath).Should().BeFalse();
    }

    [Fact]
    public void BackupRemoteFolder_RemovesRemote_WhenLegacyBackupAlreadyExists()
    {
        var adb = new ScriptedFakeAdb(directories: [DataPath, $"{DataPath}.MKbackup"]);
        var applier = CreateApplier(adb);

        InvokeBackup(applier, DataPath);

        adb.Commands.Should().Contain($"rm -r {DataPath}");
        adb.Commands.Should().NotContain(command => command.StartsWith("mv ", StringComparison.Ordinal));
        adb.DirectoryExists($"{DataPath}.MKbackup").Should().BeTrue("the legacy backup must be preserved");
    }

    [Fact]
    public void RestoreRemoteFolder_PrefersNewBackup_WhenBothExist()
    {
        var adb = new ScriptedFakeAdb(directories: [$"{DataPath}.nexora-backup", $"{DataPath}.MKbackup"]);
        var applier = CreateApplier(adb);

        InvokeRestore(applier, DataPath);

        adb.Commands.Should().Contain($"mv {DataPath}.nexora-backup {DataPath}");
        adb.DirectoryExists($"{DataPath}.MKbackup").Should().BeTrue("the legacy orphan is left in place, never auto-migrated");
    }

    [Fact]
    public void RestoreRemoteFolder_FallsBackToLegacyBackup_WhenNewIsAbsent()
    {
        var adb = new ScriptedFakeAdb(directories: [$"{DataPath}.MKbackup"]);
        var applier = CreateApplier(adb);

        InvokeRestore(applier, DataPath);

        adb.Commands.Should().Contain($"mv {DataPath}.MKbackup {DataPath}");
        adb.DirectoryExists(DataPath).Should().BeTrue();
    }

    [Fact]
    public void RestoreRemoteFolder_DoesNothing_WhenNeitherBackupExists()
    {
        var adb = new ScriptedFakeAdb(directories: []);
        var applier = CreateApplier(adb);

        InvokeRestore(applier, DataPath);

        adb.Commands.Should().NotContain(command => command.StartsWith("mv ", StringComparison.Ordinal));
    }

    private static GraphicsSettingsApplier CreateApplier(ScriptedFakeAdb adb) =>
        new(
            adb,
            new GameLoopWorkingStorage(new PhysicalFileSystem(), new GameLoopWorkRootProvider()),
            new PhysicalFileSystem(),
            new GameLoopSession());

    private static void InvokeBackup(GraphicsSettingsApplier applier, string remotePath) =>
        InvokePrivate(applier, "BackupRemoteFolder", remotePath);

    private static void InvokeRestore(GraphicsSettingsApplier applier, string remotePath) =>
        InvokePrivate(applier, "RestoreRemoteFolder", remotePath);

    private static void InvokePrivate(GraphicsSettingsApplier applier, string methodName, string remotePath)
    {
        var method = typeof(GraphicsSettingsApplier).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull($"production method '{methodName}' must exist");
        method!.Invoke(applier, [remotePath, CancellationToken.None]);
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

        public Task<bool> PullAsync(string remotePath, string localPath, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> PushAsync(string localPath, string remotePath, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> WaitForBootAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public IReadOnlyList<string> FindInstalledPackages(IEnumerable<string> packageNames, CancellationToken cancellationToken) =>
            [];

        public void StopAdb()
        {
        }

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
    }
}
