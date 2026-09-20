using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Nexora.Tests.Architecture;

/// <summary>
/// Phase 4 acceptance gate as executable guards: the structural invariants
/// the refactor established, scanned straight from the production sources so
/// they can never silently regress. All scans run over the files beside
/// <c>Nexora.slnx</c> (found by walking up from the test binaries),
/// excluding the test project itself and all build output.
/// </summary>
public sealed class ArchitectureGuardTests
{
    [Fact]
    public void NoDuplicateServiceRegistrations()
    {
        var bootstrap = ReadProductionFile("Bootstrap", "ServiceCollectionExtensions.cs");
        var registrations = Regex.Matches(bootstrap, @"Add(?:Singleton|Transient|Scoped)\s*<\s*([A-Za-z0-9_]+)")
            .Select(m => m.Groups[1].Value)
            .ToList();

        registrations.Should().NotBeEmpty();
        registrations.Should().OnlyHaveUniqueItems(
            "every service gets exactly one registration — a second AddSingleton<Iface, Impl> would fork singletons like the GameLoopService gate/session");
    }

    [Fact]
    public void ProcessDiscoveryHasASingleHome()
    {
        var offenders = ProductionFiles()
            .Where(file => LinesWithoutComments(file).Any(line => line.Contains("GetProcessesByName"))
                && !file.EndsWith("GameLoopProcessEnumerator.cs", StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty(
            "Process.GetProcessesByName may only live in GameLoopProcessEnumerator (consumed via IGameLoopProcessService)");
    }

    [Fact]
    public void NoBlockingWaits_AndAsyncVoidOnlyInWpfHandlers()
    {
        var blockers = new List<string>();
        var badVoids = new List<string>();
        foreach (var file in ProductionFiles())
        {
            foreach (var line in LinesWithoutComments(file))
            {
                // Task-blocking takes the shape <task>.Result / <call>().Result /
                // <task>.Wait() — a domain member merely named Result (e.g.
                // PerformanceStep.Result, an OperationResult) is not a block.
                if (Regex.IsMatch(line, @"(\(\)|\b[Tt]ask\w*)\.Result\b|(?<!WaitOne)\.Wait\(\)"))
                    blockers.Add($"{Short(file)}: {line.Trim()}");
                if (line.Contains("async void") && !line.Contains("EventArgs"))
                    badVoids.Add($"{Short(file)}: {line.Trim()}");
            }
        }

        blockers.Should().BeEmpty("cancellable async APIs must be awaited, never blocked on");
        badVoids.Should().BeEmpty("async void is only legal in WPF event handlers");
    }

    [Fact]
    public void NoHardcodedDriveLetterPaths()
    {
        var offenders = new List<string>();
        foreach (var file in ProductionFiles())
        {
            foreach (var line in LinesWithoutComments(file))
            {
                if (Regex.IsMatch(line, "\"[A-Za-z]:\\\\|@\"[A-Za-z]:"))
                    offenders.Add($"{Short(file)}: {line.Trim()}");
            }
        }

        offenders.Should().BeEmpty(
            "GameLoop paths resolve through IGameLoopPathResolver — never a hardcoded path or drive letter");
    }

    [Fact]
    public void PresentationMakesNoDirectSystemCalls()
    {
        var offenders = new List<string>();
        foreach (var file in ProductionFiles().Where(IsPresentationFile))
        {
            foreach (var line in LinesWithoutComments(file))
            {
                if (line.Contains("Microsoft.Win32")
                    || line.Contains("RunPowerShell")
                    || line.Contains("StartDetachedElevated")
                    || line.Contains("Process.Start")
                    || (line.Contains(".Run(") && !line.Contains("Task.Run(")))
                {
                    offenders.Add($"{Short(file)}: {line.Trim()}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "views and ViewModels delegate to injected services (registry/process/ADB/PowerShell stay behind their abstractions); " +
            "the designer-fallback ctors may construct those services but never execute through them, " +
            "and Task.Run offloading is not a system call");
    }

    private static bool IsPresentationFile(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}Presentation{Path.DirectorySeparatorChar}")
        || file.Contains($"{Path.DirectorySeparatorChar}UI{Path.DirectorySeparatorChar}");

    private static string ReadProductionFile(params string[] relative)
    {
        var path = Path.Combine([RepoRoot(), .. relative]);
        File.Exists(path).Should().BeTrue($"expected production file {string.Join("/", relative)}");
        return File.ReadAllText(path);
    }

    private static IEnumerable<string> ProductionFiles() =>
        Directory.EnumerateFiles(RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains("Nexora.Tests")
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static IEnumerable<string> LinesWithoutComments(string file)
    {
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw;
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment];
            if (!string.IsNullOrWhiteSpace(line)) yield return line;
        }
    }

    private static string Short(string file) =>
        Path.GetRelativePath(RepoRoot(), file);

    private static string? _repoRoot;

    private static string RepoRoot()
    {
        if (_repoRoot is not null) return _repoRoot;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Nexora.slnx")))
            {
                _repoRoot = directory.FullName;
                return _repoRoot;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate Nexora.slnx above the test binaries.");
    }
}
