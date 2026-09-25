using System.Reflection;
using FluentAssertions;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.GameLoop.Infrastructure;
using Nexora.Infrastructure.Processes;
using Nexora.Shared.Contracts;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Features.GameLoop;

/// <summary>
/// Pins the U-03 honesty rules for the Korean 1080p apply, which is the one
/// destructive sequence the tool runs against a user's device. No emulator is
/// needed: a failure-injecting fake ADB answers directory probes from an
/// in-memory set and records every command, so the ordering is observable.
/// <para>
/// Before the fix the applier went through the sync <c>Shell</c>, which returns
/// only trimmed stdout, so a command that failed device-side looked exactly like
/// one that succeeded, and the sequence always ended in
/// <c>OperationResult.Ok("…set to 1080p.")</c> — the tool could report success
/// over a half-applied, account-cleared install.
/// </para>
/// </summary>
public sealed class KoreanFullHdAbortTests
{
    private const string KoreanPackage = "com.pubg.krmobile";

    private static readonly string DataPath = $"/sdcard/Android/data/{KoreanPackage}";

    /// <summary>
    /// Every destructive command (<c>pm clear</c> / <c>mv</c> / <c>cp -r</c> /
    /// <c>rm -r</c>) that fails must fail the whole apply, naming the command,
    /// and must never be reported as the 1080p success.
    /// </summary>
    [Fact]
    public async Task DestructiveCommandFailure_FailsTheApply_NamingTheCommand()
    {
        var adb = new FailingFakeAdb(directories: [DataPath]);
        adb.FailWhen.Add(command => command.StartsWith("cp -r ", StringComparison.Ordinal));

        var result = await InvokeKoreanFullHdAsync(adb);

        result.Success.Should().BeFalse("a failed destructive command must not be reported as a success");
        result.Message.Should().Contain("cp -r ", "the failure must name the command that failed");
        result.Message.Should().NotContain("1080p", "the success wording must not be issued over a failed apply");
    }

    /// <summary>
    /// The ordering guarantee: the account backup is the first destructive phase,
    /// so a failed copy must abort before <c>pm clear</c> wipes the package — the
    /// restore step can never run against a backup that was never written.
    /// </summary>
    [Fact]
    public async Task FailedAccountBackup_NeverClearsThePackage_OrRestores()
    {
        var adb = new FailingFakeAdb(directories: [DataPath]);
        adb.FailWhen.Add(command => command.StartsWith("cp -r ", StringComparison.Ordinal));

        await InvokeKoreanFullHdAsync(adb);

        adb.Commands.Should().NotContain(command => command.StartsWith("pm clear ", StringComparison.Ordinal),
            "the package reset must be unreachable while the account backup is incomplete");
        adb.Commands.Should().NotContain(command => command.StartsWith("cp -r '/sdcard/mk_safe_folder/", StringComparison.Ordinal),
            "the account restore must never run after a failed account backup");
        adb.Commands.Should().NotContain(command => command.StartsWith("am start ", StringComparison.Ordinal),
            "the relaunch must not follow a half-applied sequence");
        adb.Commands.Should().Contain(command => command.StartsWith("cp -r ", StringComparison.Ordinal),
            "the test must have reached the account copy it is about");
    }

    /// <summary>
    /// The read-only probe rule: a probe that fails carries no information about
    /// the folder, so it is reported and its branch is skipped — it is never read
    /// as a confident "absent" and never authorizes the destructive clear below.
    /// Only the case where not one probe answered fails the apply, because then
    /// the device state is unknown and a wipe would run on a guess.
    /// </summary>
    [Fact]
    public async Task UnansweredProbes_FailTheApply_WithoutClearingThePackage()
    {
        var adb = new FailingFakeAdb(directories: [DataPath]);
        adb.FailWhen.Add(command => command.StartsWith("[ -d ", StringComparison.Ordinal));

        var result = await InvokeKoreanFullHdAsync(adb);

        result.Success.Should().BeFalse("an unverifiable device must not be treated as an empty one");
        result.Message.Should().Contain("Could not verify", "the failure must say what could not be established");
        result.Message.Should().Contain("the package reset was skipped", "the message must state the consequence of being unable to verify");
        adb.Commands.Should().NotContain(command => command.StartsWith("pm clear ", StringComparison.Ordinal),
            "no destructive command may run on an unverified path");
    }

    /// <summary>
    /// The partial-knowledge rule: the folder is confirmed present but one
    /// backup probe never answered, so neither the snapshot nor the wipe branch
    /// may run — skipping the backup while the clear proceeds would destroy data
    /// with no confirmed backup. This fails closed where the old sync shell (and
    /// a naive answered-only port) would have treated the unknown backup as
    /// absent, snapshotted over it, or wiped without one.
    /// </summary>
    [Fact]
    public async Task PartialBackupKnowledge_FailsTheApply_WithoutClearingThePackage()
    {
        var adb = new FailingFakeAdb(directories: [DataPath]);
        adb.FailWhen.Add(command =>
            command.StartsWith("[ -d ", StringComparison.Ordinal) &&
            command.Contains(".nexora-backup", StringComparison.Ordinal));

        var result = await InvokeKoreanFullHdAsync(adb);

        result.Success.Should().BeFalse("a backup decision made on partial knowledge must not authorize a wipe");
        result.Message.Should().Contain("Could not verify the backup state of", "the failure must name the unverifiable decision");
        adb.Commands.Should().NotContain(command => command.StartsWith("pm clear ", StringComparison.Ordinal),
            "no destructive command may run while the backup state is unknown");
        adb.Commands.Should().NotContain(command => command.StartsWith("mv ", StringComparison.Ordinal),
            "no snapshot may run while the backup state is unknown");
    }

    /// <summary>
    /// The mirror case: the folder's own existence probe never answered while
    /// its siblings did, so the folder may hold data the wipe must not touch
    /// unseen. Fails closed; the all-unanswered case still belongs to the
    /// nothing-verified gate, not to this branch.
    /// </summary>
    [Fact]
    public async Task UnansweredExistenceProbe_WithAnsweredSiblings_FailsTheApply_WithoutClearingThePackage()
    {
        var adb = new FailingFakeAdb(directories: [DataPath]);
        adb.FailWhen.Add(command =>
            command.StartsWith("[ -d ", StringComparison.Ordinal) &&
            command.Contains(DataPath + "' ]", StringComparison.Ordinal));

        var result = await InvokeKoreanFullHdAsync(adb);

        result.Success.Should().BeFalse("a folder whose existence is unknown must not be wiped");
        result.Message.Should().Contain($"Could not verify whether '{DataPath}' exists", "the failure must name the unverifiable folder");
        adb.Commands.Should().NotContain(command => command.StartsWith("pm clear ", StringComparison.Ordinal),
            "no destructive command may run on a folder that was never confirmed");
    }

    /// <summary>
    /// The success path still works: with every command succeeding and the
    /// probes answering, the full sequence runs and returns the 1080p success.
    /// </summary>
    [Fact]
    public async Task AllCommandsSucceed_ReturnsOk_WithTheFullSequenceApplied()
    {
        var adb = new FailingFakeAdb(directories: [DataPath]);

        var result = await InvokeKoreanFullHdAsync(adb);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("1080p");
        adb.Commands.Should().Contain(command => command.StartsWith("pm clear ", StringComparison.Ordinal),
            "the reset is the point of the sequence and must run on success");
        adb.Commands.Should().Contain($"mv '{DataPath}' '{DataPath}.nexora-backup'",
            "the folder backup is the first destructive step after the account copy");
        adb.Commands.Should().Contain($"mv '{DataPath}.nexora-backup' '{DataPath}'",
            "the folder restore must put the backup back");
        adb.Commands.Should().Contain(command => command.Contains("/sdcard/mk_safe_folder/databases", StringComparison.Ordinal),
            "the account restore is the last destructive step and must be reached");
        adb.Commands.Should().Contain(command => command.StartsWith("rm -r '/sdcard/mk_safe_folder", StringComparison.Ordinal),
            "the temporary backup is cleaned up on success");
    }

    /// <summary>
    /// The quoting end-to-end: a hostile package name flows through the whole
    /// Korean sequence and every interpolated word stays one quoted shell
    /// word — the injected command never becomes a word of its own, and the
    /// quote-aware fake still parses every path so the sequence succeeds.
    /// </summary>
    [Fact]
    public async Task HostilePackageName_CommandsKeepItInsideSingleQuotedWords()
    {
        const string hostilePackage = "com.evil; touch /sdcard/pwned";
        var hostileData = $"/sdcard/Android/data/{hostilePackage}";
        var adb = new FailingFakeAdb(directories: [hostileData]);

        var result = await InvokeKoreanFullHdAsync(adb, hostilePackage);

        result.Success.Should().BeTrue("quoting must not break the success path even for hostile input");
        adb.Commands.Should().Contain($"pm clear '{hostilePackage}'",
            "the package word is quoted, so the device shell reads one argument");
        adb.Commands.Should().Contain($"mv '{hostileData}' '{hostileData}.nexora-backup'",
            "derived paths are quoted as whole words too");
        foreach (var command in adb.Commands)
        {
            var words = ShellWords.Split(command);
            words.Should().NotContain("touch", "the injected payload must never become its own shell word");
            words.Should().NotContain(";", "the injected separator must never become its own shell word");
        }
    }

    /// <summary>
    /// Drives the real applier's Korean sequence via its private entry point, with
    /// the session holding the KR package and the bundled resolution asset
    /// present on the fake filesystem — the two preconditions the sequence checks
    /// before any device command runs.
    /// </summary>
    private static Task<OperationResult> InvokeKoreanFullHdAsync(FailingFakeAdb adb, string? packageName = null) =>
        InvokeKoreanFullHdWithPackageAsync(adb, packageName ?? KoreanPackage);

    private static async Task<OperationResult> InvokeKoreanFullHdWithPackageAsync(FailingFakeAdb adb, string packageName)
    {
        var fileSystem = new FakeFileSystem();
        var storage = new GameLoopWorkingStorage(fileSystem, FakeWorkRoot.Instance);
        fileSystem.Existing.Add(storage.KoreanResolutionAssetPath);

        var session = new GameLoopSession();
        session.LoadVersion([0x01, 0x02, 0x03], packageName);

        var applier = new GraphicsSettingsApplier(adb, storage, fileSystem, session);

        var method = typeof(GraphicsSettingsApplier).GetMethod("ApplyKoreanFullHdAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("the Korean 1080p sequence must keep its single private entry point");

        return await (Task<OperationResult>)method!.Invoke(applier, [CancellationToken.None])!;
    }

    /// <summary>
    /// A fake ADB that answers existence probes from an in-memory directory set,
    /// records every command for ordering assertions, and fails the commands a
    /// test lists. Failure is exit code 1 with stderr, the shape a real device
    /// returns and the shape the old sync shell discarded.
    /// </summary>
    private sealed class FailingFakeAdb(IEnumerable<string> directories) : IAdbClient
    {
        private readonly HashSet<string> _directories = new(directories, StringComparer.Ordinal);

        /// <summary>Predicates a test adds to force a command to fail device-side.</summary>
        public List<Func<string, bool>> FailWhen { get; } = new();

        /// <summary>The call log — this is what the ordering assertions read.</summary>
        public List<string> Commands { get; } = new();

        public string DeviceSerial => "emulator-5554";

        public void RefreshAdbPath()
        {
        }

        public ProcessResult Run(params string[] arguments) => new(0, string.Empty, string.Empty, false);

        public string Shell(string command)
        {
            Commands.Add(command);
            return string.Empty;
        }

        public string Shell(string command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Shell(command);
        }

        public Task<ProcessResult> ShellAsync(string command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);

            if (FailWhen.Any(fails => fails(command)))
            {
                return Task.FromResult(new ProcessResult(1, string.Empty, "adb: fake device-side failure", false));
            }

            if (command.StartsWith("[ -d ", StringComparison.Ordinal))
            {
                var words = ShellWords.Split(command);
                var path = words.Count >= 3 ? words[2] : string.Empty;
                return Task.FromResult(new ProcessResult(0, _directories.Contains(path) ? "1" : "0", string.Empty, false));
            }

            if (command.StartsWith("mv ", StringComparison.Ordinal))
            {
                var words = ShellWords.Split(command);
                if (words.Count >= 3 && _directories.Remove(words[1])) _directories.Add(words[2]);
                return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, false));
            }

            if (command.StartsWith("rm -r ", StringComparison.Ordinal))
            {
                var words = ShellWords.Split(command);
                if (words.Count >= 3) _directories.Remove(words[2]);
                return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, false));
            }

            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, false));
        }

        public Task<bool> PullAsync(string remotePath, string localPath, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(false);

        /// <summary>The sequence's entry condition is that the resolution asset lands.</summary>
        public Task<bool> PushAsync(string localPath, string remotePath, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(true);

        public Task<bool> WaitForBootAsync(CancellationToken cancellationToken, IProgress<string>? progress = null) => Task.FromResult(true);

        public IReadOnlyList<string> FindInstalledPackages(IEnumerable<string> packageNames, CancellationToken cancellationToken) => [];

        public Task<IReadOnlyList<string>> FindInstalledPackagesAsync(IEnumerable<string> packageNames, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public void StopAdb()
        {
        }

        public Task StopAdbAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>An in-memory filesystem that only answers what a test seeded.</summary>
    private sealed class FakeFileSystem : IFileSystem
    {
        public HashSet<string> Existing { get; } = new(StringComparer.Ordinal);

        private readonly Dictionary<string, byte[]> _bytes = new();

        public bool Exists(string path) => Existing.Contains(path);

        public byte[] ReadAllBytes(string path) => _bytes.TryGetValue(path, out var bytes) ? bytes : throw new FileNotFoundException(path);

        public void WriteAllBytes(string path, byte[] bytes) => _bytes[path] = bytes;

        public string ReadAllText(string path) => throw new NotImplementedException();

        public void WriteAllText(string path, string contents) => throw new NotImplementedException();

        public string[] ReadAllLines(string path) => throw new NotImplementedException();

        public void WriteAllLines(string path, IEnumerable<string> lines) => throw new NotImplementedException();

        public IEnumerable<string> ReadLines(string path) => throw new NotImplementedException();

        public void Copy(string sourceFileName, string destinationFileName, bool overwrite = false) => throw new NotImplementedException();

        public void Delete(string path) => Existing.Remove(path);

        public void CreateDirectory(string path)
        {
        }
    }

    /// <summary>
    /// Fixed roots so the seeded asset path is predictable without a hardcoded
    /// drive letter: the storage object derives the path, the test seeds that path.
    /// </summary>
    private sealed class FakeWorkRoot : IWorkRootProvider
    {
        public static readonly FakeWorkRoot Instance = new();

        public string AssetRoot => "assets";

        public string WorkRoot => "work";
    }
}
