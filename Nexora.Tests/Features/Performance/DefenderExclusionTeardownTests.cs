using System.Diagnostics;
using FluentAssertions;
using Nexora.Configuration;
using Nexora.Features.Optimizer.Application;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Contracts;
using Nexora.Shared.Kernel;
using Xunit;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;

namespace Nexora.Tests.Features.Performance;

/// <summary>
/// U-07a: the Defender exclusion an optimizer action adds must come back down on
/// the app teardown path — the same <see cref="IGameLoopPerformanceEngine.RestorePerformanceSessionAsync"/>
/// the close handler awaits — and a failed removal must settle as a status
/// rather than escape as an exception.
/// </summary>
public sealed class DefenderExclusionTeardownTests
{
    private const string TrustedInstallPath = @"C:\Program Files\TxGameAssistant";

    [Fact]
    public async Task RestorePerformanceSessionAsync_RemovesTheExclusionTheSameSessionAdded()
    {
        var runner = new TeardownRecorder();
        var engine = CreateEngine(runner);

        engine.OptimizeGameLoop();

        // The optimize pass added the exclusion and recorded ownership of it.
        runner.PowerShellScripts.Should().Contain(script =>
            script.Contains("Add-MpPreference", StringComparison.Ordinal) &&
            script.Contains(TrustedInstallPath, StringComparison.Ordinal));
        runner.PowerShellScripts.Clear();

        // The teardown path is the window-close restore: it must put the
        // exclusion back, not leave it on the machine.
        await engine.RestorePerformanceSessionAsync();

        runner.PowerShellScripts.Should().Contain(script =>
            script.Contains("Remove-MpPreference", StringComparison.Ordinal) &&
            script.Contains(TrustedInstallPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestorePerformanceSessionAsync_WithoutAnAddedExclusion_RemovesNothing()
    {
        // Nothing was optimized this session, so teardown has nothing of its
        // own to remove — it must not probe Defender or emit a removal.
        var runner = new TeardownRecorder();
        var engine = CreateEngine(runner);

        await engine.RestorePerformanceSessionAsync();

        runner.PowerShellScripts.Should().NotContain(script =>
            script.Contains("MpPreference", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestorePerformanceSessionAsync_NeverThrows_WhenDefenderRemovalFails()
    {
        var runner = new TeardownRecorder { RemoveSucceeds = false };
        var engine = CreateEngine(runner);

        engine.OptimizeGameLoop();
        runner.PowerShellScripts.Clear();

        // Window_Closing catches nothing from this method specifically — the
        // best-effort contract lives here, so a removal failure must land in
        // the returned result instead of propagating to the close handler.
        Func<Task> act = async () => await engine.RestorePerformanceSessionAsync();

        await act.Should().NotThrowAsync();
    }

    private static PerformanceEngineFacade CreateEngine(TeardownRecorder runner)
    {
        var processService = new FakeProcessService(TrustedInstallPath);
        var priorityStore = new ProcessPrioritySnapshotStore();
        var priorityApplier = new ProcessPriorityApplier(priorityStore, processService);

        return new PerformanceEngineFacade(
            runner,
            new NoOpRegistry(),
            new NoOpRegistry(),
            processService,
            new NoOpTempCleanup(),
            new ProcessPriorityService(priorityStore, priorityApplier, new ProcessPriorityMonitor(priorityStore, priorityApplier)));
    }

    /// <summary>
    /// Records every PowerShell script and answers the process calls the engine
    /// makes during optimize/restore, so the teardown sequence is asserted
    /// without elevation and without touching real Defender or power state.
    /// </summary>
    private sealed class TeardownRecorder : IProcessRunner
    {
        public List<string> PowerShellScripts { get; } = new();

        /// <summary>Controls whether Remove-MpPreference succeeds.</summary>
        public bool RemoveSucceeds { get; set; } = true;

        public ProcessResult RunPowerShell(string script, TimeSpan? timeout = null)
        {
            PowerShellScripts.Add(script);

            if (script.StartsWith("(Get-MpPreference)", StringComparison.Ordinal))
            {
                // No pre-existing exclusions, so the add path always claims ownership.
                return new ProcessResult(0, string.Empty, string.Empty, false);
            }

            if (script.Contains("Remove-MpPreference", StringComparison.Ordinal))
            {
                return new ProcessResult(RemoveSucceeds ? 0 : 1, string.Empty, RemoveSucceeds ? string.Empty : "Access denied.", false);
            }

            if (script.Contains("Get-Service -Name WinDefend", StringComparison.Ordinal))
            {
                // Healthy WinDefend, so the add path does not skip on it.
                return new ProcessResult(0, "Running|Automatic", string.Empty, false);
            }

            // Empty output makes hardware detection fall back to a safe snapshot.
            return new ProcessResult(0, string.Empty, string.Empty, false);
        }

        public ProcessResult Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null)
        {
            // The power session needs a scheme GUID to capture for later restore.
            var containsGetActiveScheme = arguments.Any(arg => arg == "/getactivescheme");
            return containsGetActiveScheme
                ? new ProcessResult(0, "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)", string.Empty, false)
                : new ProcessResult(0, string.Empty, string.Empty, false);
        }

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, TimeSpan? timeout, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, false));

        public bool StartDetachedElevated(string fileName, string arguments = "") => true;
    }

    private sealed class FakeProcessService : IGameLoopProcessService
    {
        private readonly string? _registryRoot;

        public FakeProcessService(string? registryRoot) => _registryRoot = registryRoot;

        public string? GetGameLoopRootFromRegistry() => _registryRoot;
        public string? GetGameLoopRoot() => _registryRoot;
        public string? GetGameLoopUiPath() => _registryRoot is null ? null : Path.Combine(_registryRoot, "UI");
        public List<Process> FindGameLoopProcesses(string? gameLoopRoot = null) => new();
        public OperationResult KillGameLoopProcesses(CancellationToken cancellationToken = default) =>
            OperationResult.Skip("No GameLoop processes exist in the test environment.");
    }

    /// <summary>Registry that records nothing and reports no existing values.</summary>
    private sealed class NoOpRegistry : IUserRegistry, IMachineRegistry
    {
        public int? GetUserDword(string name) => null;
        public bool SetUserDword(string name, int value) => true;
        public int? GetAppSettingDword(string name) => null;
        public void SetAppSettingDword(string name, int value) { }
        public void DeleteAppSetting(string name) { }
        public bool SetCurrentUserString(string subKeyPath, string name, string value) => true;
        public string? GetCurrentUserString(string subKeyPath, string name) => null;
        public string? GetLocalString(string name, string? branch = null) => null;
        public bool SetLocalMachineDword(string subKeyPath, string name, int value) => true;
        public int? GetLocalMachineDword(string subKeyPath, string name) => null;
    }

    private sealed class NoOpTempCleanup : ITempCleanupService
    {
        public Task<OperationResult> CleanTempAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok("Temp cleanup is a no-op in this test."));
    }
}
