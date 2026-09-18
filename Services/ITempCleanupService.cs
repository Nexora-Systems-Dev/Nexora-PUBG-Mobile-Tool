using Nexora.Shared.Kernel;

namespace Nexora.Services;

/// <summary>
/// Contract for temporary file, prefetch, and shader cache cleanup operations.
/// </summary>
public interface ITempCleanupService
{
    Task<OperationResult> CleanTempAsync(CancellationToken cancellationToken = default);
}
