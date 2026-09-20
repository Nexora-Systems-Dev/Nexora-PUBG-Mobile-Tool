using Nexora.Shared.Kernel;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Optimizer.Application;

/// <summary>
/// Contract for temporary file, prefetch, and shader cache cleanup operations.
/// </summary>
public interface ITempCleanupService
{
    Task<OperationResult> CleanTempAsync(CancellationToken cancellationToken = default);
}
