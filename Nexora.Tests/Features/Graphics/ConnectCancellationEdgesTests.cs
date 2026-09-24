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
/// Pins the two cancellation edges without an emulator: a cancel landing
/// mid-connect settles as a failed result (never a throw, never a paint of
/// half-loaded data), and a disconnect landing between connect success and
/// the profile paint leaves the Disconnected state standing with stale data
/// off the tree.
/// </summary>
public sealed class ConnectCancellationEdgesTests
{
    [Fact]
    public async Task ConnectAsync_CanceledMidFlight_SettlesFailedWithoutPainting()
    {
        var gate = new TaskCompletionSource<ConnectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new GraphicsViewModel(
            new GatedConnection(gate.Task),
            new StubSettings(),
            new IdleFakeAdb(),
            new PageOperationBus());
        var states = new List<(ConnectionState, string)>();
        var settingsLoaded = false;
        vm.ConnectionStateChanged += (state, message) => states.Add((state, message));
        vm.SettingsLoaded += _ => settingsLoaded = true;

        var connectTask = vm.ConnectAsync();
        vm.Cancel();
        gate.TrySetResult(new ConnectionResult(true, "Connected.", new[] { new PubgVersion("com.tencent.ig", "PUBG Mobile") }));
        await connectTask;

        states.Should().Equal((ConnectionState.Failed, "Connection canceled."));
        settingsLoaded.Should().BeFalse("a canceled connect must never paint a profile");
        vm.InstalledVersions.Should().BeEmpty();
    }

    [Fact]
    public async Task ConnectAsync_DisconnectInTheGap_LeavesDisconnectedStanding()
    {
        var enteredLoad = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadGate = new TaskCompletionSource<GraphicsCurrentSettings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new GapConnection();
        var vm = new GraphicsViewModel(
            connection,
            new GatedSettings(enteredLoad, loadGate.Task),
            new IdleFakeAdb(),
            new PageOperationBus());
        var states = new List<ConnectionState>();
        var settingsLoaded = false;
        vm.ConnectionStateChanged += (state, _) => states.Add(state);
        vm.SettingsLoaded += _ => settingsLoaded = true;

        var connectTask = vm.ConnectAsync();
        await enteredLoad.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await vm.DisconnectAsync();
        loadGate.TrySetResult(new GraphicsCurrentSettings("Smooth", "Low", "Classic", ShadowEnabled: false, IsKoreanVersion: false));
        await connectTask;

        settingsLoaded.Should().BeFalse("stale profile data must never reach the tree after a disconnect");
        states.Should().Equal(ConnectionState.Disconnected);
        vm.ConnectionState.Should().Be(ConnectionState.Disconnected);
    }

    private sealed class GatedConnection(Task<ConnectionResult> gate) : IGameLoopConnection
    {
        public string? CurrentPackage => null;
        public bool IsAdbConnected => false;
        public bool IsConnected => false;

        public async Task<ConnectionResult> ConnectAsync(CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            await gate.WaitAsync(cancellationToken);

        public Task<OperationResult> LoadVersionAsync(string packageName, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Fail("Not connected."));

        public void Disconnect()
        {
        }
    }

    private sealed class GapConnection : IGameLoopConnection
    {
        private bool _transportUp;

        public string? CurrentPackage => null;
        public bool IsAdbConnected => _transportUp;
        public bool IsConnected { get; set; } = true;

        public Task<ConnectionResult> ConnectAsync(CancellationToken cancellationToken, IProgress<string>? progress = null)
        {
            _transportUp = true;
            return Task.FromResult(new ConnectionResult(
                true,
                "Connected.",
                new[] { new PubgVersion("com.tencent.ig", "PUBG Mobile") }));
        }

        public Task<OperationResult> LoadVersionAsync(string packageName, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Ok("Loaded."));

        public void Disconnect()
        {
            _transportUp = false;
            IsConnected = false;
        }
    }

    private sealed class StubSettings : IGraphicsSettingsService
    {
        public Task<GraphicsCurrentSettings?> LoadCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<GraphicsCurrentSettings?>(
                new("HDR", "Extreme", "Movie", ShadowEnabled: false, IsKoreanVersion: false));

        public Task<OperationResult> ApplyAsync(GraphicsSelection selection, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok("Settings applied."));
    }

    /// <summary>
    /// Models an engine op that cannot preempt: the token is observed but the
    /// gated load still completes, so only the ViewModel's liveness re-check
    /// can keep the stale data off the tree.
    /// </summary>
    private sealed class GatedSettings(TaskCompletionSource<bool> entered, Task<GraphicsCurrentSettings?> gate) : IGraphicsSettingsService
    {
        public async Task<GraphicsCurrentSettings?> LoadCurrentAsync(CancellationToken cancellationToken = default)
        {
            entered.TrySetResult(true);
            return await gate;
        }

        public Task<OperationResult> ApplyAsync(GraphicsSelection selection, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok("Settings applied."));
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
