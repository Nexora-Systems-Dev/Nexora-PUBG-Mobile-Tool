using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Features.Optimizer.Application;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Infrastructure.Files;
using Nexora.Infrastructure.GameLoop;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;
using Nexora.Shared.Contracts;
using Nexora.UI.Presentation;

namespace Nexora.Features.Optimizer.Presentation;

/// <summary>
/// The Optimizer page: hardware profile tiles, the recommended plan, the
/// maintenance/boost/session tools and the activity surface. Thin by design —
/// every action delegates to <see cref="OptimizerViewModel"/> and every
/// display state is painted from the returned outcome below. No planning or
/// execution logic lives here (decision D1); even the report rendering only
/// formats the engine's own result.
/// </summary>
public partial class OptimizerView : UserControl
{
    private readonly OptimizerViewModel _viewModel;

    public OptimizerView()
        : this(engine: null, tempCleanup: null, processService: null, operationBus: null)
    {
    }

    public OptimizerView(
        IGameLoopPerformanceEngine? engine = null,
        ITempCleanupService? tempCleanup = null,
        IGameLoopProcessService? processService = null,
        IPageOperationBus? operationBus = null)
    {
        InitializeComponent();

        _viewModel = BuildViewModel(engine, tempCleanup, processService, operationBus);
        _viewModel.StatusChanged += (message, isError) => StatusChanged?.Invoke(message, isError);
    }

    /// <summary>
    /// The page's orchestration seam. Exposed for the shell, which refreshes
    /// the profile on startup and forwards statuses to the window status bar.
    /// </summary>
    public OptimizerViewModel ViewModel => _viewModel;

    /// <summary>
    /// Re-emitted for the shell, which owns the window status bar: the
    /// "Working..." line from the ViewModel and every tool's result line.
    /// The page's own status/activity cells are painted by each handler from
    /// its returned outcome.
    /// </summary>
    public event Action<string, bool>? StatusChanged;

    /// <summary>
    /// Reloads the hardware snapshot and its recommended plan. Called by the
    /// shell on startup and after the operations that change what the plan
    /// recommends; a busy bus makes it a no-op.
    /// </summary>
    public async Task RefreshProfileAsync()
    {
        if (_viewModel.IsBusy || RefreshOptimizerButton is null) return;
        RefreshOptimizerButton.IsEnabled = false;
        try
        {
            var outcome = await _viewModel.RefreshProfileAsync();
            // The startup refresh runs beside the update check and can settle
            // after the update handoff has closed the window; a dead
            // dispatcher must never touch the visual tree (QA F-006).
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            if (outcome is null) return;
            if (outcome.ErrorMessage is not null)
            {
                OptimizerStatusText.Text = outcome.ErrorMessage;
                OptimizerStatusText.Foreground = GetBrush("Danger");
                return;
            }

            PaintPanel(outcome.Display!);
        }
        finally
        {
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                RefreshOptimizerButton.IsEnabled = true;
        }
    }

    private static OptimizerViewModel BuildViewModel(
        IGameLoopPerformanceEngine? engine,
        ITempCleanupService? tempCleanup,
        IGameLoopProcessService? processService,
        IPageOperationBus? operationBus)
    {
        // XAML constructs this view with the parameterless ctor, so the view
        // takes its ViewModel from the running container when there is one
        // and only falls back to a locally built graph for the designer and
        // for direct construction. The fallback mirrors the container's
        // singletons: one registry, one process service, one temp cleanup,
        // one shared priority store behind the engine.
        if (engine is null && tempCleanup is null && processService is null && operationBus is null
            && System.Windows.Application.Current is App && App.Services is IServiceProvider services)
        {
            if (services.GetService<OptimizerViewModel>() is { } resolved) return resolved;
        }

        var registry = new RegistryService();
        var runner = new ProcessRunner();
        var pathResolver = new GameLoopPathResolver(registry);
        var processSvc = processService ?? new GameLoopProcessService(runner, pathResolver);
        var tempSvc = tempCleanup ?? new TempCleanupService(registry);
        var priorityStore = new ProcessPrioritySnapshotStore();
        var priorityApplier = new ProcessPriorityApplier(priorityStore, processSvc);
        var processPriority = new ProcessPriorityService(priorityStore, priorityApplier, new ProcessPriorityMonitor(priorityStore, priorityApplier));

        return new OptimizerViewModel(
            engine ?? new PerformanceEngineFacade(runner, registry, registry, processSvc, tempSvc, processPriority),
            tempSvc,
            processSvc,
            operationBus ?? new PageOperationBus());
    }

    private async void TempCleanerButton_Click(object sender, RoutedEventArgs e) =>
        await RunOptimizerToolAsync(TempCleanerButton, ct => _viewModel.TempCleanup.CleanTempAsync(ct), refreshAfter: false);

    private async void SmartSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOptimizerToolAsync(SmartSettingsButton, ct => Task.Run(() => _viewModel.Engine.ApplySmartSettings(), ct), refreshAfter: true);
    }

    private async void GameLoopOptimizerButton_Click(object sender, RoutedEventArgs e) =>
        await RunOptimizerToolAsync(GameLoopOptimizerButton, ct => Task.Run(() => _viewModel.Engine.OptimizeGameLoop(), ct), refreshAfter: false);

    private async void AllRecommendedButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOptimizerToolAsync(AllRecommendedButton, ct => _viewModel.Engine.OptimizeAllAsync(ct), refreshAfter: true);
    }

    private async void ForceCloseButton_Click(object sender, RoutedEventArgs e) =>
        await RunOptimizerToolAsync(ForceCloseButton, ct => Task.Run(() => _viewModel.ProcessService.KillGameLoopProcesses(), ct), refreshAfter: false);

    private async void PerformanceSessionButton_Click(object sender, RoutedEventArgs e) =>
        await RunOptimizerToolAsync(PerformanceSessionButton, ct => Task.Run(() => _viewModel.Engine.ApplyPerformanceSession(), ct), refreshAfter: false);

    private async void RestoreSessionButton_Click(object sender, RoutedEventArgs e) =>
        await RunOptimizerToolAsync(RestoreSessionButton, ct => _viewModel.Engine.RestorePerformanceSessionAsync(ct), refreshAfter: false);

    private async void RefreshOptimizerButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshProfileAsync();

    /// <summary>
    /// The page half of the tool execution the shell's <c>RunToolAsync</c>
    /// used to own for this page: dim the button, paint the working state,
    /// run through the ViewModel's bus-guarded core, render the activity
    /// report, and forward the status line to the shell. A refused bus still
    /// flows through the report paint, exactly as the pre-extraction helper did.
    /// </summary>
    private async Task RunOptimizerToolAsync(Button button, Func<CancellationToken, Task<OperationResult>> action, bool refreshAfter)
    {
        button.IsEnabled = false;
        OptimizerStatusText.Text = "Working...";
        OptimizerStatusText.Foreground = GetBrush("TextSecondary");
        OptimizerActivityText.Text = "Running each boost step...";
        OptimizerActivityText.Foreground = GetBrush("TextSecondary");

        OperationResult result;
        try
        {
            result = await _viewModel.ExecuteAsync(action);
        }
        finally
        {
            button.IsEnabled = true;
        }

        RenderOptimizerReport(result);
        StatusChanged?.Invoke(ActivityReportFormatter.Format(result).StatusLine, !result.Success);

        if (refreshAfter) await RefreshProfileAsync();
    }

    private void RenderOptimizerReport(OperationResult result)
    {
        var display = ActivityReportFormatter.Format(result);
        OptimizerStatusText.Text = display.StatusLine;
        OptimizerActivityText.Text = string.IsNullOrWhiteSpace(display.Details)
            ? "No step details."
            : display.Details;
        OptimizerActivityText.Foreground = GetBrush(display.IsError ? "Danger" : "TextSecondary");

        var statusBrush = result.Success
            ? result.IsSkipped ? "Accent" : "Success"
            : "Danger";
        OptimizerStatusText.Foreground = GetBrush(statusBrush);
    }

    private void PaintPanel(OptimizerDisplayModel display)
    {
        HardwareProfileText.Text = display.HardwareProfile;
        HardwareGpuText.Text = display.HardwareGpu;
        HardwareCpuText.Text = display.HardwareCpu;
        HardwareMemoryText.Text = display.HardwareMemory;
        HardwareDisplayText.Text = display.HardwareDisplay;
        HardwarePowerText.Text = display.HardwarePower;
        HardwareVirtualizationText.Text = display.HardwareVirtualization;

        PlanTierText.Text = display.PlanTier;
        PlanCpuText.Text = display.PlanCpu;
        PlanMemoryText.Text = display.PlanMemory;
        PlanRenderText.Text = display.PlanRender;
        PlanFpsText.Text = display.PlanFps;
        PlanGpuText.Text = display.PlanGpu;
        SmartPlanText.Text = display.SmartPlanSummary;
    }

    private Brush? GetBrush(string key) => FindResource(key) as Brush;
}
