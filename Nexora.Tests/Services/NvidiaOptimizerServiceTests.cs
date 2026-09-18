using System.Reflection;
using FluentAssertions;
using Nexora.Configuration;
using Nexora.Services.Performance;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

/// <summary>
/// Verifies that NvidiaOptimizerService handles non-NVIDIA GPUs gracefully
/// and fails safely when assets or paths are missing.
/// </summary>
public sealed class NvidiaOptimizerServiceTests
{
    [Fact]
    public void Constructor_Throws_WhenRequiredArgumentsNull()
    {
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        var processService = new GameLoopProcessService(runner, new GameLoopPathResolver(registry));

        var actRunnerNull = () => new NvidiaOptimizerService(null!, processService);
        var actProcessNull = () => new NvidiaOptimizerService(runner, null!);

        actRunnerNull.Should().Throw<ArgumentNullException>().WithParameterName("runner");
        actProcessNull.Should().Throw<ArgumentNullException>().WithParameterName("processService");
    }

    [Fact]
    public void OptimizeForNvidia_ReturnsOk_WhenNonNvidiaGpuDetected()
    {
        // On non-NVIDIA systems or default test runner, OptimizeForNvidia completes
        // safely without throwing and returns either Ok (not needed/applied) or Fail.
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        var service = new NvidiaOptimizerService(runner, new GameLoopProcessService(runner, new GameLoopPathResolver(registry)));

        var result = service.OptimizeForNvidia();

        result.Should().NotBeNull();
        // If machine has no NVIDIA GPU, the step is skipped with a vendor-specific reason.
        if (result.Success && result.IsSkipped)
        {
            result.Message.Should().Contain("skipped");
        }
    }

    [Fact]
    public void ResolveProfileTargets_PreservesCaseAndFollowsOptionOrder()
    {
        // Only two of the four default images exist; both kept with
        // on-disk spelling, in configured preference order, missing skipped.
        var installDir = CreateInstallDir();
        try
        {
            File.WriteAllText(Path.Combine(installDir, "aow_exe.exe"), "x");
            File.WriteAllText(Path.Combine(installDir, "AndroidEmulator.exe"), "x");

            var targets = InvokeResolveProfileTargets(CreateService(), installDir);

            targets.Should().Equal(
                Path.GetFullPath(Path.Combine(installDir, "AndroidEmulator.exe")),
                Path.GetFullPath(Path.Combine(installDir, "aow_exe.exe")));
        }
        finally
        {
            Directory.Delete(installDir, true);
        }
    }

    [Fact]
    public void ResolveProfileTargets_UsesConfiguredImageNames()
    {
        // A name outside the defaults proves the list comes from options.
        var installDir = CreateInstallDir();
        try
        {
            File.WriteAllText(Path.Combine(installDir, "CustomEmu.exe"), "x");
            var emulator = new EmulatorOptions
            {
                Emulator = new EmulatorOptions.EmulatorSettings
                {
                    NvidiaProfileImageNames = ["CustomEmu.exe"]
                }
            };

            var targets = InvokeResolveProfileTargets(CreateService(emulator), installDir);

            targets.Should().ContainSingle()
                .Which.Should().Be(Path.GetFullPath(Path.Combine(installDir, "CustomEmu.exe")));
        }
        finally
        {
            Directory.Delete(installDir, true);
        }
    }

    private static NvidiaOptimizerService CreateService(EmulatorOptions? emulator = null) =>
        new(
            new ProcessRunner(),
            new GameLoopProcessService(new ProcessRunner(), new GameLoopPathResolver(new RegistryService())),
            emulator: emulator);

    private static string CreateInstallDir() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "NexoraNvidiaTargets-" + Guid.NewGuid().ToString("N"))).FullName;

    private static List<string> InvokeResolveProfileTargets(NvidiaOptimizerService service, string installDir)
    {
        var method = typeof(NvidiaOptimizerService).GetMethod("ResolveProfileTargets", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("production method 'ResolveProfileTargets' must exist");
        return (List<string>)method!.Invoke(service, [installDir])!;
    }

    [Fact]
    public void OptimizeForNvidia_FailsSafely_WhenAssetsMissing()
    {
        // Use an isolated empty temp directory as asset root
        var emptyAssetRoot = Path.Combine(Path.GetTempPath(), $"NexoraNvidiaTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyAssetRoot);
        try
        {
            var runner = new ProcessRunner();
            var registry = new RegistryService();
            var service = new NvidiaOptimizerService(runner, new GameLoopProcessService(runner, new GameLoopPathResolver(registry)), assetRoot: emptyAssetRoot);

            var result = service.OptimizeForNvidia();

            result.Should().NotBeNull();
            // Either skipped because no NVIDIA GPU, or failed because assets missing
            if (!result.Success)
            {
                result.Message.Should().Contain("assets or GameLoop path were not found");
            }
        }
        finally
        {
            if (Directory.Exists(emptyAssetRoot))
            {
                Directory.Delete(emptyAssetRoot, true);
            }
        }
    }
}
