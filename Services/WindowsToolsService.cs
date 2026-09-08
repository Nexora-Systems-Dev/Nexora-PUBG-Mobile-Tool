using Nexora.Configuration;
using Nexora.Features.Performance;
using Nexora.Services.Performance;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

/// <summary>
/// Facade coordinating Windows system tools, performance tuning, and GameLoop optimization services.
/// Implements <see cref="IGameLoopPerformanceEngine"/> for performance plan execution.
/// </summary>
public sealed class WindowsToolsService : IWindowsToolsService
{
    private readonly ProcessRunner _runner;
    private readonly RegistryService _registry;
    private readonly IpadLayoutService _ipadLayout;
    private readonly HardwareDetectionService _hardwareDetection;
    private readonly PerformancePlanBuilder _planBuilder;
    private readonly PowerSessionService _powerSession;
    private readonly ProcessPriorityService _processPriority;
    private readonly NetworkToolsService _networkTools;
    private readonly ShortcutService _shortcuts;
    private readonly TempCleanupService _tempCleanup;
    private readonly GameLoopProcessService _processService;
    private readonly NvidiaOptimizerService _nvidiaOptimizer;
    private readonly DefenderExclusionService _defenderExclusion;
    private readonly GameLoopRegistryOptimizer _registryOptimizer;

    public WindowsToolsService(
        IProcessRunner runner,
        IRegistryService registry,
        TempCleanupOptions? tempCleanupOptions = null,
        IpadLayoutOptions? ipadLayoutOptions = null)
    {
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        _runner = runner as ProcessRunner ?? new ProcessRunner();
        _registry = registry as RegistryService ?? new RegistryService();

        var assetRoot = Path.Combine(AppContext.BaseDirectory, AppConstants.Assets.DirectoryName);
        _ipadLayout = new IpadLayoutService(_registry, ipadLayoutOptions);
        _hardwareDetection = new HardwareDetectionService(_runner);
        _planBuilder = new PerformancePlanBuilder();
        var gpuRouting = new GpuRoutingService(_registry);
        _powerSession = new PowerSessionService(_runner);
        _processPriority = new ProcessPriorityService();
        _networkTools = new NetworkToolsService(_runner);
        _shortcuts = new ShortcutService(_runner, _registry, assetRoot);
        _tempCleanup = new TempCleanupService(_registry, tempCleanupOptions);
        _processService = new GameLoopProcessService(_runner, _registry);
        _nvidiaOptimizer = new NvidiaOptimizerService(_runner, _registry, assetRoot);
        _defenderExclusion = new DefenderExclusionService(_runner, _registry);
        _registryOptimizer = new GameLoopRegistryOptimizer(_runner, _registry, gpuRouting);
    }

    #region IGameLoopPerformanceEngine Implementation

    public HardwareSnapshot GetHardwareSnapshot() => _hardwareDetection.GetSnapshot();

    public Task<HardwareSnapshot> GetHardwareSnapshotAsync(CancellationToken cancellationToken = default) =>
        _hardwareDetection.GetSnapshotAsync(cancellationToken);

    public OptimizerPlan GetRecommendedPlan(HardwareSnapshot hardware) => _planBuilder.Build(hardware);

    public OperationResult ApplySmartSettings()
    {
        var hardware = GetHardwareSnapshot();
        var plan = GetRecommendedPlan(hardware);
        return _registryOptimizer.ApplySmartSettings(hardware, plan);
    }

    public OperationResult OptimizeGameLoop()
    {
        var report = PerformanceExecutionReport.Create(
            ("GameLoop registry and GPU routing", OptimizeGameLoopRegistry()),
            ("GameLoop runtime priority", _processPriority.Apply(_processService.GetGameLoopRoot())),
            ("NVIDIA profile", OptimizeForNvidia()),
            ("Defender exclusion", AddDefenderExclusion()));

        return report.ToOperationResult(
            "Windows and GPU boost applied successfully.",
            "Windows and GPU boost completed with issues.");
    }

    public OperationResult OptimizeAll()
    {
        var report = PerformanceExecutionReport.Create(
            ("Smart settings", ApplySmartSettings()),
            ("GameLoop registry and GPU routing", OptimizeGameLoopRegistry()),
            ("GameLoop runtime priority", _processPriority.Apply(_processService.GetGameLoopRoot())),
            ("NVIDIA profile", OptimizeForNvidia()),
            ("Defender exclusion", AddDefenderExclusion()),
            ("Temp cleanup", CleanTemp()));

        return report.ToOperationResult(
            "All recommended settings applied successfully.",
            "Optimizer completed with issues.");
    }

    public OperationResult ApplyPerformanceSession()
    {
        var hardware = GetHardwareSnapshot();
        var gameLoopRoot = _processService.GetGameLoopRoot();
        var report = PerformanceExecutionReport.Create(
            ("Power policy", _powerSession.Apply(hardware)),
            ("GameLoop runtime priority", _processPriority.Apply(gameLoopRoot)));

        return report.ToOperationResult(
            "Performance Session active: Windows power policy and GameLoop runtime priority tuned.",
            "Performance Session completed with issues.");
    }

    public OperationResult RestorePerformanceSession()
    {
        var report = PerformanceExecutionReport.Create(
            ("Power policy restore", _powerSession.Restore()),
            ("GameLoop runtime priority restore", _processPriority.Restore()));

        return report.ToOperationResult(
            "Previous Windows power mode and GameLoop priorities restored.",
            "Performance Session restore completed with issues.");
    }

    public Task<OperationResult> RestorePerformanceSessionAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return RestorePerformanceSession();
        }, cancellationToken);

    #endregion

    #region System and Emulator Tools Facade

    public OperationResult CleanTemp() => _tempCleanup.CleanTemp();

    public Task<OperationResult> CleanTempAsync(CancellationToken cancellationToken = default) =>
        _tempCleanup.CleanTempAsync(cancellationToken);

    public OperationResult ChangeDns(string primary, string secondary) => _networkTools.ChangeDns(primary, secondary);

    public int? PingDns(string host) => _networkTools.PingDns(host);

    public Task<int?> PingDnsAsync(string host, CancellationToken cancellationToken = default) =>
        _networkTools.PingDnsAsync(host, cancellationToken);

    public OperationResult CreateShortcut(string displayName, string packageName) => _shortcuts.CreateShortcut(displayName, packageName);

    public OperationResult SetIpadResolution(int width, int height)
    {
        var running = _processService.FindGameLoopProcesses();
        if (running.Count > 0)
        {
            var names = string.Join(", ", running.Select(process => $"{process.ProcessName}.exe").Distinct(StringComparer.OrdinalIgnoreCase));
            foreach (var process in running)
            {
                process.Dispose();
            }

            return OperationResult.Fail($"Close GameLoop before applying iPad View ({names}), then apply it again.");
        }

        return _ipadLayout.Apply(width, height);
    }

    public OperationResult ResetIpadResolution() => _ipadLayout.Reset();

    public Task<OperationResult> KillGameLoopProcessesAsync(CancellationToken cancellationToken) =>
        _processService.KillGameLoopProcessesAsync(cancellationToken);

    public OperationResult KillGameLoopProcesses(CancellationToken cancellationToken = default) =>
        _processService.KillGameLoopProcesses(cancellationToken);

    public OperationResult OptimizeGameLoopRegistry() => _registryOptimizer.OptimizeGameLoopRegistry();

    public OperationResult OptimizeForNvidia() => _nvidiaOptimizer.OptimizeForNvidia();

    public OperationResult AddDefenderExclusion() => _defenderExclusion.AddDefenderExclusion();

    #endregion
}
