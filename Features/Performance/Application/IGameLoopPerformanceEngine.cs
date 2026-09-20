using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Performance.Application;

/// <summary>
/// Contract for GameLoop hardware inspection, optimization plans, and performance sessions.
/// </summary>
public interface IGameLoopPerformanceEngine
{
    HardwareSnapshot GetHardwareSnapshot();

    Task<HardwareSnapshot> GetHardwareSnapshotAsync(CancellationToken cancellationToken = default);

    OptimizerPlan GetRecommendedPlan(HardwareSnapshot hardware);

    OperationResult ApplySmartSettings();

    OperationResult OptimizeGameLoop();

    Task<OperationResult> OptimizeAllAsync(CancellationToken cancellationToken = default);

    OperationResult ApplyPerformanceSession();

    OperationResult RestorePerformanceSession();

    Task<OperationResult> RestorePerformanceSessionAsync(CancellationToken cancellationToken = default);
}
