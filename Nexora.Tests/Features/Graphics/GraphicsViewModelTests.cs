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
/// The ViewModel is the page's single gate for what the connection enables and
/// for the Korean 1080p workflow — logic that used to be inline in three places
/// in the shell — so both contracts are pinned here rather than in the view.
/// </summary>
public sealed class GraphicsViewModelTests
{
    private static readonly GraphicsSelection Selection =
        new("Smooth", "Low", "Classic", EnableShadow: false, EnableKoreanFullHd: false);

    [Theory]
    [InlineData("com.pubg.krmobile", true)]
    [InlineData("COM.PUBG.KRMOBILE", true)]
    [InlineData("com.tencent.ig", false)]
    [InlineData(null, false)]
    public void IsKoreanVersion_GatesOnTheLoadedPackageOnly(string? currentPackage, bool expected)
    {
        var connection = new FakeConnection { CurrentPackage = currentPackage };
        var vm = Build(connection);

        vm.IsKoreanVersion.Should().Be(expected);
    }

    [Fact]
    public void IsVersionLoaded_TracksTheConnectionNotTheTransport()
    {
        var connection = new FakeConnection(transportUp: true);
        var vm = Build(connection);

        vm.IsVersionLoaded.Should().BeFalse("a transport alone does not make apply available");

        connection.IsConnected = true;

        vm.IsVersionLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task ConnectAsync_FullyConnected_ReadsSettingsAndRaisesTheState()
    {
        var connection = new FakeConnection();
        var settings = new FakeSettings();
        var vm = Build(connection, settings);

        var states = new List<ConnectionState>();
        GraphicsCurrentSettings? loaded = null;
        vm.ConnectionStateChanged += (state, _) => states.Add(state);
        vm.SettingsLoaded += current => loaded = current;

        await vm.ConnectAsync();

        states.Should().Equal(ConnectionState.FullyConnected);
        loaded.Should().BeSameAs(settings.Current);
        vm.InstalledVersions.Should().BeSameAs(connection.InstalledVersions);
    }

    [Fact]
    public async Task ConnectAsync_TransportOnly_NeverReadsSettings()
    {
        var connection = new FakeConnection { VersionLoadedAfterConnect = false };
        var settings = new FakeSettings();
        var vm = Build(connection, settings);

        var loaded = false;
        vm.SettingsLoaded += _ => loaded = true;

        await vm.ConnectAsync();

        vm.ConnectionState.Should().Be(ConnectionState.TransportConnected);
        loaded.Should().BeFalse("a transport without a loaded version has no profile to read");
    }

    [Fact]
    public async Task ConnectAsync_SuccessWithoutATransport_RaisesAwaitingVersion()
    {
        var vm = Build(new FakeConnection { TransportAfterConnect = false });

        await vm.ConnectAsync();

        vm.ConnectionState.Should().Be(ConnectionState.AwaitingVersion);
    }

    [Fact]
    public async Task ConnectAsync_Failed_RaisesFailedAndKeepsTheLastSettings()
    {
        var connection = new FakeConnection
        {
            ConnectResult = new ConnectionResult(false, "No emulator.", Array.Empty<PubgVersion>()),
        };
        var vm = Build(connection);

        var states = new List<ConnectionState>();
        vm.ConnectionStateChanged += (state, _) => states.Add(state);

        await vm.ConnectAsync();

        states.Should().Equal(ConnectionState.Failed);
        vm.InstalledVersions.Should().BeEmpty();
    }

    [Fact]
    public async Task ConnectAsync_OnAdbConnectedConnection_TearsItDownInsteadOfReconnecting()
    {
        var connection = new FakeConnection(transportUp: true);
        var adb = new FakeAdbClient();
        var vm = Build(connection, adbClient: adb);

        await vm.ConnectAsync();

        connection.ConnectCalls.Should().Be(0, "an already-up transport is disconnected, not reconnected");
        connection.DisconnectCalls.Should().Be(1);
        adb.StopCalls.Should().Be(1);
        vm.ConnectionState.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task ConnectAsync_RefusesWhileAnotherOperationOwnsTheBus()
    {
        var bus = new PageOperationBus();
        bus.TryAcquire();
        var connection = new FakeConnection(transportUp: true);
        var vm = Build(connection, operationBus: bus);

        await vm.ConnectAsync();

        connection.ConnectCalls.Should().Be(0, "a busy bus must never let a second operation start");
    }

    [Fact]
    public async Task ApplyAsync_RefusesWhileAnotherOperationOwnsTheBus()
    {
        var bus = new PageOperationBus();
        bus.TryAcquire();
        var settings = new FakeSettings();
        var vm = Build(settings: settings, operationBus: bus);

        var result = await vm.ApplyAsync(Selection);

        result.Success.Should().BeFalse();
        settings.LastApplied.Should().BeNull("a refused apply must never reach the profile store");
    }

    [Fact]
    public async Task ApplyAsync_ReleasesTheBusForTheNextOperation()
    {
        var vm = Build();

        (await vm.ApplyAsync(Selection)).Success.Should().BeTrue();
        (await vm.ApplyAsync(Selection)).Success.Should().BeTrue("the bus is reusable after each apply");
    }

    [Fact]
    public async Task LoadVersionAsync_AlreadyLoaded_IsANoOp()
    {
        var connection = new FakeConnection(transportUp: true) { IsConnected = true };
        var vm = Build(connection);

        await vm.LoadVersionAsync(new PubgVersion("com.tencent.ig", "PUBG Mobile"));

        connection.LoadCalls.Should().Be(0, "reloading the version already loaded is a silent no-op");
    }

    [Fact]
    public async Task LoadVersionAsync_ReleasesTheBusEvenWhenTheLoadFails()
    {
        var connection = new FakeConnection(transportUp: true)
        {
            LoadResult = OperationResult.Fail("Package not found."),
        };
        var bus = new PageOperationBus();
        var vm = Build(connection, operationBus: bus);

        await vm.LoadVersionAsync(new PubgVersion("com.tencent.ig", "PUBG Mobile"));

        bus.IsBusy.Should().BeFalse("a failed load must not hold the window-wide guard");
        vm.ConnectionState.Should().Be(ConnectionState.Disconnected);
    }

    /// <summary>
    /// The store throws on cancellation and the ViewModel must render that as a
    /// skip instead of letting it escape the page (AdbCancellationTests pins
    /// the store half of this contract).
    /// </summary>
    [Fact]
    public async Task ApplyAsync_TranslatesCancellationIntoASkip()
    {
        var settings = new FakeSettings(throwOnApply: true);
        var vm = Build(settings: settings);

        var result = await vm.ApplyAsync(Selection);

        result.Success.Should().BeTrue();
        result.IsSkipped.Should().BeTrue();
        result.Outcome.Should().Be(StepOutcome.Skipped);
        result.Message.Should().Be("Graphics application was canceled.");
    }

    [Fact]
    public void Cancel_TearsDownAnInFlightConnection()
    {
        var connection = new FakeConnection(transportUp: true);
        var vm = Build(connection);

        vm.Cancel();

        connection.DisconnectCalls.Should().Be(0, "Cancel stops the token without tearing down the transport");
    }

    private static GraphicsViewModel Build(
        FakeConnection? connection = null,
        FakeSettings? settings = null,
        FakeAdbClient? adbClient = null,
        PageOperationBus? operationBus = null) =>
        new(connection ?? new FakeConnection(transportUp: true),
            settings ?? new FakeSettings(),
            adbClient ?? new FakeAdbClient(),
            operationBus ?? new PageOperationBus());

    /// <summary>
    /// Models the connection the way the real GameLoopService does: a connect
    /// is what raises the transport, and a transport plus a loaded version is
    /// what makes the profile readable. The pre-connect transport state is what
    /// separates "connect" from "disconnect" in the single-button behavior.
    /// </summary>
    private sealed class FakeConnection : IGameLoopConnection
    {
        private bool _transportUp;

        public FakeConnection(bool transportUp = false) => _transportUp = transportUp;

        public string? CurrentPackage { get; set; }
        public bool IsAdbConnected => _transportUp;
        public bool IsConnected { get; set; }
        public ConnectionResult ConnectResult { get; set; } =
            new(true, "Connected.", new[] { new PubgVersion("com.tencent.ig", "PUBG Mobile") });
        public bool TransportAfterConnect { get; set; } = true;
        public bool VersionLoadedAfterConnect { get; set; } = true;
        public IReadOnlyList<PubgVersion> InstalledVersions => ConnectResult.InstalledVersions;
        public int ConnectCalls { get; private set; }
        public int DisconnectCalls { get; private set; }
        public int LoadCalls { get; private set; }
        public OperationResult LoadResult { get; set; } = OperationResult.Ok("Loaded.");

        public Task<ConnectionResult> ConnectAsync(CancellationToken cancellationToken, IProgress<string>? progress = null)
        {
            ConnectCalls++;
            if (ConnectResult.Success)
            {
                _transportUp = TransportAfterConnect;
                IsConnected = TransportAfterConnect && VersionLoadedAfterConnect;
            }
            return Task.FromResult(ConnectResult);
        }

        public Task<OperationResult> LoadVersionAsync(string packageName, CancellationToken cancellationToken)
        {
            LoadCalls++;
            return Task.FromResult(LoadResult);
        }

        public void Disconnect() => DisconnectCalls++;
    }

    private sealed class FakeSettings : IGraphicsSettingsService
    {
        private readonly bool _throwOnApply;

        public FakeSettings(bool throwOnApply = false)
        {
            _throwOnApply = throwOnApply;
        }

        public GraphicsCurrentSettings Current { get; } = new("HDR", "Extreme", "Movie", ShadowEnabled: true, IsKoreanVersion: false);
        public GraphicsSelection? LastApplied { get; private set; }
        public OperationResult ApplyOutcome { get; set; } = OperationResult.Ok("Settings applied.");

        public Task<GraphicsCurrentSettings?> LoadCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<GraphicsCurrentSettings?>(Current);

        public Task<OperationResult> ApplyAsync(GraphicsSelection selection, CancellationToken cancellationToken = default)
        {
            if (_throwOnApply) throw new OperationCanceledException();
            LastApplied = selection;
            return Task.FromResult(ApplyOutcome);
        }
    }

    private sealed class FakeAdbClient : IAdbClient
    {
        public int StopCalls { get; private set; }

        public string DeviceSerial => "emulator-5554";

        public void RefreshAdbPath()
        {
        }

        public ProcessResult Run(params string[] arguments) => new(0, "", "", false);

        public string Shell(string command) => string.Empty;

        public string Shell(string command, CancellationToken cancellationToken) => string.Empty;

        public Task<bool> PullAsync(string remotePath, string localPath, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(true);

        public Task<bool> PushAsync(string localPath, string remotePath, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(false);

        public Task<bool> WaitForBootAsync(CancellationToken cancellationToken, IProgress<string>? progress = null) => Task.FromResult(true);

        public IReadOnlyList<string> FindInstalledPackages(IEnumerable<string> packageNames, CancellationToken cancellationToken) => [];

        public Task<IReadOnlyList<string>> FindInstalledPackagesAsync(IEnumerable<string> packageNames, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(FindInstalledPackages(packageNames, cancellationToken));

        public void StopAdb() => StopCalls++;

        public Task StopAdbAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            return Task.CompletedTask;
        }
    }
}