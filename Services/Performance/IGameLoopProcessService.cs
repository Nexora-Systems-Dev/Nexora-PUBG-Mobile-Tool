using System.Diagnostics;
using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

/// <summary>
/// Contract for discovering, validating, and terminating GameLoop emulator processes.
/// </summary>
public interface IGameLoopProcessService
{
    OperationResult KillGameLoopProcesses(CancellationToken cancellationToken = default);

    List<Process> FindGameLoopProcesses(string? gameLoopRoot = null);

    string? GetGameLoopRootFromRegistry();

    string? GetGameLoopRoot();

    string? GetGameLoopUiPath();
}
