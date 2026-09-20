using System.Diagnostics;
using Nexora.Shared.Kernel;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Performance.Application;

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
