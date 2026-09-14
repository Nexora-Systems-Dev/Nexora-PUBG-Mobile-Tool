using Nexora.Features.Performance;
using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

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

    OperationResult OptimizeAll();

    OperationResult ApplyPerformanceSession();

    OperationResult RestorePerformanceSession();

    Task<OperationResult> RestorePerformanceSessionAsync(CancellationToken cancellationToken = default);
}
