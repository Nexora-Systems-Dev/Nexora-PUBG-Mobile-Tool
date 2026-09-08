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
        // Arrange
        var report = PerformanceExecutionReport.Create(
            ("Smart settings", OperationResult.Ok("Smart settings applied.")),
            ("Temp cleanup", OperationResult.Ok("Temporary files cleaned.")));

        // Act
        var result = report.ToOperationResult("All done.", "Completed with issues.");

        // Assert
        report.Succeeded.Should().BeTrue();
        report.SuccessfulCount.Should().Be(2);
        report.Failures.Should().BeEmpty();
        result.Success.Should().BeTrue();
        result.Message.Should().Be("All done.");
    }

    [Fact]
    public void ToOperationResult_ReportsPartialSuccess_WhenOneStepFails()
    {
        // Arrange
        var report = PerformanceExecutionReport.Create(
            ("Smart settings", OperationResult.Ok("Smart settings applied.")),
            ("NVIDIA profile", OperationResult.Fail("NVIDIA Profile Inspector could not apply the profile.")));

        // Act
        var result = report.ToOperationResult("All done.", "Completed with issues.");

        // Assert: partial progress stays visible instead of collapsing to a bare boolean.
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
        // Arrange
        var report = PerformanceExecutionReport.Create();

        // Act
        var result = report.ToOperationResult("Nothing to do.", "Completed with issues.");

        // Assert
        report.Succeeded.Should().BeTrue();
        report.SuccessfulCount.Should().Be(0);
        result.Success.Should().BeTrue();
        result.Message.Should().Be("Nothing to do.");
    }
}
