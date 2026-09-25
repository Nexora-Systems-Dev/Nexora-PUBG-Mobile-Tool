using FluentAssertions;
using Nexora.Features.Updates.Application;
using Nexora.Features.Updates.Domain;
using Nexora.Features.Updates.Presentation;
using Nexora.Shared.Contracts;
using Xunit;

namespace Nexora.Tests.Features.Updates;

/// <summary>
/// The startup update handoff moved out of the shell verbatim, so the paths
/// that need no window (nothing available, check failure) are pinned here.
/// The prompt-and-download path needs a MessageBox and stays manual-only.
/// </summary>
public sealed class UpdateHandoffTests
{
    private static readonly UpdateInfo NoneAvailable = new(
        Available: false,
        LatestVersion: "v1.1.0",
        AssetName: "Nexora.exe",
        DownloadUrl: "https://example.invalid/nexora.exe",
        ChangeLog: "Nothing new.");

    [Fact]
    public async Task RunAsync_NoUpdateAvailable_TouchesNothing()
    {
        var service = new FakeUpdateService { Info = NoneAvailable };
        var statuses = new List<(string, bool)>();
        var closed = false;
        var handoff = new UpdateHandoff(service, () => false, (m, e) => statuses.Add((m, e)), () => closed = true);

        await handoff.RunAsync();

        service.CheckCalls.Should().Be(1);
        statuses.Should().BeEmpty();
        closed.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_AfterShutdown_SkipsThePrompt()
    {
        var available = NoneAvailable with { Available = true };
        var service = new FakeUpdateService { Info = available };
        var closed = false;
        var handoff = new UpdateHandoff(service, () => true, (_, _) => { }, () => closed = true);

        await handoff.RunAsync();

        closed.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_CheckFailure_ReportsThroughStatusWithoutClosing()
    {
        var service = new FakeUpdateService { ThrowOnCheck = new InvalidOperationException("offline") };
        var statuses = new List<(string, bool)>();
        var closed = false;
        var handoff = new UpdateHandoff(service, () => false, (m, e) => statuses.Add((m, e)), () => closed = true);

        await handoff.RunAsync();

        statuses.Should().ContainSingle().Which.Should().Be(("Update check failed: offline", true));
        closed.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_CanceledBeforeClose_ReportsCancelWithoutClosingOrThrowing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new FakeUpdateService { Info = NoneAvailable };
        var statuses = new List<(string, bool)>();
        var closed = false;
        var handoff = new UpdateHandoff(service, () => false, (m, e) => statuses.Add((m, e)), () => closed = true);

        await handoff.RunAsync(cts.Token);

        service.CheckCalls.Should().Be(0);
        statuses.Should().ContainSingle().Which.Should().Be(("Update check canceled.", true));
        closed.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_CanceledDuringShutdown_StaysSilent()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new FakeUpdateService { Info = NoneAvailable };
        var statuses = new List<(string, bool)>();
        var closed = false;
        var handoff = new UpdateHandoff(service, () => true, (m, e) => statuses.Add((m, e)), () => closed = true);

        await handoff.RunAsync(cts.Token);

        service.CheckCalls.Should().Be(0);
        statuses.Should().BeEmpty();
        closed.Should().BeFalse();
    }

    [Fact]
    public void CompleteHandoff_SuccessOnLiveWindow_ReportsAndClosesOnce()
    {
        var service = new FakeUpdateService { Info = NoneAvailable };
        var statuses = new List<(string, bool)>();
        var closes = 0;
        var handoff = new UpdateHandoff(service, () => false, (m, e) => statuses.Add((m, e)), () => closes++);

        handoff.CompleteHandoff(OperationResult.Ok("Update downloaded and verified."));

        statuses.Should().ContainSingle().Which.Should().Be(("Update downloaded and verified.", false));
        closes.Should().Be(1);
    }

    [Fact]
    public void CompleteHandoff_ThrowingClose_IsSwallowedNeverMisreportedAsCheckFailure()
    {
        // U-05c: a racing close that beats the handoff to a dead window throws
        // InvalidOperationException — it must stay silent instead of falling
        // into RunAsync's catch-all as "Update check failed".
        var service = new FakeUpdateService { Info = NoneAvailable };
        var statuses = new List<(string, bool)>();
        var handoff = new UpdateHandoff(
            service, () => false, (m, e) => statuses.Add((m, e)),
            () => throw new InvalidOperationException("Cannot call Close when the window is closing."));

        var act = () => handoff.CompleteHandoff(OperationResult.Ok("Update downloaded and verified."));

        act.Should().NotThrow();
        statuses.Should().ContainSingle().Which.Should().Be(("Update downloaded and verified.", false));
    }

    [Fact]
    public void CompleteHandoff_AfterShutdown_StaysFullySilent()
    {
        var service = new FakeUpdateService { Info = NoneAvailable };
        var statuses = new List<(string, bool)>();
        var closes = 0;
        var handoff = new UpdateHandoff(service, () => true, (m, e) => statuses.Add((m, e)), () => closes++);

        handoff.CompleteHandoff(OperationResult.Ok("Update downloaded and verified."));

        statuses.Should().BeEmpty("the status sink paints an unguarded cell");
        closes.Should().Be(0);
    }

    [Fact]
    public void CompleteHandoff_FailedDownload_ReportsWithoutClosing()
    {
        var service = new FakeUpdateService { Info = NoneAvailable };
        var statuses = new List<(string, bool)>();
        var closes = 0;
        var handoff = new UpdateHandoff(service, () => false, (m, e) => statuses.Add((m, e)), () => closes++);

        handoff.CompleteHandoff(OperationResult.Fail("Update failed: offline."));

        statuses.Should().ContainSingle().Which.Should().Be(("Update failed: offline.", true));
        closes.Should().Be(0);
    }

    private sealed class FakeUpdateService : IUpdateService
    {
        public UpdateInfo Info { get; set; } = NoneAvailable;
        public Exception? ThrowOnCheck { get; set; }
        public int CheckCalls { get; private set; }

        public Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            if (ThrowOnCheck is not null) throw ThrowOnCheck;
            return Task.FromResult(Info);
        }

        public Task<OperationResult> DownloadAndLaunchAsync(UpdateInfo update, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok("Launched."));
    }
}
