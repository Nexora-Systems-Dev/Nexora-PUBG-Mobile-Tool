using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Features.Tuning.Application;
using Nexora.Features.Tuning.Domain;
using Nexora.Infrastructure.GameLoop;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;
using Nexora.UI.Presentation;

namespace Nexora.Features.Tuning.Presentation;

/// <summary>
/// The Tuning page: CPU/memory sliders, DPI, the six emulator toggles and the
/// Apply/End Task actions. Thin by design — every action delegates to
/// <see cref="TuningViewModel"/> and every display state comes back through
/// one of the render methods below. No registry, process, or ADB calls live
/// here; even the running-state guard is only painted, never decided.
/// </summary>
public partial class TuningView : UserControl
{
    private readonly TuningViewModel _viewModel;

    public TuningView()
        : this(tuning: null, processService: null, operationBus: null)
    {
    }

    public TuningView(
        IEmulatorSettingsService? tuning = null,
        IGameLoopProcessService? processService = null,
        IPageOperationBus? operationBus = null)
    {
        InitializeComponent();

        _viewModel = BuildViewModel(tuning, processService, operationBus);
        _viewModel.StateLoaded += OnStateLoaded;
        _viewModel.LoadingChanged += OnLoadingChanged;
        _viewModel.BusyVisualChanged += OnBusyVisualChanged;
        _viewModel.StatusChanged += OnStatusChanged;

        TuningDpiComboBox.ItemsSource = EmulatorTuningCatalog.DpiOptions;
        TuningDpiComboBox.SelectedItem = EmulatorTuningCatalog.DefaultDpi;
        UpdateLabels();
    }

    /// <summary>
    /// The page's orchestration seam. Exposed for the shell, which refreshes
    /// the page on navigation and forwards statuses to the window status bar.
    /// </summary>
    public TuningViewModel ViewModel => _viewModel;

    /// <summary>
    /// Reloads the emulator's current settings. Called by the shell when the
    /// page is navigated to; a busy bus makes it a no-op.
    /// </summary>
    public Task RefreshAsync() => _viewModel.RefreshAsync();

    private static TuningViewModel BuildViewModel(
        IEmulatorSettingsService? tuning,
        IGameLoopProcessService? processService,
        IPageOperationBus? operationBus)
    {
        // XAML constructs this view with the parameterless ctor, so the view
        // takes its ViewModel from the running container when there is one
        // and only falls back to a locally built graph for the designer and
        // for direct construction. The fallback mirrors the container's
        // singletons: one registry, one process service.
        if (tuning is null && processService is null && operationBus is null
            && System.Windows.Application.Current is App && App.Services is IServiceProvider services)
        {
            if (services.GetService<TuningViewModel>() is { } resolved) return resolved;
        }

        var registry = new RegistryService();
        var runner = new ProcessRunner();
        var pathResolver = new GameLoopPathResolver(registry);
        var processSvc = processService ?? new GameLoopProcessService(runner, pathResolver);

        return new TuningViewModel(
            tuning ?? new EmulatorSettingsService(registry, processSvc),
            processSvc,
            operationBus ?? new PageOperationBus());
    }

    /// <summary>
    /// Repaints every control from a freshly loaded state, exactly as the
    /// pre-extraction refresh did: values first, labels second, then the
    /// running-state guard that gates Apply.
    /// </summary>
    private void OnStateLoaded(EmulatorTuningState state)
    {
        TuningCpuSlider.Value = state.Selection.CpuCores;
        TuningMemorySlider.Value = state.Selection.MemoryMb;
        TuningDpiComboBox.SelectedItem = state.Selection.Dpi;
        TuningRenderCacheCheck.IsChecked = state.Selection.RenderCacheEnabled;
        TuningGlobalCacheCheck.IsChecked = state.Selection.GlobalCacheEnabled;
        TuningDiscreteGpuCheck.IsChecked = state.Selection.DiscreteGpuEnabled;
        TuningRenderOptimizeCheck.IsChecked = state.Selection.RenderOptimizeEnabled;
        TuningVSyncCheck.IsChecked = state.Selection.VSyncEnabled;
        TuningAdbCheck.IsChecked = state.Selection.AdbEnabled;
        TuningAntiAliasingCheck.IsChecked = state.Selection.AntiAliasingEnabled;
        UpdateLabels();

        if (state.IsGameLoopRunning)
        {
            var names = string.Join(", ", state.RunningProcessNames);
            TuningNoticeText.Text = $"GameLoop is running ({names}). Close it or press End Task before applying settings.";
            TuningNoticeText.Foreground = GetBrush("Danger");
            TuningApplyButton.IsEnabled = false;
        }
        else
        {
            TuningNoticeText.Text = "GameLoop must be closed before applying settings.";
            TuningNoticeText.Foreground = GetBrush("Warning");
            TuningApplyButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Page-local loading paint: the accent bar plus a status hint. Apply is
    /// re-enabled after the load settles, per the detected emulator state.
    /// </summary>
    private void OnLoadingChanged(bool loading)
    {
        TuningLoadingBar.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        if (!loading) return;

        TuningApplyButton.IsEnabled = false;
    }

    /// <summary>Page-local enablement while Apply or End Task runs.</summary>
    private void OnBusyVisualChanged(bool busy)
    {
        TuningApplyButton.IsEnabled = !busy;
        TuningEndTaskButton.IsEnabled = !busy;
    }

    /// <summary>The page's own status cell. The shell forwards the same message to the window bar.</summary>
    private void OnStatusChanged(string message, bool isError)
    {
        TuningStatusText.Text = message;
        TuningStatusText.Foreground = GetBrush(isError ? "Danger" : "TextSecondary");
    }

    private void UpdateLabels()
    {
        // ValueChanged fires during InitializeComponent (XAML-assigned Value)
        // while later-declared labels are still null. Bail out until the tree
        // is complete.
        if (TuningCpuSlider is null || TuningCpuText is null || TuningMemorySlider is null || TuningMemoryText is null)
            return;
        TuningCpuText.Text = TuningViewModel.FormatCpuLabel((int)TuningCpuSlider.Value);
        TuningMemoryText.Text = TuningViewModel.FormatMemoryLabel((int)TuningMemorySlider.Value);
    }

    private void TuningCpuSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateLabels();

    private void TuningMemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateLabels();

    private async void TuningEndTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        await _viewModel.EndTaskAsync();
        await _viewModel.RefreshAsync();
    }

    private async void TuningApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        await _viewModel.ApplyAsync(BuildSelection());
        await _viewModel.RefreshAsync();
    }

    /// <summary>
    /// Assembles the apply payload from the selected controls. An
    /// unrecognized DPI falls back to the catalog default, exactly as the
    /// pre-extraction handler did.
    /// </summary>
    private EmulatorTuningSelection BuildSelection() => new(
        CpuCores: (int)TuningCpuSlider.Value,
        MemoryMb: (int)TuningMemorySlider.Value,
        Dpi: TuningDpiComboBox.SelectedItem is int dpi ? dpi : EmulatorTuningCatalog.DefaultDpi,
        RenderCacheEnabled: TuningRenderCacheCheck.IsChecked == true,
        GlobalCacheEnabled: TuningGlobalCacheCheck.IsChecked == true,
        DiscreteGpuEnabled: TuningDiscreteGpuCheck.IsChecked == true,
        RenderOptimizeEnabled: TuningRenderOptimizeCheck.IsChecked == true,
        VSyncEnabled: TuningVSyncCheck.IsChecked == true,
        AdbEnabled: TuningAdbCheck.IsChecked == true,
        AntiAliasingEnabled: TuningAntiAliasingCheck.IsChecked == true);

    private Brush GetBrush(string key) => ResourceBrushLookup.Get(this, key);
}
