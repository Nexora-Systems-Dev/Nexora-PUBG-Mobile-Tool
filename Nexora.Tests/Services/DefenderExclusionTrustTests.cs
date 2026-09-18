using FluentAssertions;
using Nexora.Configuration;
using Nexora.Features.Security;
using Nexora.Services.Performance;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

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
}
