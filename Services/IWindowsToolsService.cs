using Nexora.Services.Performance;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

/// <summary>
/// Facade contract coordinating Windows system tools, performance tuning, and GameLoop optimization services.
/// Extends <see cref="IGameLoopPerformanceEngine"/>.
/// </summary>
public interface IWindowsToolsService : IGameLoopPerformanceEngine
{
    OperationResult CleanTemp();

    Task<OperationResult> CleanTempAsync(CancellationToken cancellationToken = default);

    OperationResult ChangeDns(string primary, string secondary);

    int? PingDns(string host);

    Task<int?> PingDnsAsync(string host, CancellationToken cancellationToken = default);

    OperationResult CreateShortcut(string displayName, string packageName);

    OperationResult SetIpadResolution(int width, int height);

    OperationResult ResetIpadResolution();

    Task<OperationResult> KillGameLoopProcessesAsync(CancellationToken cancellationToken);

    OperationResult KillGameLoopProcesses(CancellationToken cancellationToken = default);

    OperationResult OptimizeGameLoopRegistry();

    OperationResult OptimizeForNvidia();

    OperationResult AddDefenderExclusion();
}
