using System.Diagnostics;
using FluentAssertions;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.Graphics.Application;
using Nexora.Features.Graphics.Domain;
using Nexora.Features.Graphics.Presentation;
using Nexora.Infrastructure.Processes;
using Nexora.Shared.Contracts;
using Nexora.UI.Presentation;
using Xunit;

namespace Nexora.Tests.Features.Graphics;

/// <summary>
/// Pins the connect/disconnect offload contract without an emulator: teardown
/// never blocks the caller, cancellation settles as a failed result, and
/// connect phases flow through the existing status event.
/// </summary>
public sealed class ConnectDisconnectOffloadTests
{
    [Fact]
    public async Task DisconnectAsync_WithHungTeardown_ReturnsPromptlyThenSettlesDisconnected()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var _ = safety.Token.Register(() => gate.TrySetResult(true));
        var adb = new HungFakeAdb(gate.Task);
        var vm = new GraphicsViewModel(
            new StubConnection(transportUp: true),
            new StubSettings(),
            adb,
            new PageOperationBus());

        var stopwatch = Stopwatch.StartNew();
        var disconnectTask = vm.DisconnectAsync();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(5),
            "teardown must run off the caller thread even when adb hangs");
        gate.TrySetResult(true);
        await disconnectTask;

        adb.StopAsyncCalls.Should().Be(1);
        vm.ConnectionState.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task ConnectAsync_CanceledConnect_SettlesAsFailedResult()
    {
        var vm = new GraphicsViewModel(
            new StubConnection(throwCanceled: true),
            new StubSettings(),
            new IdleFakeAdb(),
            new PageOperationBus());
        var states = new List<ConnectionState>();
        vm.ConnectionStateChanged += (state, _) => states.Add(state);

        await vm.ConnectAsync();

        states.Should().Equal(ConnectionState.Failed);
        vm.ConnectionState.Should().Be(ConnectionState.Failed);
        vm.InstalledVersions.Should().BeEmpty();
    }

    [Fact]
    public async Task ConnectAsync_ReportsPhasesThroughTheExistingStatusEvent()
    {
        var vm = new GraphicsViewModel(
            new StubConnection(reportPhases: true),
            new StubSettings(),
            new IdleFakeAdb(),
            new PageOperationBus());
        var statuses = new List<string>();
        vm.StatusChanged += (message, _) => statuses.Add(message);

        await vm.ConnectAsync();

        statuses.Should().Equal(
            "Connecting to GameLoop...",
            "phase-one",
            "phase-two");
    }

    private sealed class StubConnection(bool transportUp = false, bool throwCanceled = false, bool reportPhases = false) : IGameLoopConnection
    {
        public string? CurrentPackage => null;
        public bool IsAdbConnected => transportUp;
        public bool IsConnected => false;
        public int DisconnectCalls { get; private set; }
        public IProgress<string>? SeenProgress { get; private set; }

        public Task<ConnectionResult> ConnectAsync(CancellationToken cancellationToken, IProgress<string>? progress = null)
        {
            SeenProgress = progress;
            if (throwCanceled) throw new OperationCanceledException();
            if (reportPhases)
            {
                progress?.Report("phase-one");
                progress?.Report("phase-two");
            }

            return Task.FromResult(new ConnectionResult(false, "No emulator.", Array.Empty<PubgVersion>()));
        }

        public Task<OperationResult> LoadVersionAsync(string packageName, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Fail("Not connected."));

        public void Disconnect() => DisconnectCalls++;
    }

    private sealed class StubSettings : IGraphicsSettingsService
    {
        public GraphicsCurrentSettings Current { get; } =
            new("HDR", "Extreme", "Movie", ShadowEnabled: false, IsKoreanVersion: false);

        public Task<GraphicsCurrentSettings?> LoadCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<GraphicsCurrentSettings?>(Current);

        public Task<OperationResult> ApplyAsync(GraphicsSelection selection, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok("Settings applied."));
    }

    private sealed class HungFakeAdb(Task gate) : IAdbClient
    {
        public int StopAsyncCalls { get; private set; }

        public string DeviceSerial => "emulator-5554";

        public void RefreshAdbPath()
        {
        }

        public ProcessResult Run(params string[] arguments) => new(0, "", "", false);

        public string Shell(string command) => string.Empty;

        public string Shell(string command, CancellationToken cancellationToken) => string.Empty;

        public Task<ProcessResult> ShellAsync(string command, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, false));

        public Task<bool> PullAsync(string remotePath, string localPath, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(false);

        public Task<bool> PushAsync(string localPath, string remotePath, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(false);

        public Task<bool> WaitForBootAsync(CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(false);

        public IReadOnlyList<string> FindInstalledPackages(IEnumerable<string> packageNames, CancellationToken cancellationToken) => [];

        public Task<IReadOnlyList<string>> FindInstalledPackagesAsync(IEnumerable<string> packageNames, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public void StopAdb()
        {
        }

        public async Task StopAdbAsync(CancellationToken cancellationToken)
        {
            StopAsyncCalls++;
            await gate;
        }
    }

    private sealed class IdleFakeAdb : IAdbClient
    {
        public string DeviceSerial => "emulator-5554";

        public void RefreshAdbPath()
        {
        }

        public ProcessResult Run(params string[] arguments) => new(0, "", "", false);

        public string Shell(string command) => string.Empty;

        public string Shell(string command, CancellationToken cancellationToken) => string.Empty;

        public Task<ProcessResult> ShellAsync(string command, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, false));

        public Task<bool> PullAsync(string remotePath, string localPath, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(false);

        public Task<bool> PushAsync(string localPath, string remotePath, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(false);

        public Task<bool> WaitForBootAsync(CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(false);

        public IReadOnlyList<string> FindInstalledPackages(IEnumerable<string> packageNames, CancellationToken cancellationToken) => [];

        public Task<IReadOnlyList<string>> FindInstalledPackagesAsync(IEnumerable<string> packageNames, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public void StopAdb()
        {
        }

        public Task StopAdbAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
