using System.Text;
using FluentAssertions;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.GameLoop.Infrastructure;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;
using Nexora.Features.Graphics.Domain;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;
using Nexora.Infrastructure.GameLoop;
using Nexora.Infrastructure.Files;
using Nexora.Shared.Contracts;

namespace Nexora.Tests.Features.GameLoop;

/// <summary>
/// Proves the <see cref="GameLoopService"/> operation gate serializes
/// overlapping session writers. A <see cref="TaskCompletionSource"/>-gated
/// fake ADB forces the exact overlaps that used to publish mismatched
/// (buffer, package) pairs and cross-contaminate save buffers. Working files
/// live in isolated temp directories; the registry and real ADB are untouched.
/// </summary>
public sealed class GameLoopSessionConcurrencyTests
{
    private const string PackageA = "com.tencent.ig";
    private const string PackageB = "com.vng.pubgmobile";

    [Fact]
    public async Task ConcurrentLoadVersionAsync_SerializesWriters_AndKeepsSessionPairSelfConsistent()
    {
        using var roots = new TempWorkRoots();
        var adb = new GatedFakeAdb();
        var service = CreateService(adb, roots);
        var pullRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adb.PullGate = pullRelease.Task;

        Task<OperationResult>? loadA = null;
        Task<OperationResult>? loadB = null;
        try
        {
            loadA = service.LoadVersionAsync(PackageA, CancellationToken.None);
            // Wait until A is parked inside the pull, then start B so the two
            // calls genuinely overlap in time.
            await WaitUntilAsync(() => adb.PullsInFlight > 0);
            loadB = service.LoadVersionAsync(PackageB, CancellationToken.None);

            await Task.Delay(TimeSpan.FromMilliseconds(300));
            // B must be parked at the facade gate, not inside a second pull.
            loadB.IsCompleted.Should().BeFalse("overlapping LoadVersion calls must serialize");

            pullRelease.TrySetResult();
            var results = await Task.WhenAll(loadA, loadB);
            results.Should().OnlyContain(result => result.Success);
        }
        finally
        {
            pullRelease.TrySetResult();
            if (loadA is not null) await loadA;
            if (loadB is not null) await loadB;
        }

        // Fully serialized: the pulls never overlapped, and the starter that
        // ran second owns both session fields — never a torn (B-buffer,
        // A-package) pair.
        adb.MaxConcurrentPulls.Should().Be(1);
        service.CurrentPackage.Should().Be(PackageB);
        service.GetGraphicsQuality().Should().Be("Balanced");
    }

    [Fact]
    public async Task ApplyGraphicsAsync_SerializesAgainstConcurrentLoadVersion_WithoutCrossContamination()
    {
        using var roots = new TempWorkRoots();
        var adb = new GatedFakeAdb();
        var service = CreateService(adb, roots);

        var seed = await service.LoadVersionAsync(PackageA, CancellationToken.None);
        seed.Success.Should().BeTrue();
        service.CurrentPackage.Should().Be(PackageA);

        var selection = new GraphicsSelection("Smooth", "Low", "Classic", EnableShadow: false, EnableKoreanFullHd: false);
        var pushRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adb.PushGate = pushRelease.Task;

        Task<OperationResult>? apply = null;
        Task<OperationResult>? loadB = null;
        try
        {
            apply = service.ApplyGraphicsAsync(selection, CancellationToken.None);
            // Wait until Apply has edited the session buffer in place and
            // parked mid-deploy, then start the overlapping LoadVersion.
            await WaitUntilAsync(() => adb.PushesInFlight > 0);
            loadB = service.LoadVersionAsync(PackageB, CancellationToken.None);

            await Task.Delay(TimeSpan.FromMilliseconds(300));
            loadB.IsCompleted.Should().BeFalse("LoadVersion must wait for the in-flight Apply to finish");
            // B has not clobbered anything: A's pair is intact with A's edits.
            service.CurrentPackage.Should().Be(PackageA);
            service.GetGraphicsQuality().Should().Be("Smooth");

            pushRelease.TrySetResult();
            var results = await Task.WhenAll(apply, loadB);
            results.Should().OnlyContain(result => result.Success);
        }
        finally
        {
            pushRelease.TrySetResult();
            if (apply is not null) await apply;
            if (loadB is not null) await loadB;
        }

        // B ran after Apply completed: fresh B bytes under the B package, so
        // A's in-place edits never leaked into B's buffer.
        service.CurrentPackage.Should().Be(PackageB);
        service.GetGraphicsQuality().Should().Be("Balanced");

        // The deploy carried A's edited buffer, not a contaminated mix: the
        // A tag survived and the quality byte holds the applied selection.
        var tag = Encoding.ASCII.GetBytes($"PKG:{PackageA};");
        adb.PushedSavBytes.Length.Should().BeGreaterThan(tag.Length);
        adb.PushedSavBytes[..tag.Length].Should().Equal(tag);
        new Ue4SavEditor(adb.PushedSavBytes).ReadProperty("BattleRenderQuality").Should().Be(0x01);
    }

    private static GameLoopService CreateService(GatedFakeAdb adb, TempWorkRoots roots) =>
        new(
            new RegistryService(),
            adb,
            new GameLoopWorkingStorage(new PhysicalFileSystem(), roots),
            new PhysicalFileSystem(),
            new GameLoopProcessService(new ProcessRunner(), new GameLoopPathResolver(new RegistryService())));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Timed out waiting for the overlapped operation to start.");
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }

    private static byte[] SavSegment(string propertyName, byte value) =>
        Ue4SavEditor.CreateHeader(propertyName).Concat(new[] { value }).ToArray();

    private sealed class TempWorkRoots : IWorkRootProvider, IDisposable
    {
        public TempWorkRoots()
        {
            Directory.CreateDirectory(AssetRoot);
            Directory.CreateDirectory(WorkRoot);
        }

        public string AssetRoot { get; } = Path.Combine(Path.GetTempPath(), "NexoraGateTest_Assets_" + Guid.NewGuid().ToString("N"));

        public string WorkRoot { get; } = Path.Combine(Path.GetTempPath(), "NexoraGateTest_Work_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try
            {
                Directory.Delete(AssetRoot, recursive: true);
            }
            catch { }

            try
            {
                Directory.Delete(WorkRoot, recursive: true);
            }
            catch { }
        }
    }

    /// <summary>
    /// Fake ADB that tags every pulled save buffer with its package so a
    /// mismatched (buffer, package) session pair is directly observable, and
    /// parks pulls/pushes on test-controlled gates to force real overlaps.
    /// </summary>
    private sealed class GatedFakeAdb : IAdbClient
    {
        private readonly object _sync = new();
        private int _pullsInFlight;
        private int _pushesInFlight;

        public Task? PullGate { get; set; }

        public Task? PushGate { get; set; }

        public int PullsInFlight
        {
            get
            {
                lock (_sync) return _pullsInFlight;
            }
        }

        public int PushesInFlight
        {
            get
            {
                lock (_sync) return _pushesInFlight;
            }
        }

        public int MaxConcurrentPulls { get; private set; }

        public byte[] PushedSavBytes { get; private set; } = [];

        public string DeviceSerial => "emulator-5554";

        public void RefreshAdbPath()
        {
        }

        public ProcessResult Run(params string[] arguments) => new(0, "", "", false);

        public string Shell(string command) => "";

        public string Shell(string command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Shell(command);
        }

        public Task<ProcessResult> ShellAsync(string command, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessResult(0, Shell(command, cancellationToken), string.Empty, false));

        public Task<bool> WaitForBootAsync(CancellationToken cancellationToken, IProgress<string>? progress = null) => Task.FromResult(true);

        public IReadOnlyList<string> FindInstalledPackages(IEnumerable<string> packageNames, CancellationToken cancellationToken) =>
            packageNames.ToList();

        public Task<IReadOnlyList<string>> FindInstalledPackagesAsync(IEnumerable<string> packageNames, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(FindInstalledPackages(packageNames, cancellationToken));

        public void StopAdb()
        {
        }

        public Task StopAdbAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<bool> PullAsync(string remotePath, string localPath, CancellationToken cancellationToken, IProgress<string>? progress = null)
        {
            lock (_sync)
            {
                _pullsInFlight++;
                MaxConcurrentPulls = Math.Max(MaxConcurrentPulls, _pullsInFlight);
            }

            try
            {
                if (PullGate is not null) await PullGate.WaitAsync(cancellationToken);

                if (remotePath.Contains("/SaveGames/Active.sav", StringComparison.Ordinal))
                {
                    File.WriteAllBytes(localPath, TaggedSav(ExtractPackage(remotePath)));
                }
                else if (remotePath.EndsWith("UserCustom.ini", StringComparison.Ordinal))
                {
                    File.WriteAllLines(localPath, ["[ShadowTrackerExtra]", "+CVars=" + UnrealCVarCodec.EncodeCVar("r.ShadowQuality", "0")]);
                }

                return true;
            }
            finally
            {
                lock (_sync) _pullsInFlight--;
            }
        }

        public async Task<bool> PushAsync(string localPath, string remotePath, CancellationToken cancellationToken, IProgress<string>? progress = null)
        {
            lock (_sync) _pushesInFlight++;

            try
            {
                if (PushGate is not null) await PushGate.WaitAsync(cancellationToken);

                if (remotePath.EndsWith("/SaveGames/Active.sav", StringComparison.Ordinal) && File.Exists(localPath))
                {
                    PushedSavBytes = File.ReadAllBytes(localPath);
                }

                return true;
            }
            finally
            {
                lock (_sync) _pushesInFlight--;
            }
        }

        private static string ExtractPackage(string remotePath)
        {
            // "/sdcard/Android/data/{package}/files/..." — the segment after "data".
            var segments = remotePath.Split('/');
            var dataIndex = Array.IndexOf(segments, "data");
            return dataIndex >= 0 && dataIndex + 1 < segments.Length ? segments[dataIndex + 1] : "";
        }

        private static byte[] TaggedSav(string package)
        {
            // Package tag first so buffer/package mismatches are observable;
            // all seven mutable properties present so Apply always succeeds.
            var (quality, fps, style) = package == PackageA
                ? ((byte)0x03, (byte)0x04, (byte)0x02)
                : ((byte)0x02, (byte)0x03, (byte)0x03);
            return Encoding.ASCII.GetBytes($"PKG:{package};")
                .Concat(SavSegment("ArtQuality", quality))
                .Concat(SavSegment("LobbyRenderQuality", quality))
                .Concat(SavSegment("BattleRenderQuality", quality))
                .Concat(SavSegment("FPSLevel", fps))
                .Concat(SavSegment("BattleFPS", fps))
                .Concat(SavSegment("LobbyFPS", fps))
                .Concat(SavSegment("BattleRenderStyle", style))
                .ToArray();
        }
    }
}
