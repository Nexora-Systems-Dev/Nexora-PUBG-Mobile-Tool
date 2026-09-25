using FluentAssertions;
using Nexora.Features.Network.Application;
using Nexora.Features.Network.Domain;
using Nexora.Features.Network.Presentation;
using Nexora.Shared.Contracts;
using Nexora.UI.Presentation;
using Xunit;

namespace Nexora.Tests.Features.Network;

/// <summary>
/// The ViewModel is the Network page's single gate for DNS probing/applying
/// and iPad apply/reset — logic that used to live inline in the shell — so
/// those contracts are pinned here rather than in the view. The iPad
/// running-state guard itself stays in the service and is only surfaced.
/// </summary>
public sealed class NetworkViewModelTests
{
    [Fact]
    public void FindPreset_KnownDisplayName_ReturnsThePreset()
    {
        var expected = IpadPresetCatalog.Presets[0];

        NetworkViewModel.FindPreset(expected.DisplayName).Should().BeSameAs(expected);
    }

    [Fact]
    public void FindPreset_UnknownOrNull_ReturnsNull()
    {
        NetworkViewModel.FindPreset("No such preset").Should().BeNull();
        NetworkViewModel.FindPreset(null).Should().BeNull();
    }

    [Fact]
    public async Task ProbeDnsAsync_ReturnsPingForTheSelectedProvider()
    {
        var network = new FakeNetworkTools { PingMs = 12 };
        var vm = Build(networkTools: network);
        var label = DnsCatalog.Labels[0];

        var probe = await vm.ProbeDnsAsync(label, () => label);

        probe.Should().NotBeNull();
        probe!.Entry.Label.Should().Be(label);
        probe.PingMs.Should().Be(12);
    }

    [Fact]
    public async Task ProbeDnsAsync_NoResponse_ReturnsNullPing()
    {
        var network = new FakeNetworkTools { PingMs = null };
        var vm = Build(networkTools: network);
        var label = DnsCatalog.Labels[1];

        var probe = await vm.ProbeDnsAsync(label, () => label);

        probe.Should().NotBeNull();
        probe!.PingMs.Should().BeNull();
    }

    [Fact]
    public async Task ProbeDnsAsync_SelectionMovedOn_DiscardsTheStaleResult()
    {
        var network = new FakeNetworkTools { PingMs = 12 };
        var vm = Build(networkTools: network);

        var probe = await vm.ProbeDnsAsync(DnsCatalog.Labels[0], () => DnsCatalog.Labels[1]);

        probe.Should().BeNull("a stale answer must never overwrite the newly selected provider");
    }

    [Fact]
    public async Task ProbeDnsAsync_UnknownLabel_ReturnsNullWithoutPinging()
    {
        var network = new FakeNetworkTools { PingMs = 12 };
        var vm = Build(networkTools: network);

        var probe = await vm.ProbeDnsAsync("No such provider", () => "No such provider");

        probe.Should().BeNull();
        network.PingCalls.Should().Be(0);
    }

    [Fact]
    public async Task ApplyDnsAsync_RoutesThroughTheServiceAndReportsItsMessage()
    {
        var network = new FakeNetworkTools();
        var vm = Build(networkTools: network);
        var label = DnsCatalog.Labels[0];
        var statuses = new List<(string, bool)>();
        vm.StatusChanged += (message, isError) => statuses.Add((message, isError));

        var result = await vm.ApplyDnsAsync(label);

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        network.LastChanged.Should().Be(DnsCatalog.Entries[0].Primary);
        statuses.Should().ContainSingle().Which.Should().Be((result.Message, false));
    }

    [Fact]
    public async Task ApplyDnsAsync_UnknownLabel_ReturnsNullWithoutTouchingTheBus()
    {
        var bus = new PageOperationBus();
        var vm = Build(operationBus: bus);

        var result = await vm.ApplyDnsAsync("No such provider");

        result.Should().BeNull();
        bus.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyIpadAsync_DelegatesDimensionsToTheService()
    {
        var ipad = new FakeIpadLayout();
        var vm = Build(ipadLayout: ipad);
        var preset = IpadPresetCatalog.Presets[2];

        var result = await vm.ApplyIpadAsync(preset);

        result.Success.Should().BeTrue();
        ipad.LastApplied.Should().Be((preset.Width, preset.Height));
    }

    [Fact]
    public async Task ApplyIpadAsync_ServiceRefusal_IsRenderedVerbatim()
    {
        var ipad = new FakeIpadLayout
        {
            ApplyOutcome = OperationResult.Fail("Close GameLoop before applying the iPad view."),
        };
        var vm = Build(ipadLayout: ipad);
        var statuses = new List<(string, bool)>();
        vm.StatusChanged += (message, isError) => statuses.Add((message, isError));

        var result = await vm.ApplyIpadAsync(IpadPresetCatalog.Presets[0]);

        result.Success.Should().BeFalse();
        statuses.Should().ContainSingle().Which.Should().Be((result.Message, true));
    }

    [Fact]
    public async Task ResetIpadAsync_RestoresThroughTheService()
    {
        var ipad = new FakeIpadLayout();
        var vm = Build(ipadLayout: ipad);

        var result = await vm.ResetIpadAsync();

        result.Success.Should().BeTrue();
        ipad.ResetCalls.Should().Be(1);
    }

    [Fact]
    public async Task ApplyDnsAsync_Cancellation_IsTranslatedToASkippedResult()
    {
        var network = new FakeNetworkTools { ThrowOnChange = new OperationCanceledException() };
        var vm = Build(networkTools: network);
        var statuses = new List<(string, bool)>();
        vm.StatusChanged += (message, isError) => statuses.Add((message, isError));

        var result = await vm.ApplyDnsAsync(DnsCatalog.Labels[0]);

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.IsSkipped.Should().BeTrue();
        result.Outcome.Should().Be(StepOutcome.Skipped);
        result.Message.Should().Be("Operation canceled.");
        statuses.Should().Contain((result.Message, false));
        network.ChangeCalls.Should().Be(1);
    }

    [Fact]
    public async Task ResetIpadAsync_Cancellation_IsTranslatedToASkippedResult()
    {
        var ipad = new FakeIpadLayout { ThrowOnReset = new OperationCanceledException() };
        var vm = Build(ipadLayout: ipad);
        var statuses = new List<(string, bool)>();
        vm.StatusChanged += (message, isError) => statuses.Add((message, isError));

        var result = await vm.ResetIpadAsync();

        result.Success.Should().BeTrue();
        result.IsSkipped.Should().BeTrue();
        result.Outcome.Should().Be(StepOutcome.Skipped);
        result.Message.Should().Be("Operation canceled.");
        statuses.Should().Contain((result.Message, false));
        ipad.ResetCalls.Should().Be(1);
    }

    [Fact]
    public async Task ApplyIpadAsync_Cancellation_IsTranslatedToASkippedResult()
    {
        var ipad = new FakeIpadLayout { ThrowOnApply = new OperationCanceledException() };
        var vm = Build(ipadLayout: ipad);
        var statuses = new List<(string, bool)>();
        vm.StatusChanged += (message, isError) => statuses.Add((message, isError));

        var result = await vm.ApplyIpadAsync(IpadPresetCatalog.Presets[0]);

        result.Success.Should().BeTrue();
        result.IsSkipped.Should().BeTrue();
        result.Outcome.Should().Be(StepOutcome.Skipped);
        result.Message.Should().Be("Operation canceled.");
        statuses.Should().Contain((result.Message, false));
    }

    [Fact]
    public async Task MutatingOperations_WhileBusy_AreRefusedWithoutTouchingServices()
    {
        var network = new FakeNetworkTools();
        var ipad = new FakeIpadLayout();
        var bus = new PageOperationBus();
        bus.TryAcquire().Should().BeTrue();
        var vm = Build(networkTools: network, ipadLayout: ipad, operationBus: bus);

        var dns = await vm.ApplyDnsAsync(DnsCatalog.Labels[0]);
        var apply = await vm.ApplyIpadAsync(IpadPresetCatalog.Presets[0]);
        var reset = await vm.ResetIpadAsync();

        dns!.Success.Should().BeFalse();
        apply.Success.Should().BeFalse();
        reset.Success.Should().BeFalse();
        network.ChangeCalls.Should().Be(0);
        ipad.ApplyCalls.Should().Be(0);
        ipad.ResetCalls.Should().Be(0);
        bus.Release();
    }

    [Fact]
    public async Task ProbeDnsAsync_SupersededProbe_DiesInFlightInsteadOfStacking()
    {
        var gate = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var network = new GatedPingTools(gate.Task);
        var vm = Build(networkTools: network);
        var first = DnsCatalog.Labels[0];
        var second = DnsCatalog.Labels[1];

        var superseded = vm.ProbeDnsAsync(first, () => second);
        var current = vm.ProbeDnsAsync(second, () => second);
        gate.TrySetResult(12);

        (await superseded).Should().BeNull("a superseded probe must die in flight, never throw, never paint");
        var probe = await current;
        probe.Should().NotBeNull();
        probe!.PingMs.Should().Be(12);
    }

    [Fact]
    public async Task ProbeDnsAsync_SupersededProbe_RegisteringLate_DoesNotThrow()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var network = new LateRegisterPingTools(gate.Task);
        var vm = Build(networkTools: network);
        var first = DnsCatalog.Labels[0];
        var second = DnsCatalog.Labels[1];

        var superseded = vm.ProbeDnsAsync(first, () => second);
        var current = vm.ProbeDnsAsync(second, () => second);
        gate.TrySetResult(true);

        (await superseded).Should().BeNull("late registration on a canceled-not-disposed token must not throw");
        var probe = await current;
        probe.Should().NotBeNull();
        probe!.PingMs.Should().Be(12);
    }

    [Fact]
    public async Task ApplyIpadAsync_CanceledBeforeDispatch_SettlesFailedWithoutTouchingTheService()
    {
        // U-05a: the sync registry/file calls cannot pre-empt mid-call, so the
        // cancellable window is dispatch — a close landing there settles Failed
        // without starting new system writes, never throws.
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var ipad = new FakeIpadLayout();
        var bus = new PageOperationBus();
        var vm = Build(ipadLayout: ipad, operationBus: bus);

        var result = await vm.ApplyIpadAsync(IpadPresetCatalog.Presets[0], canceled.Token);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Operation canceled.");
        ipad.ApplyCalls.Should().Be(0, "a canceled dispatch must never reach the service tree");
        bus.IsBusy.Should().BeFalse("the bus must release even on the pre-start cancel path");
    }

    private static NetworkViewModel Build(
        INetworkToolsService? networkTools = null,
        FakeIpadLayout? ipadLayout = null,
        PageOperationBus? operationBus = null) =>
        new(networkTools ?? new FakeNetworkTools(),
            ipadLayout ?? new FakeIpadLayout(),
            operationBus ?? new PageOperationBus());

    private sealed class FakeNetworkTools : INetworkToolsService
    {
        public int? PingMs { get; set; } = 12;
        public int PingCalls { get; private set; }
        public int ChangeCalls { get; private set; }
        public string? LastChanged { get; private set; }
        public Exception? ThrowOnChange { get; set; }

        public OperationResult ChangeDns(string primary, string secondary)
        {
            ChangeCalls++;
            LastChanged = primary;
            if (ThrowOnChange is not null) throw ThrowOnChange;
            return OperationResult.Ok($"Applied: {primary} / {secondary}");
        }

        public Task<int?> PingDnsAsync(string host, CancellationToken cancellationToken = default)
        {
            PingCalls++;
            return Task.FromResult(PingMs);
        }
    }

    private sealed class GatedPingTools(Task<int?> gate) : INetworkToolsService
    {
        public OperationResult ChangeDns(string primary, string secondary) =>
            OperationResult.Ok($"Applied: {primary} / {secondary}");

        public async Task<int?> PingDnsAsync(string host, CancellationToken cancellationToken = default) =>
            await gate.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Pins the supersede contract under adversarial timing: a ping that registers
    /// on the token only after the supersede has landed must still settle null and
    /// never throw. (Audit PRP-018: the previous CTS is canceled, never eagerly
    /// disposed. Verified by experiment that Register tolerates a disposed source on
    /// this runtime — but cancel-only keeps the whole WaitHandle class of failures
    /// impossible, so the test pins the contract, not the race.)
    /// </summary>
    private sealed class LateRegisterPingTools(Task<bool> gate) : INetworkToolsService
    {
        public OperationResult ChangeDns(string primary, string secondary) =>
            OperationResult.Ok($"Applied: {primary} / {secondary}");

        public async Task<int?> PingDnsAsync(string host, CancellationToken cancellationToken = default)
        {
            await gate;
            using var registration = cancellationToken.Register(() => { });
            return 12;
        }
    }

    private sealed class FakeIpadLayout : IIpadLayoutService
    {
        public int ApplyCalls { get; private set; }
        public int ResetCalls { get; private set; }
        public (int Width, int Height)? LastApplied { get; private set; }
        public OperationResult ApplyOutcome { get; set; } = OperationResult.Ok("Applied.");
        public Exception? ThrowOnApply { get; set; }
        public Exception? ThrowOnReset { get; set; }

        public OperationResult SetIpadResolution(int width, int height)
        {
            ApplyCalls++;
            LastApplied = (width, height);
            if (ThrowOnApply is not null) throw ThrowOnApply;
            return ApplyOutcome;
        }

        public OperationResult ResetIpadResolution()
        {
            ResetCalls++;
            if (ThrowOnReset is not null) throw ThrowOnReset;
            return OperationResult.Ok("Restored.");
        }
    }
}
