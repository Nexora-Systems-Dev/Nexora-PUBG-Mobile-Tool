using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Nexora.Configuration;
using Nexora.Features.GameLoop;
using Nexora.Features.Layout;
using Nexora.Features.Performance;
using Nexora.Features.SystemTools;
using Nexora.Features.SystemTools.Network;
using Nexora.Services;
using Nexora.Services.Performance;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Nexora.UI.Behaviors;
using Nexora.UI.Helpers;
using Nexora.UI.Layout;
using Nexora.UI.Presentation;

namespace Nexora;

public partial class MainWindow : Window
{
    private readonly IGameLoopConnection _connection;
    private readonly IGraphicsProfileStore _graphics;
    private readonly IAdbClient _adb;
    private readonly ITempCleanupService _tempCleanup;
    private readonly INetworkToolsService _networkTools;
    private readonly IGameLoopProcessService _processService;
    private readonly IShortcutService _shortcuts;
    private readonly IIpadLayoutService _ipadLayout;
    private readonly IGameLoopPerformanceEngine _performanceEngine;
    private readonly IEmulatorSettingsService _tuning;
    private readonly IUpdateService _updates;
    private readonly GameLoopOptions _gameLoopOptions;

    private CancellationTokenSource? _connectionCancellation;
    private CancellationTokenSource? _toolCancellation;
    private IDisposable? _chromeHook;
    private bool _suppressSelection;
    private bool _isBusy;

    public MainWindow()
        : this(null, null, null, null, null, null, null, null, null, null, null, null)
    {
    }

    public MainWindow(
        IGameLoopConnection? connection = null,
        IGraphicsProfileStore? graphics = null,
        ITempCleanupService? tempCleanup = null,
        INetworkToolsService? networkTools = null,
        IGameLoopProcessService? processService = null,
        IShortcutService? shortcuts = null,
        IIpadLayoutService? ipadLayout = null,
        IGameLoopPerformanceEngine? performanceEngine = null,
        IUpdateService? updates = null,
        GameLoopOptions? gameLoopOptions = null,
        IAdbClient? adb = null,
        IEmulatorSettingsService? tuning = null)
    {
        InitializeComponent();
        FreezeSharedResources();
        _gameLoopOptions = gameLoopOptions ?? new GameLoopOptions();
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        // Single device identity: use the injected client when DI provides one
        // (the same singleton instance GameLoopService holds), and only fall
        // back to a locally constructed client for the designer path — which
        // is then shared with the fallback GameLoopService below.
        var pathResolver = new GameLoopPathResolver(registry);
        var adbClient = adb ?? new AdbClient(runner, pathResolver);
        // Single process/temp identity in the designer path: one process
        // service and one temp cleanup shared by every fallback below,
        // mirroring the DI singletons used when the container provides them.
        var processSvc = processService ?? new GameLoopProcessService(runner, pathResolver);
        var tempSvc = tempCleanup ?? new TempCleanupService(registry);
        // Same shared-store composition the DI container builds for the trio.
        var priorityStore = new ProcessPrioritySnapshotStore();
        var priorityApplier = new ProcessPriorityApplier(priorityStore, processSvc);
        var processPriority = new ProcessPriorityService(priorityStore, priorityApplier, new ProcessPriorityMonitor(priorityStore, priorityApplier));

        // Single GameLoop identity in the designer path: both facets share
        // one service, mirroring the DI factory-forwards used when the
        // container provides them.
        GameLoopService? loopFallback = null;
        GameLoopService LoopFallback() => loopFallback ??= new GameLoopService(registry, adbClient, new GameLoopWorkingStorage(new PhysicalFileSystem(), new GameLoopWorkRootProvider()), new PhysicalFileSystem(), processSvc);
        _connection = connection ?? LoopFallback();
        _graphics = graphics ?? LoopFallback();
        _adb = adbClient;
        _tempCleanup = tempSvc;
        _networkTools = networkTools ?? new NetworkToolsService(runner);
        _processService = processSvc;
        _shortcuts = shortcuts ?? new ShortcutService(runner, pathResolver, Path.Combine(AppContext.BaseDirectory, new EmulatorOptions().Assets.DirectoryName));
        _ipadLayout = ipadLayout ?? new IpadLayoutService(registry, new PhysicalFileSystem(), processService: processSvc);
        _performanceEngine = performanceEngine ?? new PerformanceEngineFacade(runner, registry, registry, processSvc, tempSvc, processPriority);
        _tuning = tuning ?? new EmulatorSettingsService(registry, processSvc);
        _updates = updates ?? new UpdateService(runner);

        PubgVersionComboBox.DisplayMemberPath = nameof(PubgVersion.DisplayName);
        ShortcutComboBox.DisplayMemberPath = nameof(PubgVersion.DisplayName);

        IpadComboBox.ItemsSource = IpadPresetCatalog.Presets.Select(preset => preset.DisplayName).ToList();
        IpadComboBox.SelectedIndex = -1;
        UpdateIpadPresetDetails();

        DnsComboBox.ItemsSource = DnsCatalog.Labels;
        DnsComboBox.SelectedIndex = 0;

        TuningDpiComboBox.ItemsSource = EmulatorTuningCatalog.DpiOptions;
        TuningDpiComboBox.SelectedItem = EmulatorTuningCatalog.DefaultDpi;

        ShortcutComboBox.ItemsSource = PubgVersionCatalog.PubgVersions
            .Select(pair => new PubgVersion(pair.Key, pair.Value))
            .ToList();
        ShortcutComboBox.SelectedIndex = 0;
        UpdateShortcutPreview();

        ClassicButton.IsChecked = true;
        SmoothButton.IsChecked = true;
        LowButton.IsChecked = true;
        VersionText.Text = "VERSION " + AppConstants.CurrentVersion.TrimStart('v');
        SetStatus("Ready to connect to GameLoop.");
        UpdateSummary();
    }

    /// <summary>
    /// Freezes the shared brushes and drop-shadow effects each view declares in
    /// its resource dictionary. A frozen freezable is immutable, so WPF can skip
    /// change tracking and the extra software-rasterization passes the per-page
    /// accent glows and glows' effects would otherwise force on resize/scroll
    /// (QA §3.3). Only resource-dictionary entries are touched — inline,
    /// data-bound values stay live because <see cref="Freezable.CanFreeze"/> is
    /// checked first, and nothing in code-behind mutates these resources.
    /// </summary>
    private void FreezeSharedResources()
    {
        foreach (var view in new[] { GraphicsView, OptimizerView, TuningView, NetworkView, ShortcutsView, AboutView })
        {
            foreach (var value in view.Resources.Values.OfType<Freezable>())
            {
                if (value.CanFreeze) value.Freeze();
            }
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        if (_connection.IsAdbConnected)
        {
            DisconnectFromGameLoop();
            return;
        }

        await ConnectToGameLoopAsync();
    }

    private void DisconnectFromGameLoop()
    {
        CancelAndDisposeConnection();
        _connection.Disconnect();
        _adb.StopAdb();
        ShowConnectionVisual(ConnectionState.Disconnected, "Disconnected from GameLoop.");
    }

    private async Task ConnectToGameLoopAsync()
    {
        _isBusy = true;
        CancelAndDisposeConnection();
        _connectionCancellation = new CancellationTokenSource();
        SetBusyState(true);
        SetStatus("Connecting to GameLoop...");

        ConnectionResult result;
        try
        {
            result = await _connection.ConnectAsync(_connectionCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            result = new ConnectionResult(false, "Connection canceled.", Array.Empty<PubgVersion>());
        }
        catch (Exception ex)
        {
            result = new ConnectionResult(false, ex.Message, Array.Empty<PubgVersion>());
        }
        finally
        {
            _isBusy = false;
            SetBusyState(false);
        }

        _suppressSelection = true;
        try
        {
            PubgVersionComboBox.ItemsSource = result.InstalledVersions;
            ShortcutComboBox.ItemsSource = result.InstalledVersions.Count > 0
                ? result.InstalledVersions
                : PubgVersionCatalog.PubgVersions.Select(pair => new PubgVersion(pair.Key, pair.Value)).ToList();
            ShortcutComboBox.SelectedIndex = result.InstalledVersions.Count == 1 ? 0 : -1;
            if (result.InstalledVersions.Count == 1)
            {
                PubgVersionComboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            _suppressSelection = false;
        }

        if (!result.Success)
        {
            ShowConnectionVisual(ConnectionState.Failed, result.Message);
            return;
        }

        if (_connection.IsConnected)
        {
            await ApplyLoadedSettingsAsync(_connectionCancellation?.Token ?? CancellationToken.None);
            ShowConnectionVisual(ConnectionState.FullyConnected, result.Message);
        }
        else if (_connection.IsAdbConnected)
        {
            ShowConnectionVisual(ConnectionState.TransportConnected, result.Message);
        }
        else
        {
            ShowConnectionVisual(ConnectionState.AwaitingVersion, result.Message);
        }
    }

    private async void PubgVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || _isBusy || PubgVersionComboBox.SelectedItem is not PubgVersion version || _connection.IsConnected) return;
        _isBusy = true;
        SetStatus($"Loading {version.DisplayName}...");
        var cancellationToken = _connectionCancellation?.Token ?? CancellationToken.None;
        try
        {
            var result = await _connection.LoadVersionAsync(version.PackageName, cancellationToken);
            if (result.Success)
            {
                await ApplyLoadedSettingsAsync(cancellationToken);
                ShowConnectionVisual(ConnectionState.FullyConnected, result.Message);
            }
            else
            {
                SetStatus(result.Message, isError: true);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Loading version settings was canceled.");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load {version.DisplayName}: {ex.Message}", isError: true);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        var selection = new GraphicsSelection(
            SelectedContent(SmoothButton, BalancedButton, HdButton, HdrButton, UltraHdButton, UhdButton) ?? GraphicsSelection.Defaults.Quality,
            SelectedContent(LowButton, MediumButton, HighButton, UltraButton, ExtremeButton, Fps90Button, Fps120Button) ?? GraphicsSelection.Defaults.FrameRate,
            SelectedStyle(),
            ShadowEnableButton.IsChecked == true,
            KoreanFullHdButton.IsChecked == true &&
            _connection.CurrentPackage?.Equals(PubgVersionCatalog.KoreanPackage, StringComparison.OrdinalIgnoreCase) == true);

        _isBusy = true;
        SetBusyState(true);
        SetStatus("Applying graphics settings...");
        OperationResult result;
        var cancellationToken = _connectionCancellation?.Token ?? CancellationToken.None;
        try
        {
            result = await _graphics.ApplyGraphicsAsync(selection, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result = OperationResult.Fail("Graphics application was canceled.");
        }
        catch (Exception ex)
        {
            result = OperationResult.Fail(ex.Message);
        }
        finally
        {
            _isBusy = false;
            SetBusyState(false);
        }

        SetStatus(result.Message, !result.Success);
    }

    private void SettingRadioButton_Checked(object sender, RoutedEventArgs e) => UpdateSummary();

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // The GitHub update check (up to the 12 s HttpTimeout) and the Optimizer
        // profile refresh are independent, so they run concurrently: an offline
        // or slow network can no longer leave the panel empty for the whole
        // timeout window. Each task owns its own error handling and
        // shutdown-guarded UI writes, so a failure in one never blocks or
        // suppresses the other (QA F-006).
        await Task.WhenAll(CheckForUpdatesAsync(), RefreshOptimizerProfileAsync());
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await _updates.CheckAsync();
            // The check can now outlive the window (a slow network plus a user
            // close, or the refresh settling first); never show UI on a
            // dispatcher that is already torn down.
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            if (update.Available)
            {
                var message = $"Nexora update {update.LatestVersion} is available.\n\n{update.ChangeLog}";
                if (MessageBox.Show(message, "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    SetStatus("Downloading update...");
                    var result = await _updates.DownloadAndLaunchAsync(update);
                    SetStatus(result.Message, !result.Success);
                    if (result.Success)
                    {
                        // UpdateService has already verified that the new
                        // elevated process started successfully. Closing this
                        // instance lets the new version take over cleanly.
                        Close();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Update check failed: {ex.Message}", isError: true);
        }
    }

    // Built once: the five style cards never change identity, so caching the
    // array avoids allocating it on every access (QA §3.5).
    private ToggleButton[]? _styleButtons;
    private ToggleButton[] StyleButtons => _styleButtons ??= new[] { ClassicButton, ColorfulButton, RealisticButton, SoftButton, MovieButton };

    private void StyleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton selected) return;
        foreach (var button in StyleButtons)
        {
            if (!ReferenceEquals(button, selected)) button.IsChecked = false;
        }
        UpdateSummary();
    }

    private async void TempCleanerButton_Click(object sender, RoutedEventArgs e) =>
        await RunToolAsync(TempCleanerButton, ct => _tempCleanup.CleanTempAsync(ct), OptimizerStatusText);

    private async void SmartSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunToolAsync(SmartSettingsButton, ct => Task.Run(() => _performanceEngine.ApplySmartSettings(), ct), OptimizerStatusText);
        await RefreshOptimizerProfileAsync();
    }

    private async void GameLoopOptimizerButton_Click(object sender, RoutedEventArgs e) =>
        await RunToolAsync(GameLoopOptimizerButton, ct => Task.Run(() => _performanceEngine.OptimizeGameLoop(), ct), OptimizerStatusText);

    private async void AllRecommendedButton_Click(object sender, RoutedEventArgs e)
    {
        await RunToolAsync(AllRecommendedButton, ct => _performanceEngine.OptimizeAllAsync(ct), OptimizerStatusText);
        await RefreshOptimizerProfileAsync();
    }

    private async void ForceCloseButton_Click(object sender, RoutedEventArgs e) =>
        await RunToolAsync(ForceCloseButton, ct => Task.Run(() => _processService.KillGameLoopProcesses(), ct), OptimizerStatusText);

    private async void PerformanceSessionButton_Click(object sender, RoutedEventArgs e) =>
        await RunToolAsync(PerformanceSessionButton, ct => Task.Run(() => _performanceEngine.ApplyPerformanceSession(), ct), OptimizerStatusText);

    private async void RestoreSessionButton_Click(object sender, RoutedEventArgs e) =>
        await RunToolAsync(RestoreSessionButton, ct => _performanceEngine.RestorePerformanceSessionAsync(ct), OptimizerStatusText);

    private async void RefreshOptimizerButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshOptimizerProfileAsync();

    private async Task RefreshOptimizerProfileAsync()
    {
        if (_isBusy || RefreshOptimizerButton is null) return;
        RefreshOptimizerButton.IsEnabled = false;
        try
        {
            var hardware = await _performanceEngine.GetHardwareSnapshotAsync();
            var plan = _performanceEngine.GetRecommendedPlan(hardware);
            // Running beside the update check means the refresh can settle after
            // the update handoff has closed the window; a dead dispatcher must
            // never touch the visual tree (QA F-006).
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            UpdateOptimizerPanel(hardware, plan);
        }
        catch (Exception ex)
        {
            OptimizerStatusText.Text = $"Hardware detection failed: {ex.Message}";
            OptimizerStatusText.Foreground = GetBrush("Danger");
        }
        finally
        {
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                RefreshOptimizerButton.IsEnabled = true;
        }
    }

    private void UpdateOptimizerPanel(HardwareSnapshot hardware, OptimizerPlan plan)
    {
        var display = OptimizerDisplayFormatter.Format(hardware, plan);

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

    private bool _closeRequested;

    private bool _closeCompleted;

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // The only close ever allowed straight through is our own deferred
        // Close() from the finally block below, which identifies itself with
        // _closeCompleted (QA F-001). Every other request — the X button,
        // Alt+F4, the update handoff's Close(), or an external shutdown — is
        // held here until the async restore finishes, so a second trigger can
        // never close the window out from under the running continuation.
        if (_closeCompleted) return;
        e.Cancel = true;
        // A restore is already in flight from an earlier request, so this is a
        // duplicate (double-X). It must not start a second teardown or release
        // the window: the first pass's finally still owns the final Close().
        if (_closeRequested) return;
        _closeRequested = true;

        CancelAndDisposeConnection();
        CancelAndDisposeTool();
        _chromeHook?.Dispose();
        _chromeHook = null;

        // Restore performance session before shutdown so the system's power
        // plan and process priorities are not left modified. The bounded
        // timeout guarantees shutdown can never hang indefinitely.
        try
        {
            using var shutdownCts = new CancellationTokenSource(_gameLoopOptions.Timeouts.ShutdownRestoreTimeout);
            await _performanceEngine.RestorePerformanceSessionAsync(shutdownCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Bounded timeout elapsed; restoration is best-effort.
            // Logging infrastructure is unavailable at shutdown.
        }
        catch (Exception)
        {
            // Best-effort teardown during window closure.
        }
        finally
        {
            // Close exactly once, and only while the dispatcher is still alive.
            // The e.Cancel guard above normally holds the window open for the
            // whole restore, but a close request can still land in the narrow
            // window between setting _closeCompleted and calling Close(); on an
            // already-closing window that throws InvalidOperationException, which
            // in an async void method would terminate the process (QA F-001).
            if (!_closeCompleted)
            {
                _closeCompleted = true;
                try
                {
                    if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                        Close();
                }
                catch (InvalidOperationException)
                {
                    // Window already closed via the racing close request; teardown done.
                }
            }
        }
    }

    private void CancelAndDisposeConnection()
    {
        var cancellation = _connectionCancellation;
        _connectionCancellation = null;
        if (cancellation is null) return;
        try
        {
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void CancelAndDisposeTool()
    {
        var cancellation = _toolCancellation;
        _toolCancellation = null;
        if (cancellation is null) return;
        try
        {
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async void DnsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DnsComboBox.SelectedItem is not string label || !DnsCatalog.TryGet(label, out var entry) || entry is null) return;
        var selectedLabel = label;
        DnsStatusText.Text = $"{entry.ShortName} • Testing response...";
        var ping = await _networkTools.PingDnsAsync(entry.Primary);

        // The continuation resumes on the UI thread: the event handler captured
        // the UI SynchronizationContext at the await, so the result can be
        // applied directly instead of bouncing through a synchronous
        // Dispatcher.Invoke (QA F-005). The shutdown guard is still required,
        // because this continuation can outlive a closed window.
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

        if (DnsComboBox.SelectedItem is not string currentLabel ||
            !string.Equals(currentLabel, selectedLabel, StringComparison.OrdinalIgnoreCase)) return;

        DnsStatusText.Text = ping is null
            ? $"{entry.ShortName} • No response from DNS server"
            : $"{entry.ShortName} • Ping: {ping}ms • Ready to apply";
    }

    private async void ChangeDnsButton_Click(object sender, RoutedEventArgs e)
    {
        if (DnsComboBox.SelectedItem is not string label || !DnsCatalog.TryGet(label, out var entry) || entry is null) return;
        var result = await RunToolAsync(ChangeDnsButton, ct => Task.Run(() => _networkTools.ChangeDns(entry.Primary, entry.Secondary), ct), null);
        DnsStatusText.Text = result.Success
            ? $"{entry.ShortName} • Applied: {entry.Primary} / {entry.Secondary}"
            : result.Message;
    }

    private async void ChangeIpadButton_Click(object sender, RoutedEventArgs e)
    {
        var preset = GetSelectedIpadPreset();
        if (preset is null) return;
        var result = await RunToolAsync(ChangeIpadButton, ct => Task.Run(() => _ipadLayout.SetIpadResolution(preset.Width, preset.Height), ct), null);
        IpadApplyStatusText.Text = result.Success
            ? $"Applied: {preset.Label} • {preset.Width} × {preset.Height}. Restart GameLoop to load it."
            : result.Message;
        IpadApplyStatusText.Foreground = GetBrush(result.Success ? "Accent" : "Danger");
    }

    private void IpadComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateIpadPresetDetails();

    private void UpdateIpadPresetDetails()
    {
        var preset = GetSelectedIpadPreset();
        if (preset is not null)
        {
            IpadPresetDetailsText.Text = preset.Details;
            IpadApplyStatusText.Text = "Ready to apply this profile.";
            IpadApplyStatusText.Foreground = GetBrush("TextMuted");
            ChangeIpadButton.IsEnabled = true;
        }
        else
        {
            IpadPresetDetailsText.Text = "Choose a tested iPad display profile.";
            IpadApplyStatusText.Text = "No profile selected.";
            IpadApplyStatusText.Foreground = GetBrush("TextMuted");
            ChangeIpadButton.IsEnabled = false;
        }
    }

    private IpadResolutionPreset? GetSelectedIpadPreset() =>
        IpadPresetCatalog.FindByDisplayName(IpadComboBox.SelectedItem as string);

    private async void ResetIpadButton_Click(object sender, RoutedEventArgs e) =>
        await RunToolAsync(ResetIpadButton, ct => Task.Run(() => _ipadLayout.ResetIpadResolution(), ct), null);

    private async Task RefreshTuningAsync()
    {
        // Cooperative guard: the refresh owns _isBusy for its duration, so a second
        // Tuning click cannot stack another hardware scan on the first (the missed
        // clicks of QA F-008). APPLY/END TASK are blocked by the same flag inside
        // RunToolAsync, so only the status text + accent bar show the busy state.
        if (_isBusy) return;
        _isBusy = true;
        SetTuningLoading(true);

        EmulatorTuningState state;
        try
        {
            // The hardware scan now runs off-thread, so this token genuinely
            // pre-empts a stuck scan and hands control back to the user at 15 s
            // worst case, instead of freezing the UI for the full CIM timeout.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            state = await _tuning.LoadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            TuningStatusText.Text = "Loading emulator settings timed out.";
            TuningStatusText.Foreground = GetBrush("Danger");
            return;
        }
        catch (Exception ex)
        {
            TuningStatusText.Text = $"Could not load emulator settings: {ex.Message}";
            TuningStatusText.Foreground = GetBrush("Danger");
            return;
        }
        finally
        {
            SetTuningLoading(false);
            _isBusy = false;
        }

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
        UpdateTuningLabels();

        if (state.IsGameLoopRunning)
        {
            var names = string.Join(", ", state.RunningProcessNames);
            TuningNoticeText.Text = $"GameLoop is running ({names}). Close it or press End Task before applying settings.";
            TuningNoticeText.Foreground = GetBrush("Danger");
            TuningStatusText.Text = "GameLoop is running. End its tasks, then apply.";
            TuningStatusText.Foreground = GetBrush("Danger");
            TuningApplyButton.IsEnabled = false;
        }
        else
        {
            TuningNoticeText.Text = "GameLoop must be closed before applying settings.";
            TuningNoticeText.Foreground = GetBrush("Warning");
            TuningStatusText.Text = "Current settings loaded. Adjust, then apply.";
            TuningStatusText.Foreground = GetBrush("TextSecondary");
            TuningApplyButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Lightweight loading state for the Tuning page: an accent bar under the fixed
    /// header plus a status hint. The page stays interactive while the hardware scan
    /// runs off-thread (QA F-002 / F-008); the bar collapses again as soon as the
    /// refresh settles, so no layout is reflowed.
    /// </summary>
    private void SetTuningLoading(bool loading)
    {
        TuningLoadingBar.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        if (!loading) return;

        TuningStatusText.Text = "Loading emulator settings...";
        TuningStatusText.Foreground = GetBrush("TextSecondary");
        // Re-enabled after the load settles, per the detected emulator state.
        TuningApplyButton.IsEnabled = false;
    }

    private void UpdateTuningLabels()
    {
        // ValueChanged fires during InitializeComponent (XAML-assigned Value) while
        // later-declared labels are still null. Bail out until the tree is complete.
        if (TuningCpuSlider is null || TuningCpuText is null || TuningMemorySlider is null || TuningMemoryText is null)
            return;
        var cores = (int)TuningCpuSlider.Value;
        TuningCpuText.Text = FormattableString.Invariant($"{cores} core{(cores == 1 ? string.Empty : "s")}");
        var megabytes = (int)TuningMemorySlider.Value;
        TuningMemoryText.Text = FormattableString.Invariant($"{megabytes} MB");
    }

    private void TuningCpuSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateTuningLabels();

    private void TuningMemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateTuningLabels();

    private async void TuningEndTaskButton_Click(object sender, RoutedEventArgs e)
    {
        await RunToolAsync(TuningEndTaskButton, ct => Task.Run(() => _processService.KillGameLoopProcesses(ct), ct), TuningStatusText);
        await RefreshTuningAsync();
    }

    private async void TuningApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var selection = new EmulatorTuningSelection(
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
        await RunToolAsync(TuningApplyButton, ct => _tuning.ApplyAsync(selection, ct), TuningStatusText);
        await RefreshTuningAsync();
    }

    private async void CreateShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (ShortcutComboBox.SelectedItem is not PubgVersion version) return;
        await RunToolAsync(CreateShortcutButton, ct => Task.Run(() => _shortcuts.CreateShortcut(version.DisplayName, version.PackageName), ct), null);
    }

    private void ShortcutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateShortcutPreview();

    private void UpdateShortcutPreview()
    {
        if (ShortcutComboBox.SelectedItem is not PubgVersion version)
        {
            ShortcutDisplayNameText.Text = "Choose a PUBG Mobile version";
            ShortcutPackageText.Text = "No package selected";
            ShortcutDestinationText.Text = "The shortcut will be placed on your desktop.";
            ShortcutIcon.Source = null;
            CreateShortcutButton.IsEnabled = false;
            return;
        }

        ShortcutDisplayNameText.Text = version.DisplayName;
        ShortcutPackageText.Text = version.PackageName;
        ShortcutDestinationText.Text = $"Desktop shortcut: {version.DisplayName}.lnk";
        CreateShortcutButton.IsEnabled = true;

        ShortcutIcon.Source = _shortcuts.GetIcon(version.PackageName);
    }

    private async void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton button || button.Tag is not string page) return;
        GraphicsView.Visibility = page == "Graphics" ? Visibility.Visible : Visibility.Collapsed;
        OptimizerView.Visibility = page == "Optimizer" ? Visibility.Visible : Visibility.Collapsed;
        TuningView.Visibility = page == "Tuning" ? Visibility.Visible : Visibility.Collapsed;
        NetworkView.Visibility = page == "Network" ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsView.Visibility = page == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        AboutView.Visibility = page == "About" ? Visibility.Visible : Visibility.Collapsed;

        if (page == "Tuning") await RefreshTuningAsync();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        if (e.ClickCount == 2) { MaximizeButton_Click(sender, e); return; }
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _chromeHook?.Dispose();
        _chromeHook = WindowChromeBehavior.Attach(this);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var sidebarWidth = ResponsiveLayoutManager.GetSidebarWidth(e.NewSize.Width);
        SidebarColumn.Width = new GridLength(sidebarWidth);
        TitleBrandColumn.Width = new GridLength(sidebarWidth);

        var pageMargin = ResponsiveLayoutManager.GetPageMargin(e.NewSize.Width);
        GraphicsView.Margin = pageMargin;
        OptimizerView.Margin = pageMargin;
        NetworkView.Margin = pageMargin;
        ShortcutsView.Margin = pageMargin;
        TuningView.Margin = pageMargin;
        AboutView.Margin = pageMargin;
    }

    private async Task<OperationResult> RunToolAsync(Button button, Func<CancellationToken, Task<OperationResult>> action, TextBlock? resultLabel)
    {
        if (_isBusy) return OperationResult.Fail("Another operation is already running.");
        _isBusy = true;
        button.IsEnabled = false;
        SetStatus("Working...");
        if (resultLabel is not null)
        {
            resultLabel.Text = "Working...";
            resultLabel.Foreground = GetBrush("TextSecondary");
            if (ReferenceEquals(resultLabel, OptimizerStatusText) && OptimizerActivityText is not null)
            {
                OptimizerActivityText.Text = "Running each boost step...";
                OptimizerActivityText.Foreground = GetBrush("TextSecondary");
            }
        }

        OperationResult result;
        _toolCancellation = new CancellationTokenSource();
        try { result = await action(_toolCancellation.Token); }
        catch (OperationCanceledException) { result = OperationResult.Fail("Operation canceled."); }
        catch (Exception ex) { result = OperationResult.Fail(ex.Message); }
        finally
        {
            button.IsEnabled = true;
            _isBusy = false;
            CancelAndDisposeTool();
        }

        if (resultLabel is not null)
        {
            if (ReferenceEquals(resultLabel, OptimizerStatusText))
            {
                RenderOptimizerReport(result);
            }
            else
            {
                resultLabel.Text = result.Message;
                resultLabel.Foreground = GetBrush(result.Success ? "Success" : "Danger");
            }
        }

        var statusLine = ReferenceEquals(resultLabel, OptimizerStatusText)
            ? ActivityReportFormatter.Format(result).StatusLine
            : result.Message;
        SetStatus(statusLine, !result.Success);
        return result;
    }

    private void RenderOptimizerReport(OperationResult result)
    {
        var display = ActivityReportFormatter.Format(result);
        OptimizerStatusText.Text = display.StatusLine;
        if (OptimizerActivityText is not null)
        {
            OptimizerActivityText.Text = string.IsNullOrWhiteSpace(display.Details)
                ? "No step details."
                : display.Details;
            OptimizerActivityText.Foreground = GetBrush(display.IsError ? "Danger" : "TextSecondary");
        }

        var statusBrush = result.Success
            ? result.IsSkipped ? "Accent" : "Success"
            : "Danger";
        OptimizerStatusText.Foreground = GetBrush(statusBrush);
    }

    private async Task ApplyLoadedSettingsAsync(CancellationToken cancellationToken)
    {
        _suppressSelection = true;
        try
        {
            SelectContent(_graphics.GetGraphicsQuality(), new[] { SmoothButton, BalancedButton, HdButton, HdrButton, UltraHdButton, UhdButton });
            SelectContent(_graphics.GetFrameRate(), new[] { LowButton, MediumButton, HighButton, UltraButton, ExtremeButton, Fps90Button, Fps120Button });
            var style = _graphics.GetGraphicsStyle();
            foreach (var button in StyleButtons)
            {
                button.IsChecked = style is not null && string.Equals(button.Tag?.ToString(), style, StringComparison.OrdinalIgnoreCase);
            }

            var shadow = await _graphics.GetShadowAsync(cancellationToken);
            ShadowDisableButton.IsChecked = string.Equals(shadow, "Disable", StringComparison.OrdinalIgnoreCase);
            ShadowEnableButton.IsChecked = string.Equals(shadow, "Enable", StringComparison.OrdinalIgnoreCase);
            ShadowDisableButton.IsEnabled = true;
            ShadowEnableButton.IsEnabled = true;

            var isKoreanVersion = string.Equals(_connection.CurrentPackage, PubgVersionCatalog.KoreanPackage, StringComparison.OrdinalIgnoreCase);
            KoreanResolutionPanel.Visibility = isKoreanVersion ? Visibility.Visible : Visibility.Collapsed;
            KoreanFullHdButton.IsEnabled = isKoreanVersion;
            KoreanFullHdButton.IsChecked = isKoreanVersion;
        }
        finally
        {
            _suppressSelection = false;
        }

        UpdateSummary();
    }

    private Brush? GetBrush(string key) => FindResource(key) as Brush;

    private static DropShadowEffect CreateSuccessGlow() =>
        new() { Color = Color.FromRgb(0x10, 0xB9, 0x81), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 };

    private enum ConnectionState
    {
        Disconnected,
        Failed,
        AwaitingVersion,
        TransportConnected,
        FullyConnected,
    }

    private void ShowConnectionVisual(ConnectionState state, string message)
    {
        switch (state)
        {
            case ConnectionState.Failed:
                ShowFailureConnectionVisual();
                SetStatus(message, isError: true);
                break;
            case ConnectionState.AwaitingVersion:
                // Defensive branch: reachable only if a connect reports success without
                // any transport state. It intentionally leaves ConnectionDot untouched,
                // matching the pre-merge behavior — do not fold it into the helper.
                var awaitingSuccess = GetBrush("Success") ?? Brushes.LimeGreen;
                var awaitingGlow = CreateSuccessGlow();
                TopConnectionDot.Fill = awaitingSuccess;
                TopConnectionDot.Effect = awaitingGlow;
                TopConnectionPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x10, 0xB9, 0x81));
                TopConnectionText.Text = "GameLoop connected";
                SidebarConnectionDot.Fill = awaitingSuccess;
                SidebarConnectionDot.Effect = awaitingGlow;
                SidebarConnectionText.Text = "CONNECTED";
                SidebarAdbText.Text = "ADB: Connected";
                SummaryAdb.Text = "Connected";
                ConnectionDetail.Text = "Select the PUBG Mobile version to load its settings.";
                SetStatus(message);
                break;
            case ConnectionState.TransportConnected:
            case ConnectionState.FullyConnected:
                ShowConnectedConnectionVisual(state, message);
                break;
            default:
                ShowDisconnectedConnectionVisual();
                ConnectionDetail.Text = "Connect to GameLoop to load current settings.";
                SetStatus(message);
                UpdateSummary();
                break;
        }
    }

    private void ShowConnectedConnectionVisual(ConnectionState state, string message)
    {
        ShowSuccessConnectionVisual();
        TopConnectionText.Text = "Connected to GameLoop";
        ConnectButton.Content = "DISCONNECT";
        // Only a fully loaded version enables Apply; transport-only also
        // keeps the shadow toggles off until the profile has been read.
        ApplyButton.IsEnabled = state == ConnectionState.FullyConnected;
        if (state == ConnectionState.TransportConnected)
        {
            ShadowDisableButton.IsEnabled = false;
            ShadowEnableButton.IsEnabled = false;
        }

        ConnectionDetail.Text = message;
        SetStatus(message);
        UpdateSummary();
    }

    private void ShowSuccessConnectionVisual()
    {
        var success = GetBrush("Success") ?? Brushes.LimeGreen;
        var emeraldGlow = CreateSuccessGlow();
        ConnectionDot.Fill = success;
        ConnectionDot.Effect = emeraldGlow;
        TopConnectionDot.Fill = success;
        TopConnectionDot.Effect = emeraldGlow;
        TopConnectionPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x10, 0xB9, 0x81));
        SidebarConnectionDot.Fill = success;
        SidebarConnectionDot.Effect = emeraldGlow;
        SidebarConnectionText.Text = "CONNECTED";
        SidebarAdbText.Text = "ADB: Connected";
        SummaryAdb.Text = "Connected";
    }

    private void ShowFailureConnectionVisual()
    {
        var danger = GetBrush("Danger") ?? Brushes.Crimson;
        var dangerGlow = new DropShadowEffect { Color = Color.FromRgb(0xEF, 0x44, 0x44), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.85 };
        TopConnectionDot.Fill = danger;
        TopConnectionDot.Effect = dangerGlow;
        TopConnectionPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xEF, 0x44, 0x44));
        SidebarConnectionDot.Fill = danger;
        SidebarConnectionDot.Effect = dangerGlow;
        TopConnectionText.Text = "Connection failed";
        SidebarConnectionText.Text = "FAILED";
    }

    private void ShowDisconnectedConnectionVisual()
    {
        var muted = GetBrush("TextMuted") ?? Brushes.Gray;
        ConnectionDot.Fill = muted;
        ConnectionDot.Effect = null;
        TopConnectionDot.Fill = muted;
        TopConnectionDot.Effect = null;
        TopConnectionText.Text = "Not connected";
        TopConnectionPill.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x32, 0x44));
        SidebarConnectionDot.Fill = muted;
        SidebarConnectionDot.Effect = null;
        SidebarConnectionText.Text = "NOT CONNECTED";
        SidebarAdbText.Text = "ADB: Offline";
        SummaryAdb.Text = "Offline";
        ConnectButton.Content = "CONNECT";
        ApplyButton.IsEnabled = false;
        ShadowDisableButton.IsEnabled = false;
        ShadowEnableButton.IsEnabled = false;
        ShadowDisableButton.IsChecked = false;
        ShadowEnableButton.IsChecked = false;
        KoreanResolutionPanel.Visibility = Visibility.Collapsed;
        KoreanFullHdButton.IsEnabled = false;
        KoreanFullHdButton.IsChecked = false;
    }

    private void SetBusyState(bool busy)
    {
        ConnectButton.IsEnabled = !busy;
        RefreshConnectionButton.IsEnabled = !busy;
        ApplyButton.IsEnabled = !busy && _connection.IsConnected;
        ShadowDisableButton.IsEnabled = !busy && _connection.IsConnected;
        ShadowEnableButton.IsEnabled = !busy && _connection.IsConnected;
        KoreanFullHdButton.IsEnabled = !busy &&
            string.Equals(_connection.CurrentPackage, PubgVersionCatalog.KoreanPackage, StringComparison.OrdinalIgnoreCase);
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = GetBrush(isError ? "Danger" : "TextSecondary");
    }

    private void UpdateSummary()
    {
        SummaryVersion.Text = PubgVersionComboBox.SelectedItem is PubgVersion version ? version.DisplayName : "—";
        SummaryQuality.Text = SelectedContent(SmoothButton, BalancedButton, HdButton, HdrButton, UltraHdButton, UhdButton) ?? "—";
        SummaryFps.Text = SelectedContent(LowButton, MediumButton, HighButton, UltraButton, ExtremeButton, Fps90Button, Fps120Button) ?? "—";
        SummaryStyle.Text = SelectedStyleOrNull() ?? "—";
        SummaryShadow.Text = ShadowEnableButton.IsChecked == true
            ? "Enabled"
            : ShadowDisableButton.IsChecked == true
                ? "Disabled"
                : "—";
    }

    private string SelectedStyle() => SelectedStyleOrNull() ?? GraphicsSelection.Defaults.Style;

    private string? SelectedStyleOrNull() =>
        StyleButtons
            .FirstOrDefault(button => button.IsChecked == true)?.Tag?.ToString();

    private static string? SelectedContent(params RadioButton[] buttons) =>
        buttons.FirstOrDefault(button => button.IsChecked == true)?.Content?.ToString();

    private static void SelectContent(string? content, IEnumerable<RadioButton> buttons)
    {
        foreach (var button in buttons)
        {
            button.IsChecked = string.Equals(button.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase);
        }
    }
}
