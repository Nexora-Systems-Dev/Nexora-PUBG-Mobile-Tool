using FluentAssertions;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Infrastructure.Processes;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Features.Performance;

/// <summary>
/// Pins the U-05b power-scheme race: a close-path Restore landing mid-Apply
/// must never strand the system on high-performance with the restore value
/// nulled. The apply re-arms a consumed capture, so the next restore brings
/// the system back instead of reporting "no session". No elevation, no
/// powercfg — a gated fake runner stands in for the spawns.
/// </summary>
public sealed class PowerSessionConcurrencyTests
{
    private static readonly HardwareSnapshot DesktopOnAc = new(
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

    [Fact]
    public async Task RestoreDuringApply_RearmsTheCapture_SoTheNextRestoreBringsTheSystemBack()
    {
        var runner = new GatedPowerRunner();
        var service = new PowerSessionService(runner);

        // The apply captures Balanced, then blocks inside the high-performance
        // setactive — the exact window a close-path restore can land in.
        var apply = Task.Run(() => service.Apply(DesktopOnAc));
        (await Task.WhenAny(runner.EnteredSetHighPerf.Task, Task.Delay(TimeSpan.FromSeconds(10))))
            .Should().Be(runner.EnteredSetHighPerf.Task, "the apply must reach its setactive spawn");

        // Close lands mid-apply: consumes the capture and restores the
        // still-original scheme.
        var midRestore = service.Restore();
        midRestore.Success.Should().BeTrue();

        // The apply's setactive lands after the restore: high-performance is
        // now active, but the capture must be re-armed, not lost.
        runner.ReleaseSetHighPerf.Set();
        (await apply).Success.Should().BeTrue();

        // Proves the value survived: a lost (nulled) capture reports
        // "No Windows power session is active" and the system stays stranded.
        var finalRestore = service.Restore();
        finalRestore.Success.Should().BeTrue();
        finalRestore.Message.Should().Be("Previous Windows power mode restored.");
        runner.SetActiveSchemes.Should().Contain(argv => argv.Contains(GatedPowerRunner.BalancedSchemeGuid),
            "the final restore must switch back to the captured scheme");
        runner.SetActiveCalls.Should().Be(3, "apply setactive + mid restore + final restore");
    }

    [Fact]
    public void Restore_WithoutApply_ReportsNoSession()
    {
        var service = new PowerSessionService(new GatedPowerRunner());

        var result = service.Restore();

        result.Success.Should().BeTrue();
        result.Message.Should().Be("No Windows power session is active.");
    }

    /// <summary>
    /// Answers powercfg probes from canned output and gates only the
    /// high-performance setactive, so the test can park an apply inside its
    /// spawn while the test thread runs the racing restore.
    /// </summary>
    private sealed class GatedPowerRunner : IProcessRunner
    {
        public const string BalancedSchemeGuid = "381b4222-fb69-11d1-9a68-00c04fd827f7";

        public TaskCompletionSource<bool> EnteredSetHighPerf { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ManualResetEventSlim ReleaseSetHighPerf { get; } = new(false);

        private readonly List<string> _setActiveSchemes = new();
        private int _setActiveCalls;

        public IReadOnlyList<string> SetActiveSchemes
        {
            get { lock (_setActiveSchemes) return _setActiveSchemes.ToList(); }
        }

        public int SetActiveCalls => Volatile.Read(ref _setActiveCalls);

        public ProcessResult Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null)
        {
            var argv = string.Join(' ', arguments);
            if (argv.StartsWith("/getactivescheme", StringComparison.Ordinal))
            {
                return new ProcessResult(0, $"Power Scheme GUID: {BalancedSchemeGuid}  (Balanced)", string.Empty, false);
            }

            if (argv.StartsWith("/setactive", StringComparison.Ordinal))
            {
                if (argv.Contains("SCHEME_MIN", StringComparison.OrdinalIgnoreCase))
                {
                    EnteredSetHighPerf.TrySetResult(true);
                    ReleaseSetHighPerf.Wait(TimeSpan.FromSeconds(10));
                }

                lock (_setActiveSchemes) _setActiveSchemes.Add(argv);
                Interlocked.Increment(ref _setActiveCalls);
                return new ProcessResult(0, string.Empty, string.Empty, false);
            }

            throw new InvalidOperationException($"Unexpected powercfg call: {argv}");
        }

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, TimeSpan? timeout, CancellationToken cancellationToken) =>
            Task.FromResult(Run(fileName, arguments, timeout));

        public ProcessResult RunPowerShell(string script, TimeSpan? timeout = null) =>
            throw new InvalidOperationException("The power session never shells out to PowerShell.");

        public bool StartDetachedElevated(string fileName, string? arguments = null) => false;
    }
}
