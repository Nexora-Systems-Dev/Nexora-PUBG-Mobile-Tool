using FluentAssertions;
using Nexora.Services.Performance;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Performance;

public sealed class PerformanceExecutionReportTests
{
    [Fact]
    public void ToOperationResult_ReturnsSuccessMessage_WhenAllStepsSucceed()
    {
        var report = PerformanceExecutionReport.Create(
            ("Smart settings", OperationResult.Ok("Smart settings applied.")),
            ("Temp cleanup", OperationResult.Ok("Temporary files cleaned.")));

        var result = report.ToOperationResult("All done.", "Completed with issues.");

        report.Succeeded.Should().BeTrue();
        report.SuccessfulCount.Should().Be(2);
        report.Failures.Should().BeEmpty();
        result.Success.Should().BeTrue();
        result.Message.Should().Contain("All done.");
        result.Message.Should().Contain("[Applied]");
        result.Message.Should().Contain("Smart settings");
    }

    [Fact]
    public void ToOperationResult_ReportsPartialSuccess_WhenOneStepFails()
    {
        var report = PerformanceExecutionReport.Create(
            ("Smart settings", OperationResult.Ok("Smart settings applied.")),
            ("NVIDIA profile", OperationResult.Fail("NVIDIA Profile Inspector could not apply the profile.")));

        var result = report.ToOperationResult("All done.", "Completed with issues.");

        // Partial progress stays visible instead of collapsing to a bare boolean.
        report.Succeeded.Should().BeFalse();
        report.SuccessfulCount.Should().Be(1);
        report.Failures.Should().HaveCount(1);
        report.Failures[0].Name.Should().Be("NVIDIA profile");
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("1/2 steps completed");
        result.Message.Should().Contain("NVIDIA profile");
        result.Message.Should().Contain("could not apply the profile");
    }

    [Fact]
    public void ToOperationResult_SucceedsVacuously_WhenThereAreNoSteps()
    {
        var report = PerformanceExecutionReport.Create();

        var result = report.ToOperationResult("Nothing to do.", "Completed with issues.");

        report.Succeeded.Should().BeTrue();
        report.SuccessfulCount.Should().Be(0);
        result.Success.Should().BeTrue();
        result.IsSkipped.Should().BeTrue();
        result.Message.Should().Contain("Nothing to do.");
    }
}
