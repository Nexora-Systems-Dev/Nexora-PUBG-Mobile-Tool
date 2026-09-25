using FluentAssertions;
using Nexora.Infrastructure.Processes;
using Xunit;

namespace Nexora.Tests.Infrastructure.Processes;

/// <summary>
/// Pins the U-06 fix on the sync path: <see cref="ProcessRunner.Run"/> drains
/// both pipes before the wait, so a healthy but chatty child (past the ~64 KB
/// OS pipe buffer) is captured instead of deadlocked, misreported as timed
/// out, and killed. No emulator, no GameLoop — plain local commands only.
/// </summary>
public sealed class ProcessRunnerSyncTests
{
    [Fact]
    public void Run_ChattyChildOverPipeBuffer_CapturesFullOutputWithoutTimeout()
    {
        var runner = new ProcessRunner();
        const int lineCount = 4000; // ~280 KB of stdout — well past the ~64 KB pipe buffer.
        var script =
            $"1..{lineCount} | ForEach-Object {{ \"LINE-{{0:D4}}-{{1}}\" -f $_, ('x' * 60) }}; " +
            "[Console]::Error.WriteLine('ERR-MARKER')";

        var result = runner.Run(
            "powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-Command", script },
            TimeSpan.FromSeconds(60));

        result.TimedOut.Should().BeFalse("a healthy child must not be mistaken for a hang");
        result.Succeeded.Should().BeTrue();
        result.StandardOutput.Should().Contain("LINE-0001-");
        result.StandardOutput.Should().Contain($"LINE-{lineCount:D4}-");
        result.StandardOutput.Length.Should().BeGreaterThan(256 * 1024, "no pipe-buffer truncation or kill may drop output");
        result.StandardError.Should().Contain("ERR-MARKER");
    }

    [Fact]
    public void Run_GenuinelyHungChild_StillTimesOutAndKills()
    {
        var runner = new ProcessRunner();

        var result = runner.Run(
            "powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30" },
            TimeSpan.FromSeconds(2));

        result.TimedOut.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
        result.StandardOutput.Should().BeEmpty();
        result.StandardError.Should().Be("Process timed out.");
    }
}
