using FluentAssertions;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Features.Security.Infrastructure;
using Nexora.Shared.Kernel;
using Nexora.UI.Presentation;
using Xunit;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;
using Nexora.Infrastructure.GameLoop;
using Nexora.Shared.Contracts;

namespace Nexora.Tests.Features.Performance;

/// <summary>
/// Focused reporting tests for WINDOWS &amp; GPU BOOST:
/// Applied vs Skipped vs Failed must stay visible through
/// PerformanceExecutionReport -&gt; OperationResult -&gt; Activity panel.
/// </summary>
public sealed class WindowsGpuBoostReportingTests
{
    [Fact]
    public void OperationResult_Skip_IsSuccess_WithSkippedOutcome()
    {
        var result = OperationResult.Skip("GameLoop is not running; monitor armed.");

        result.Success.Should().BeTrue();
        result.IsSkipped.Should().BeTrue();
        result.Outcome.Should().Be(StepOutcome.Skipped);
        result.Message.Should().Contain("not running");
    }

    [Fact]
    public void Report_Distinguishes_Applied_Skipped_And_Failed()
    {
        var report = PerformanceExecutionReport.Create(
            ("Registry", OperationResult.Ok("Registry applied.")),
            ("NVIDIA profile", OperationResult.Skip("Intel GPU detected; NVIDIA profile skipped.")),
            ("Defender exclusion", OperationResult.Fail("Could not update the exclusion.")));

        report.AppliedCount.Should().Be(1);
        report.SkippedCount.Should().Be(1);
        report.FailedCount.Should().Be(1);
        report.Succeeded.Should().BeFalse();

        var result = report.ToDetailedResult("Boost finished");
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("[Applied]");
        result.Message.Should().Contain("[Skipped]");
        result.Message.Should().Contain("[Failed]");
    }

    [Fact]
    public void Report_AllSkipped_DoesNotClaimApplied()
    {
        var report = PerformanceExecutionReport.Create(
            ("NVIDIA profile", OperationResult.Skip("Intel GPU detected; NVIDIA profile skipped.")),
            ("Defender exclusion", OperationResult.Skip("Windows Defender is disabled; exclusion skipped.")));

        var result = report.ToDetailedResult("Windows and GPU boost finished");

        result.Success.Should().BeTrue();
        result.IsSkipped.Should().BeTrue();
        result.Message.Should().Contain("0 applied");
        result.Message.Should().NotContain("applied successfully");
    }

    [Fact]
    public void Report_SuccessDetails_ListEveryStep()
    {
        var report = PerformanceExecutionReport.Create(
            ("Registry", OperationResult.Ok("Registry applied.")),
            ("Priority", OperationResult.Skip("GameLoop is not running; monitor armed.")));

        var result = report.ToDetailedResult("Windows and GPU boost finished");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Registry");
        result.Message.Should().Contain("Priority");
        result.Message.Should().Contain("GameLoop is not running");
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4060 | NVIDIA", GpuVendor.Nvidia)]
    [InlineData("Intel Iris Xe Graphics | Intel", GpuVendor.Intel)]
    [InlineData("AMD Radeon Graphics | AMD", GpuVendor.Amd)]
    [InlineData("Advanced Micro Devices Radeon RX 7900", GpuVendor.Amd)]
    [InlineData("", GpuVendor.Unknown)]
    public void ClassifyGpuProvider_Detects_Vendor_Accurately(string providerText, GpuVendor expected)
    {
        NvidiaOptimizerService.ClassifyGpuProvider(providerText).Should().Be(expected);
    }

    [Fact]
    public void ShouldSkipDefender_ReturnsTrue_WhenServiceStoppedOrUnavailable()
    {
        DefenderExclusionService.ShouldSkipDefender("Stopped|Disabled", true).Should().BeTrue();
        DefenderExclusionService.ShouldSkipDefender("Running|Automatic", true).Should().BeFalse();
        DefenderExclusionService.ShouldSkipDefender("Unavailable", true).Should().BeTrue();
        DefenderExclusionService.ShouldSkipDefender("Running|Automatic", false).Should().BeTrue();
    }

    [Fact]
    public void ProcessPriority_OfflineGameLoop_ReturnsSkipped_AndArmsMonitor()
    {
        var store = new ProcessPrioritySnapshotStore();
        var processService = new GameLoopProcessService(new ProcessRunner(), new GameLoopPathResolver(new RegistryService()));
        var applier = new ProcessPriorityApplier(store, processService);
        var service = new ProcessPriorityService(store, applier, new ProcessPriorityMonitor(store, applier));

        // CI hosts have no GameLoop emulator running; if one is running the
        // service must still succeed and report priority tuning instead.
        var result = service.Apply(Path.Combine(Path.GetTempPath(), $"NexoraNoGameLoop-{Guid.NewGuid():N}"));

        try
        {
            result.Success.Should().BeTrue();
            if (result.IsSkipped)
            {
                result.Message.Should().Contain("not running");
                result.Message.Should().Contain("monitor");
            }
            else
            {
                result.Message.Should().Contain("priority");
            }
        }
        finally
        {
            service.Restore();
        }
    }

    [Fact]
    public void PowerSession_ApplyAndRestore_DoNotThrow_AndStayConcise()
    {
        var service = new PowerSessionService(new ProcessRunner());
        var hardware = new Nexora.Features.Performance.Domain.HardwareSnapshot(
            "Unknown CPU vendor", "Unknown CPU", 4, 8, 16,
            "Intel", "Iris Xe", 0, 60, false, true, false, false);

        var applied = service.Apply(hardware);
        try
        {
            applied.Should().NotBeNull();
            applied.Message.Length.Should().BeLessThan(200);
        }
        finally
        {
            var restored = service.Restore();
            restored.Should().NotBeNull();
            restored.Message.Length.Should().BeLessThan(200);
        }
    }

    [Fact]
    public void ActivityFormatter_Propagates_StepDetails_ToPanel()
    {
        var report = PerformanceExecutionReport.Create(
            ("Registry", OperationResult.Ok("Registry applied.")),
            ("NVIDIA profile", OperationResult.Skip("Intel GPU detected; NVIDIA profile skipped.")));
        var result = report.ToDetailedResult("Windows and GPU boost finished");

        var display = ActivityReportFormatter.Format(result);

        display.StatusLine.Should().Contain("1 applied");
        display.Details.Should().Contain("Intel GPU detected");
        display.Details.Should().Contain("[Skipped]");
        display.IsError.Should().BeFalse();
    }

    [Fact]
    public void ActivityFormatter_Marks_Failures_AsError()
    {
        var report = PerformanceExecutionReport.Create(
            ("Defender exclusion", OperationResult.Fail("Access denied; run as administrator.")));
        var result = report.ToDetailedResult("Windows and GPU boost finished");

        var display = ActivityReportFormatter.Format(result);

        display.IsError.Should().BeTrue();
        display.Details.Should().Contain("[Failed]");
    }

    [Fact]
    public void Report_EmptySteps_DoesNotClaimApplied()
    {
        var report = PerformanceExecutionReport.Create();
        var result = report.ToDetailedResult("Windows and GPU boost finished");

        result.Success.Should().BeTrue();
        result.IsSkipped.Should().BeTrue();
        result.Message.Should().Contain("0 applied");
    }

    [Fact]
    public void VendorSkipMessage_Covers_UnknownGpu()
    {
        NvidiaOptimizerService.ClassifyGpuProvider("Some Future GPU 9000").Should().Be(GpuVendor.Unknown);
        NvidiaOptimizerService.VendorSkipMessage(GpuVendor.Unknown).Should().Contain("No NVIDIA GPU");
    }

    [Theory]
    [InlineData("NVIDIA", "GeForce GTX 1660", true)]
    [InlineData("NVIDIA", "GeForce MX150", true)]
    [InlineData("Intel", "Arc A770", true)]
    [InlineData("AMD", "Radeon RX 7900", true)]
    [InlineData("AMD", "Radeon Graphics", false)]
    [InlineData("Intel", "Iris Xe Graphics", false)]
    [InlineData("Intel", "UHD Graphics 630", false)]
    public void HasDedicatedGpu_Shares_Classifier_Markers(string gpuVendor, string gpuName, bool expected)
    {
        var hardware = new HardwareSnapshot("Intel", "Core i5", 4, 8, 16, gpuVendor, gpuName, 4, 60, false, true, false, false);
        hardware.HasDedicatedGpu.Should().Be(expected);
    }

    [Fact]
    public void IntelArc_Classifies_IntelVendor_But_DedicatedPlan()
    {
        // The motivating case: one shared classifier answers both questions —
        // Intel vendor (NVIDIA profile correctly skipped) with discrete
        // silicon (dedicated plan correctly offered).
        NvidiaOptimizerService.ClassifyGpuProvider("Intel Arc A770 | Intel").Should().Be(GpuVendor.Intel);
        var hardware = new HardwareSnapshot("Intel", "Core i5", 6, 12, 16, "Intel", "Arc A770", 8, 144, false, true, false, false);
        hardware.HasDedicatedGpu.Should().BeTrue();
    }

    [Theory]
    [InlineData("GpuPreference=2;", true)]
    [InlineData("GpuPreference=2", true)]
    [InlineData("GpuPreference=1;", false)]
    [InlineData("", false)]
    public void IsHighPerformancePreference_Tolerates_LegacyFormat(string? value, bool expected)
    {
        GpuRoutingService.IsHighPerformancePreference(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"D:\Games\TxGameAssistant\UI", true)]
    [InlineData(@"C:\Program Files\TxGameAssistant\UI", true)]
    [InlineData(@"C:\Windows\System32", false)]
    [InlineData(@"C:\FakeTxGameAssistantEvil\UI", false)]
    [InlineData("", false)]
    public void IsTrustedInstallPath_RequiresExactInstallFolderSegment(string? path, bool expected)
    {
        GpuRoutingService.IsTrustedInstallPath(path, new Nexora.Configuration.EmulatorOptions()).Should().Be(expected);
    }

    [Fact]
    public void ApplyHighPerformance_RejectsDirectory_OutsideInstallFolder()
    {
        var untrusted = Path.Combine(Path.GetTempPath(), $"NexoraGpuUntrusted-{Guid.NewGuid():N}");
        Directory.CreateDirectory(untrusted);
        try
        {
            var service = new GpuRoutingService(new MemoryUserRegistry());

            var result = service.ApplyHighPerformance(untrusted, new[] { "AndroidEmulatorEn.exe" });

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("outside the expected installation directory");
        }
        finally
        {
            Directory.Delete(untrusted, recursive: true);
        }
    }

    private sealed class MemoryUserRegistry : Nexora.Infrastructure.Registry.IUserRegistry
    {
        public int? GetUserDword(string name) => null;

        public bool SetUserDword(string name, int value) => true;

        public int? GetAppSettingDword(string name) => null;

        public void SetAppSettingDword(string name, int value)
        {
        }

        public void DeleteAppSetting(string name)
        {
        }

        public bool SetCurrentUserString(string subKeyPath, string name, string value) => true;

        public string? GetCurrentUserString(string subKeyPath, string name) => null;
    }
}
