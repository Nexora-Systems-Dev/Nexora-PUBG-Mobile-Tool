using System.Diagnostics;
using FluentAssertions;
using Nexora.Configuration;
using Nexora.Shared.Contracts;
using Nexora.Features.Security.Infrastructure;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;
using Nexora.Infrastructure.GameLoop;

namespace Nexora.Tests.Infrastructure;

/// <summary>
/// Verifies Defender exclusion path validation, ensuring paths originate strictly
/// from trusted registry hierarchies and reject arbitrary or system directories.
/// </summary>
public sealed class DefenderExclusionTrustTests
{
    [Theory]
    [InlineData(@"C:\Program Files\TxGameAssistant")]
    [InlineData(@"C:\Program Files (x86)\TxGameAssistant")]
    [InlineData(@"D:\Games\TxGameAssistant")]
    [InlineData(@"E:\TxGameAssistant\UI")]
    public void IsTrustedGameLoopPath_AcceptsStandardInstallHierarchies(string path)
    {
        DefenderExclusionService.IsTrustedGameLoopPath(path, new EmulatorOptions()).Should().BeTrue();
    }

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Users\Public\Downloads")]
    [InlineData(@"C:\Temp")]
    [InlineData(@"D:\MaliciousSoftware")]
    [InlineData(@"C:\TxGameAssistantFake\NotReal")]
    [InlineData(@"C:\TxGameAssistant_Malicious")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsTrustedGameLoopPath_RejectsUntrustedOrSystemDirectories(string path)
    {
        DefenderExclusionService.IsTrustedGameLoopPath(path, new EmulatorOptions()).Should().BeFalse();
    }

    [Fact]
    public void IsTrustedGameLoopPath_RejectsSystemRootDirectory()
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        DefenderExclusionService.IsTrustedGameLoopPath(systemRoot, new EmulatorOptions()).Should().BeFalse();
    }

    [Fact]
    public void GetGameLoopRootFromRegistry_ReturnsNull_WhenRegistryPathMissing()
    {
        // When registry has no GameLoop installation, it must safely return null
        // without falling back to inspecting running processes.
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        var processService = new GameLoopProcessService(runner, new GameLoopPathResolver(registry));

        // If no GameLoop install exists at the mock/test environment, it returns null
        var registryPath = processService.GetGameLoopRootFromRegistry();

        // Must either be null or a validated existing directory
        if (registryPath is not null)
        {
            Directory.Exists(registryPath).Should().BeTrue();
            DefenderExclusionService.IsTrustedGameLoopPath(registryPath, new EmulatorOptions()).Should().BeTrue();
        }
    }

    [Fact]
    public void AddDefenderExclusion_FailsGracefully_WhenGameLoopNotInstalledInRegistry()
    {
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        var service = new DefenderExclusionService(runner, new GameLoopProcessService(runner, new GameLoopPathResolver(registry)));

        var result = service.AddDefenderExclusion();

        // Either Defender is disabled/unavailable (OK), or registry path not found (Fail)
        // It must NEVER crash or add an untrusted path.
        result.Should().NotBeNull();
        if (!result.Success)
        {
            result.Message.Should().Match(m =>
                m.Contains("not found in the registry") ||
                m.Contains("outside the expected") ||
                m.Contains("Defender"));
        }
    }

    // U-07a: an exclusion must come back down on teardown, and only ever the
    // one this app added itself — a pre-existing user exclusion survives it.

    private const string TrustedInstallPath = @"C:\Program Files\TxGameAssistant";

    [Fact]
    public async Task AddThenRemove_EmitsAddForAppAddedPath_AndRemoveForThatPathOnly()
    {
        var runner = new ExclusionRecorder();
        var service = new DefenderExclusionService(runner, new FakeProcessService(TrustedInstallPath));

        var addResult = service.AddDefenderExclusion();

        addResult.Success.Should().BeTrue();
        addResult.IsSkipped.Should().BeFalse();
        runner.Scripts.Should().Contain(script =>
            script.Contains("Add-MpPreference", StringComparison.Ordinal) &&
            script.Contains(TrustedInstallPath, StringComparison.Ordinal));
        runner.Scripts.Should().NotContain(script => script.Contains("Remove-MpPreference", StringComparison.Ordinal));

        var removeResult = await service.RemoveDefenderExclusionAsync();

        removeResult.Success.Should().BeTrue();
        runner.Scripts.Should().Contain(script =>
            script.Contains("Remove-MpPreference", StringComparison.Ordinal) &&
            script.Contains(TrustedInstallPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreExistingForeignExclusion_IsNeverAddedTo_AndIsNeverRemoved()
    {
        // The user (or another tool) already excluded the GameLoop directory:
        // Nexora must not add a duplicate, must not claim ownership, and must
        // never remove it on teardown.
        var runner = new ExclusionRecorder { ExistingExclusions = TrustedInstallPath };
        var service = new DefenderExclusionService(runner, new FakeProcessService(TrustedInstallPath));

        var addResult = service.AddDefenderExclusion();

        addResult.IsSkipped.Should().BeTrue();
        addResult.Message.Should().Contain("already excluded");
        runner.Scripts.Should().NotContain(script => script.Contains("Add-MpPreference", StringComparison.Ordinal));

        var removeResult = await service.RemoveDefenderExclusionAsync();

        removeResult.IsSkipped.Should().BeTrue();
        runner.Scripts.Should().NotContain(script => script.Contains("Remove-MpPreference", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemoveDefenderExclusionAsync_SettlesAsStatus_NeverThrows_WhenRemovalFails()
    {
        var runner = new ExclusionRecorder { RemoveSucceeds = false };
        var service = new DefenderExclusionService(runner, new FakeProcessService(TrustedInstallPath));

        service.AddDefenderExclusion();

        // A throwing teardown would fail this line before any assertion ran.
        var removeResult = await service.RemoveDefenderExclusionAsync();

        removeResult.Success.Should().BeFalse();
        removeResult.Message.Should().Contain("Could not remove");

        // The path stays owned, so a later teardown retries it instead of
        // silently forgetting an exclusion that is still on the machine.
        runner.RemoveSucceeds = true;
        var retryResult = await service.RemoveDefenderExclusionAsync();

        retryResult.Success.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveDefenderExclusionAsync_ThrowsOperationCanceled_WhenCancelledBeforeStart()
    {
        var service = new DefenderExclusionService(new ExclusionRecorder(), new FakeProcessService(TrustedInstallPath));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RemoveDefenderExclusionAsync(cts.Token));
    }

    [Theory]
    [InlineData("C:\\Program Files\\TxGameAssistant", "C:\\Program Files\\TxGameAssistant", true)]
    [InlineData("C:\\Program Files\\TxGameAssistant", "c:\\program files\\txgameassistant", true)]
    [InlineData("C:\\Program Files\\TxGameAssistant\\", "C:\\Program Files\\TxGameAssistant", true)]
    [InlineData("C:\\Program Files\\TxGameAssistant", "C:\\Windows", false)]
    [InlineData("C:\\Program Files\\TxGameAssistant", "", false)]
    [InlineData("", "C:\\Program Files\\TxGameAssistant", false)]
    [InlineData("C:\\Program Files\\TxGameAssistant", "not a path at all", false)]
    public void IsPathInExclusionList_MatchesNormalizedSpellings_Only(string resolvedPath, string preferenceOutput, bool expected)
    {
        DefenderExclusionService.IsPathInExclusionList(resolvedPath, preferenceOutput).Should().Be(expected);
    }

    [Fact]
    public void IsPathInExclusionList_MatchesOneLineAmongMany()
    {
        var output = string.Join('\n', @"C:\Windows", TrustedInstallPath, @"D:\Other");

        DefenderExclusionService.IsPathInExclusionList(TrustedInstallPath, output).Should().BeTrue();
        DefenderExclusionService.IsPathInExclusionList(@"D:\Unrelated", output).Should().BeFalse();
    }

    /// <summary>
    /// Records every PowerShell script handed to it and answers the Defender
    /// queries the service makes, so the add/remove scripts are asserted
    /// without ever touching real Defender state or needing elevation.
    /// </summary>
    private sealed class ExclusionRecorder : IProcessRunner
    {
        public List<string> Scripts { get; } = new();

        /// <summary>The lines Get-MpPreference reports as already excluded.</summary>
        public string ExistingExclusions { get; set; } = string.Empty;

        /// <summary>Controls whether Remove-MpPreference succeeds.</summary>
        public bool RemoveSucceeds { get; set; } = true;

        public ProcessResult RunPowerShell(string script, TimeSpan? timeout = null)
        {
            Scripts.Add(script);

            if (script.StartsWith("(Get-MpPreference)", StringComparison.Ordinal))
            {
                return new ProcessResult(0, ExistingExclusions, string.Empty, false);
            }

            if (script.Contains("Remove-MpPreference", StringComparison.Ordinal))
            {
                return new ProcessResult(RemoveSucceeds ? 0 : 1, string.Empty, RemoveSucceeds ? string.Empty : "Access denied.", false);
            }

            // The WinDefend service probe: healthy, so the add path proceeds.
            return new ProcessResult(0, "Running|Automatic", string.Empty, false);
        }

        public ProcessResult Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null) =>
            throw new InvalidOperationException("The Defender exclusion flow only uses RunPowerShell.");

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, TimeSpan? timeout, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The Defender exclusion flow only uses RunPowerShell.");

        public bool StartDetachedElevated(string fileName, string arguments = "") =>
            throw new InvalidOperationException("The Defender exclusion flow only uses RunPowerShell.");
    }

    /// <summary>
    /// Names a registry-sourced GameLoop root without touching the registry or
    /// resolving a live install.
    /// </summary>
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
}
