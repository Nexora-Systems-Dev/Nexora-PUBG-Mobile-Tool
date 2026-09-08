using Nexora.Features.Performance;
using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

/// <summary>
/// Stable seam for GameLoop performance work. UI code can depend on this
/// contract while implementation details evolve behind it.
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
