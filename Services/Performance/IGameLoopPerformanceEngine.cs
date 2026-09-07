using Nexora.Models;

namespace Nexora.Services.Performance;

/// <summary>
/// Stable seam for GameLoop performance work. UI code can depend on this
/// contract while implementation details evolve behind it.
/// </summary>
public interface IGameLoopPerformanceEngine
{
    HardwareSnapshot GetHardwareSnapshot();

    OptimizerPlan GetRecommendedPlan(HardwareSnapshot hardware);

    OperationResult ApplySmartSettings();

    OperationResult OptimizeGameLoop();

    OperationResult OptimizeAll();

    OperationResult ApplyPerformanceSession();

    OperationResult RestorePerformanceSession();
}
