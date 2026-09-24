using System.Diagnostics;
using FluentAssertions;
using Nexora.Infrastructure.Processes;
using Xunit;

namespace Nexora.Tests.Infrastructure.Processes;

/// <summary>
/// Pins the true-async process primitive the connect path now awaits: no
/// emulator, no GameLoop — plain local commands only.
/// </summary>
public sealed class ProcessRunnerAsyncTests
{
    [Fact]
    public async Task RunAsync_FastCommand_ReturnsItsOutput()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            "cmd.exe",
            new[] { "/c", "echo", "hello" },
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.StandardOutput.Should().Contain("hello");
    }

    [Fact]
    public async Task RunAsync_PreCanceledToken_ThrowsWithoutWaiting()
    {
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => runner.RunAsync(
            "cmd.exe",
            new[] { "/c", "echo", "hello" },
            TimeSpan.FromSeconds(10),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_TimeoutKillsTheProcess_ReportsTimedOut()
    {
        var runner = new ProcessRunner();
        var stopwatch = Stopwatch.StartNew();

        var result = await runner.RunAsync(
            "powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30" },
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        stopwatch.Stop();
        result.TimedOut.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }
}
