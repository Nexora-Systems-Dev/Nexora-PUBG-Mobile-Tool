using Nexora.Configuration;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Features.Security.Infrastructure;
using Nexora.Features.Optimizer.Application;
using Nexora.Shared.Kernel;
using Nexora.Infrastructure.Registry;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Performance.Infrastructure;

/// <summary>
/// Facade coordinating GameLoop hardware inspection, optimization plans,
/// and performance sessions.
/// </summary>
public sealed class PerformanceEngineFacade : IGameLoopPerformanceEngine
{
    private readonly HardwareDetectionService _hardwareDetection;
    private readonly PerformancePlanBuilder _planBuilder;
    private readonly PowerSessionService _powerSession;
    private readonly ProcessPriorityService _processPriority;
    private readonly ITempCleanupService _tempCleanup;
    private readonly IGameLoopProcessService _processService;
    private readonly NvidiaOptimizerService _nvidiaOptimizer;
    private readonly DefenderExclusionService _defenderExclusion;
    private readonly GameLoopRegistryOptimizer _registryOptimizer;

    public PerformanceEngineFacade(
        IProcessRunner runner,
        IUserRegistry userRegistry,
        IMachineRegistry machineRegistry,
        IGameLoopProcessService processService,
        ITempCleanupService tempCleanup,
        ProcessPriorityService processPriority,
        GameLoopOptions? gameLoop = null,
        EmulatorOptions? emulator = null)
    {
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        if (userRegistry is null) throw new ArgumentNullException(nameof(userRegistry));
        if (machineRegistry is null) throw new ArgumentNullException(nameof(machineRegistry));
        if (processService is null) throw new ArgumentNullException(nameof(processService));
        if (tempCleanup is null) throw new ArgumentNullException(nameof(tempCleanup));
        if (processPriority is null) throw new ArgumentNullException(nameof(processPriority));

        var gameLoopOptions = gameLoop ?? new GameLoopOptions();
        var emulatorOptions = emulator ?? new EmulatorOptions();
        var assetRoot = Path.Combine(AppContext.BaseDirectory, emulatorOptions.Assets.DirectoryName);
        _processService = processService;
        _hardwareDetection = new HardwareDetectionService(runner, gameLoopOptions);
        _planBuilder = new PerformancePlanBuilder();
        var gpuRouting = new GpuRoutingService(userRegistry, emulatorOptions);
        _powerSession = new PowerSessionService(runner);
        _processPriority = processPriority;
        _tempCleanup = tempCleanup;
        _nvidiaOptimizer = new NvidiaOptimizerService(runner, _processService, assetRoot, emulatorOptions, gameLoopOptions);
        _defenderExclusion = new DefenderExclusionService(runner, _processService, emulatorOptions);
        _registryOptimizer = new GameLoopRegistryOptimizer(userRegistry, machineRegistry, _processService, gpuRouting, gameLoopOptions, emulatorOptions);
    }

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
        var hardware = GetHardwareSnapshot();
        var report = PerformanceExecutionReport.Create(
            ("Windows power policy", _powerSession.Apply(hardware)),
            ("GameLoop registry and GPU routing", OptimizeGameLoopRegistry()),
            ("GameLoop runtime priority", _processPriority.Apply(_processService.GetGameLoopRoot())),
            ("NVIDIA profile", OptimizeForNvidia()),
            ("Defender exclusion", AddDefenderExclusion()));

        return report.ToDetailedResult("Windows and GPU boost finished");
    }

    public async Task<OperationResult> OptimizeAllAsync(CancellationToken cancellationToken = default)
    {
        // Temp cleanup is the only async step; the remaining optimizers are
        // sync-native. The report builder takes completed results, so each
        // sync step runs inline after the single await. Cancellation is
        // honored between steps so a cancel never starts new system writes.
        var tempResult = await _tempCleanup.CleanTempAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var smartResult = ApplySmartSettings();
        cancellationToken.ThrowIfCancellationRequested();
        var registryResult = OptimizeGameLoopRegistry();
        cancellationToken.ThrowIfCancellationRequested();
        var priorityResult = _processPriority.Apply(_processService.GetGameLoopRoot());
        cancellationToken.ThrowIfCancellationRequested();
        var nvidiaResult = OptimizeForNvidia();
        cancellationToken.ThrowIfCancellationRequested();
        var defenderResult = AddDefenderExclusion();
        var report = PerformanceExecutionReport.Create(
            ("Smart settings", smartResult),
            ("GameLoop registry and GPU routing", registryResult),
            ("GameLoop runtime priority", priorityResult),
            ("NVIDIA profile", nvidiaResult),
            ("Defender exclusion", defenderResult),
            ("Temp cleanup", tempResult));

        return report.ToDetailedResult("All recommended settings finished");
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

    public async Task<OperationResult> RestorePerformanceSessionAsync(CancellationToken cancellationToken = default)
    {
        // Shutdown-safe path: await the bounded 2s monitor stop via
        // RestoreAsync instead of fire-and-forget Stop(). Power restore is
        // sync-native and bounded by the process runner timeout.
        // Note: registry / GPU / NVIDIA / Defender optimizers are permanent
        // user-triggered tuning (not session state) — only power policy and
        // runtime priority are restored here.
        cancellationToken.ThrowIfCancellationRequested();
        var priorityResult = await _processPriority.RestoreAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var powerResult = _powerSession.Restore();
        var report = PerformanceExecutionReport.Create(
            ("Power policy restore", powerResult),
            ("GameLoop runtime priority restore", priorityResult));

        return report.ToOperationResult(
            "Previous Windows power mode and GameLoop priorities restored.",
            "Performance Session restore completed with issues.");
    }

    private OperationResult OptimizeGameLoopRegistry() => _registryOptimizer.OptimizeGameLoopRegistry();

    private OperationResult OptimizeForNvidia() => _nvidiaOptimizer.OptimizeForNvidia();

    private OperationResult AddDefenderExclusion() => _defenderExclusion.AddDefenderExclusion();
}
