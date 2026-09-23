namespace Nexora.Infrastructure.GameLoop;

/// <summary>
/// Resolves GameLoop emulator installation paths using the
/// custom-override → registry → running-process →
/// ProgramFiles discovery pipeline.
/// </summary>
public interface IGameLoopPathResolver
{
    string? GetRootFromRegistry();

    string? GetRoot();

    string? GetUiPath();

    string? GetAppMarketPath();

    bool IsGameLoopPath(string? executablePath, string? gameLoopRoot);
}
