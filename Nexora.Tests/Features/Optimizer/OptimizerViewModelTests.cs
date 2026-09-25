using System.Diagnostics;
using FluentAssertions;
using Nexora.Features.Optimizer.Application;
using Nexora.Features.Optimizer.Presentation;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Shared.Contracts;
using Nexora.UI.Presentation;
using Xunit;

namespace Nexora.Tests.Features.Optimizer;

/// <summary>
/// The ViewModel is the Optimizer page's single gate for profile refreshes
/// and tool execution — logic that used to live inline in the shell — so the
/// bus contract, the failure translation, and the D1 presentation-only
/// boundary (no plan building here) are pinned here rather than in the view.
/// </summary>
public sealed class OptimizerViewModelTests
{
    private static readonly HardwareSnapshot Snapshot = new(
        CpuVendor: "GenuineIntel",
        CpuName: "Intel Core i7",
        PhysicalCores: 8,
        LogicalCores: 16,
        TotalMemoryGb: 16,
        GpuVendor: "NVIDIA",
        GpuName: "GeForce RTX 4060",
        GpuMemoryGb: 8,
        RefreshRateHz: 144,
        IsLaptop: false,
        IsOnAcPower: true,
        VirtualizationEnabled: true,
        HypervisorDetected: false);

    private static readonly OptimizerPlan Plan = new(
        Tier: "Performance",
        EmulatorMemoryMb: 4096,
        EmulatorCpuCores: 4,
        ContentScale: 1,
        RenderQuality: 2,
        FxaaQuality: 1,
        EnableLocalShaderCache: true,
        EnableGlobalShaderCache: true,
        RecommendedFps: "120 FPS",
        GpuRoute: "Discrete NVIDIA",
        PowerMode: "High performance");

    [Fact]
    public async Task RefreshProfileAsync_FormatsHardwareAndPlanThroughTheFormatter()
    {
        var engine = new FakeEngine { Snapshot = Snapshot, Plan = Plan };
        var vm = Build(engine: engine);

        var outcome = await vm.RefreshProfileAsync();

        outcome.Should().NotBeNull();
        outcome!.ErrorMessage.Should().BeNull();
        outcome.Display.Should().Be(OptimizerDisplayFormatter.Format(Snapshot, Plan));
    }

    [Fact]
    public async Task RefreshProfileAsync_DetectionFailure_ReturnsTheErrorInsteadOfThrowing()
    {
        var engine = new FakeEngine { ThrowOnSnapshot = new InvalidOperationException("no CIM") };
        var vm = Build(engine: engine);

        var outcome = await vm.RefreshProfileAsync();

        outcome.Should().NotBeNull();
        outcome!.Display.Should().BeNull();
        outcome.ErrorMessage.Should().Be("Hardware detection failed: no CIM");
    }

    [Fact]
    public async Task RefreshProfileAsync_WhileBusy_ReturnsNullWithoutTouchingTheEngine()
    {
        var engine = new FakeEngine { Snapshot = Snapshot, Plan = Plan };
        var bus = new PageOperationBus();
        bus.TryAcquire().Should().BeTrue();
        var vm = Build(engine: engine, operationBus: bus);

        var outcome = await vm.RefreshProfileAsync();

        outcome.Should().BeNull();
        engine.SnapshotCalls.Should().Be(0);
        bus.Release();
    }

    [Fact]
    public async Task ExecuteAsync_RunsTheActionAndReleasesTheBus()
    {
        var vm = Build();
        var statuses = new List<(string, bool)>();
        vm.StatusChanged += (message, isError) => statuses.Add((message, isError));

        var result = await vm.ExecuteAsync(_ => Task.FromResult(OperationResult.Ok("Cleaned.")));

        result.Success.Should().BeTrue();
        statuses.Should().ContainSingle().Which.Should().Be(("Working...", false));
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhileBusy_RefusesWithoutRunningTheAction()
    {
        var bus = new PageOperationBus();
        bus.TryAcquire().Should().BeTrue();
        var vm = Build(operationBus: bus);
        var ran = false;

        var result = await vm.ExecuteAsync(_ =>
        {
            ran = true;
            return Task.FromResult(OperationResult.Ok("Cleaned."));
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Another operation is already running.");
        ran.Should().BeFalse();
        bus.Release();
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_IsTranslatedToASkippedResult()
    {
        var vm = Build();

        var result = await vm.ExecuteAsync(_ => Task.FromException<OperationResult>(new OperationCanceledException()));

        result.Success.Should().BeTrue();
        result.IsSkipped.Should().BeTrue();
        result.Outcome.Should().Be(StepOutcome.Skipped);
        result.Message.Should().Be("Operation canceled.");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Failure_IsTranslatedToAFailedResult()
    {
        var vm = Build();

        var result = await vm.ExecuteAsync(_ => Task.FromException<OperationResult>(new InvalidOperationException("boost blew up")));

        result.Success.Should().BeFalse();
        result.Message.Should().Be("boost blew up");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public void Cancel_OnAnIdleViewModel_IsANoOp()
    {
        var vm = Build();

        var act = () => vm.Cancel();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task ExecuteAsync_CancelDuringRun_SettlesSkippedAndReleasesTheBus()
    {
        // U-05b: close-during-optimizer-apply pre-empts the tool token; the
        // spine translates the pre-emption to a Skip, never a throw.
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = Build();

        var run = vm.ExecuteAsync(async cancellationToken =>
        {
            await gate.Task.WaitAsync(cancellationToken);
            return OperationResult.Ok("Done.");
        });
        vm.Cancel();
        gate.TrySetResult(true);

        var result = await run;
        result.IsSkipped.Should().BeTrue();
        result.Message.Should().Be("Operation canceled.");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshProfileAsync_CancelDuringDetection_SettlesNullWithoutThrowing()
    {
        // U-05b: the refresh token travels into the engine — a close landing
        // mid-detection pre-empts the wait and settles as a silent no-op.
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new FakeEngine { Snapshot = Snapshot, Plan = Plan };
        engine.SnapshotAsyncImpl = async cancellationToken =>
        {
            await gate.Task.WaitAsync(cancellationToken);
            return Snapshot;
        };
        var vm = Build(engine: engine);

        var refresh = vm.RefreshProfileAsync();
        vm.Cancel();
        gate.TrySetResult(true);

        (await refresh).Should().BeNull("a pre-empted read has no reader and paints nothing");
        engine.SnapshotCalls.Should().Be(1);
    }

    [Fact]
    public async Task RefreshProfileAsync_PrecanceledCallerToken_SettlesNullThroughTheEngine()
    {
        // The engine itself pre-empts the canceled token (as the real
        // detection spawn does); the ViewModel translates that to the silent
        // no-op, never a throw.
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var engine = new FakeEngine { Snapshot = Snapshot, Plan = Plan };
        var vm = Build(engine: engine);

        (await vm.RefreshProfileAsync(canceled.Token)).Should().BeNull();
        engine.SnapshotCalls.Should().Be(1);
    }

    private static OptimizerViewModel Build(
        FakeEngine? engine = null,
        FakeTempCleanup? tempCleanup = null,
        FakeProcessService? processService = null,
        PageOperationBus? operationBus = null) =>
        new(engine ?? new FakeEngine { Snapshot = Snapshot, Plan = Plan },
            tempCleanup ?? new FakeTempCleanup(),
            processService ?? new FakeProcessService(),
            operationBus ?? new PageOperationBus());

    private sealed class FakeEngine : IGameLoopPerformanceEngine
    {
        public HardwareSnapshot Snapshot { get; set; } = null!;
        public OptimizerPlan Plan { get; set; } = null!;
        public Exception? ThrowOnSnapshot { get; set; }
        public Func<CancellationToken, Task<HardwareSnapshot>>? SnapshotAsyncImpl { get; set; }
        public int SnapshotCalls { get; private set; }

        public HardwareSnapshot GetHardwareSnapshot() => Snapshot;

        public Task<HardwareSnapshot> GetHardwareSnapshotAsync(CancellationToken cancellationToken = default)
        {
            SnapshotCalls++;
            if (SnapshotAsyncImpl is not null) return SnapshotAsyncImpl(cancellationToken);
            if (ThrowOnSnapshot is not null) throw ThrowOnSnapshot;
            // Mirrors the real detection service, whose Task.Run pre-empts a
            // canceled token before the spawn starts.
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot);
        }

        public OptimizerPlan GetRecommendedPlan(HardwareSnapshot hardware) => Plan;

        public OperationResult ApplySmartSettings() => OperationResult.Ok("Smart.");

        public OperationResult OptimizeGameLoop() => OperationResult.Ok("Boosted.");

        public Task<OperationResult> OptimizeAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok("All."));

        public OperationResult ApplyPerformanceSession() => OperationResult.Ok("Session.");

        public OperationResult RestorePerformanceSession() => OperationResult.Ok("Restored.");

        public Task<OperationResult> RestorePerformanceSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok("Restored."));
    }

    private sealed class FakeTempCleanup : ITempCleanupService
    {
        public Task<OperationResult> CleanTempAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok("Cleaned."));
    }

    private sealed class FakeProcessService : IGameLoopProcessService
    {
        public OperationResult KillGameLoopProcesses(CancellationToken cancellationToken = default) =>
            OperationResult.Ok("Ended.");

        public List<Process> FindGameLoopProcesses(string? gameLoopRoot = null) => new();

        public string? GetGameLoopRootFromRegistry() => null;

        public string? GetGameLoopRoot() => null;

        public string? GetGameLoopUiPath() => null;
    }
}
