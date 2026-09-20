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
