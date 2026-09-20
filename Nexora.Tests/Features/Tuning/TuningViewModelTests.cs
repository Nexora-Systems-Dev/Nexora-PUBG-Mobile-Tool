using System.Diagnostics;
using FluentAssertions;
using Nexora.Features.Performance.Application;
using Nexora.Features.Tuning.Application;
using Nexora.Features.Tuning.Domain;
using Nexora.Features.Tuning.Presentation;
using Nexora.Shared.Contracts;
using Nexora.UI.Presentation;
using Xunit;

namespace Nexora.Tests.Features.Tuning;

/// <summary>
/// The ViewModel is the Tuning page's single gate for load/apply/end-task and
/// for the slider labels — logic that used to live inline in the shell — so
/// those contracts are pinned here rather than in the view.
/// </summary>
public sealed class TuningViewModelTests
{
    private static readonly EmulatorTuningSelection Selection = new(
        CpuCores: 4,
        MemoryMb: 4096,
        Dpi: 320,
        RenderCacheEnabled: true,
        GlobalCacheEnabled: true,
        DiscreteGpuEnabled: true,
        RenderOptimizeEnabled: true,
        VSyncEnabled: false,
        AdbEnabled: true,
        AntiAliasingEnabled: true);

    private static readonly EmulatorTuningState IdleState = new(
        Selection,
        IsGameLoopRunning: false,
        RunningProcessNames: Array.Empty<string>());

    [Theory]
    [InlineData(1, "1 core")]
    [InlineData(4, "4 cores")]
    [InlineData(8, "8 cores")]
    public void FormatCpuLabel_SingularizesOneCore(int cores, string expected) =>
        TuningViewModel.FormatCpuLabel(cores).Should().Be(expected);

    [Theory]
    [InlineData(1024, "1024 MB")]
    [InlineData(4096, "4096 MB")]
    public void FormatMemoryLabel_AppendsMegabytes(int megabytes, string expected) =>
        TuningViewModel.FormatMemoryLabel(megabytes).Should().Be(expected);

    [Fact]
    public async Task RefreshAsync_LoadsStateAndRaisesItWithAnIdleStatus()
    {
        var tuning = new FakeTuning { State = IdleState };
        var vm = Build(tuning: tuning);

        EmulatorTuningState? loaded = null;
        var statuses = new List<(string Message, bool IsError)>();
        var loading = new List<bool>();
        vm.StateLoaded += state => loaded = state;
        vm.StatusChanged += (message, isError) => statuses.Add((message, isError));
        vm.LoadingChanged += busy => loading.Add(busy);

        await vm.RefreshAsync();

        tuning.LoadCalls.Should().Be(1);
        loaded.Should().BeSameAs(IdleState);
        vm.CurrentState.Should().BeSameAs(IdleState);
        loading.Should().Equal(true, false);
        statuses.Should().Contain(("Current settings loaded. Adjust, then apply.", false));
    }

    [Fact]
    public async Task RefreshAsync_RunningEmulator_ReportsTheGuardStatus()
    {
        var tuning = new FakeTuning
        {
            State = IdleState with { IsGameLoopRunning = true, RunningProcessNames = new[] { "AndroidEmulatorEx.exe" } },
        };
        var vm = Build(tuning: tuning);

        var statuses = new List<(string Message, bool IsError)>();
        vm.StatusChanged += (message, isError) => statuses.Add((message, isError));

        await vm.RefreshAsync();

        statuses.Should().Contain(("GameLoop is running. End its tasks, then apply.", true));
    }

    [Fact]
    public async Task RefreshAsync_Failure_LeavesStateUntouchedAndReportsTheError()
    {
        var tuning = new FakeTuning { ThrowOnLoad = new InvalidOperationException("registry offline") };
        var vm = Build(tuning: tuning);

        var loaded = false;
        var statuses = new List<(string Message, bool IsError)>();
        vm.StateLoaded += _ => loaded = true;
        vm.StatusChanged += (message, isError) => statuses.Add((message, isError));

        await vm.RefreshAsync();

        loaded.Should().BeFalse();
        vm.CurrentState.Should().BeNull();
        statuses.Should().HaveCount(2);
        statuses[^1].Should().Be(
            ("Could not load emulator settings: registry offline", true));
    }

    [Fact]
    public async Task RefreshAsync_WhileBusy_IsANoOp()
    {
        var tuning = new FakeTuning { State = IdleState };
        var bus = new PageOperationBus();
        bus.TryAcquire().Should().BeTrue();
        var vm = Build(tuning: tuning, operationBus: bus);

        await vm.RefreshAsync();

        tuning.LoadCalls.Should().Be(0);
        bus.Release();
    }

    [Fact]
    public async Task ApplyAsync_DelegatesToTheServiceAndReportsItsMessage()
    {
        var tuning = new FakeTuning { State = IdleState };
        var vm = Build(tuning: tuning);

        var result = await vm.ApplyAsync(Selection);

        result.Success.Should().BeTrue();
        tuning.LastApplied.Should().BeSameAs(Selection);
    }

    [Fact]
    public async Task ApplyAsync_ServiceRefusal_IsRenderedVerbatim()
    {
        var tuning = new FakeTuning
        {
            State = IdleState,
            ApplyOutcome = OperationResult.Fail("Close GameLoop before applying emulator tuning (AndroidEmulatorEx.exe), then apply it again."),
        };
        var vm = Build(tuning: tuning);

        var statuses = new List<(string Message, bool IsError)>();
        vm.StatusChanged += (message, isError) => statuses.Add((message, isError));

        var result = await vm.ApplyAsync(Selection);

        result.Success.Should().BeFalse();
        statuses.Should().Contain((result.Message, true));
    }

    [Fact]
    public async Task EndTaskAsync_KillsProcessesThroughTheProcessService()
    {
        var processes = new FakeProcessService();
        var vm = Build(processService: processes);

        var result = await vm.EndTaskAsync();

        result.Success.Should().BeTrue();
        processes.KillCalls.Should().Be(1);
    }

    private static TuningViewModel Build(
        FakeTuning? tuning = null,
        FakeProcessService? processService = null,
        PageOperationBus? operationBus = null) =>
        new(tuning ?? new FakeTuning { State = IdleState },
            processService ?? new FakeProcessService(),
            operationBus ?? new PageOperationBus());

    private sealed class FakeTuning : IEmulatorSettingsService
    {
        public EmulatorTuningState State { get; set; } = null!;
        public Exception? ThrowOnLoad { get; set; }
        public int LoadCalls { get; private set; }
        public EmulatorTuningSelection? LastApplied { get; private set; }
        public OperationResult ApplyOutcome { get; set; } = OperationResult.Ok("Applied 11 emulator settings. Restart GameLoop to take effect.");

        public Task<EmulatorTuningState> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCalls++;
            if (ThrowOnLoad is not null) throw ThrowOnLoad;
            return Task.FromResult(State);
        }

        public Task<OperationResult> ApplyAsync(EmulatorTuningSelection selection, CancellationToken cancellationToken = default)
        {
            LastApplied = selection;
            return Task.FromResult(ApplyOutcome);
        }
    }

    private sealed class FakeProcessService : IGameLoopProcessService
    {
        public int KillCalls { get; private set; }

        public OperationResult KillGameLoopProcesses(CancellationToken cancellationToken = default)
        {
            KillCalls++;
            return OperationResult.Ok("Ended 1 GameLoop process.");
        }

        public List<Process> FindGameLoopProcesses(string? gameLoopRoot = null) => new();

        public string? GetGameLoopRootFromRegistry() => null;

        public string? GetGameLoopRoot() => null;

        public string? GetGameLoopUiPath() => null;
    }
}
