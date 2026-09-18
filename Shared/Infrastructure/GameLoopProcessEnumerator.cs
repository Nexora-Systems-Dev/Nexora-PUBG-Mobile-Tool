using System.Diagnostics;

namespace Nexora.Shared.Infrastructure;

/// <summary>
/// Single home for GameLoop process enumeration. All production
/// <c>Process.GetProcessesByName</c> calls go through here so the resolver
/// and the killer can never drift apart.
/// </summary>
public static class GameLoopProcessEnumerator
{
    /// <summary>
    /// Enumerates running processes for the given base names (with or without .exe).
    /// Enumeration errors are swallowed per-name; caller owns disposal.
    /// </summary>
    public static IEnumerable<Process> EnumerateByNames(IEnumerable<string> baseNames)
    {
        foreach (var raw in baseNames)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(raw.Trim());
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            Process[] found;
            try
            {
                found = Process.GetProcessesByName(name);
            }
            catch
            {
                // Ignore enumeration errors for this name.
                continue;
            }

            foreach (var process in found)
            {
                yield return process;
            }
        }
    }
}
