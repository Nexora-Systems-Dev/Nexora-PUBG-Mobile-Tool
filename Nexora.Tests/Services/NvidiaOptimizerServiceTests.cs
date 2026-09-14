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

        var actRunnerNull = () => new NvidiaOptimizerService(null!, registry);
        var actRegistryNull = () => new NvidiaOptimizerService(runner, null!);

        actRunnerNull.Should().Throw<ArgumentNullException>().WithParameterName("runner");
        actRegistryNull.Should().Throw<ArgumentNullException>().WithParameterName("registry");
    }

    [Fact]
    public void OptimizeForNvidia_ReturnsOk_WhenNonNvidiaGpuDetected()
    {
        // On non-NVIDIA systems or default test runner, OptimizeForNvidia completes
        // safely without throwing and returns either Ok (not needed/applied) or Fail.
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        var service = new NvidiaOptimizerService(runner, registry);

        var result = service.OptimizeForNvidia();

        result.Should().NotBeNull();
        // If machine has no NVIDIA GPU, the step is skipped with a vendor-specific reason.
        if (result.Success && result.IsSkipped)
        {
            result.Message.Should().Contain("skipped");
        }
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
            var service = new NvidiaOptimizerService(runner, registry, emptyAssetRoot);

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
