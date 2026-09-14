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
    private readonly IGameLoopService _gameLoop;
    private readonly IWindowsToolsService _windowsTools;
    private readonly IGameLoopPerformanceEngine _performanceEngine;
    private readonly IUpdateService _updates;

    private CancellationTokenSource? _connectionCancellation;
    private IDisposable? _chromeHook;
    private bool _suppressSelection;
    private bool _isBusy;

    public MainWindow()
        : this(null, null, null)
    {
    }

    public MainWindow(
        IGameLoopService? gameLoop = null,
        IWindowsToolsService? windowsTools = null,
        IUpdateService? updates = null)
    {
        InitializeComponent();
        var runner = new ProcessRunner();
        var registry = new RegistryService();
        var adb = new AdbClient(runner, registry);

        _gameLoop = gameLoop ?? new GameLoopService(registry, adb);
        _windowsTools = windowsTools ?? new WindowsToolsService(runner, registry);
        _performanceEngine = _windowsTools;
        _updates = updates ?? new UpdateService(runner);

        PubgVersionComboBox.DisplayMemberPath = nameof(PubgVersion.DisplayName);
        ShortcutComboBox.DisplayMemberPath = nameof(PubgVersion.DisplayName);

        IpadComboBox.ItemsSource = IpadPresetCatalog.Presets.Select(preset => preset.DisplayName).ToList();
        IpadComboBox.SelectedIndex = -1;
        UpdateIpadPresetDetails();

        DnsComboBox.ItemsSource = DnsCatalog.Labels;
        DnsComboBox.SelectedIndex = 0;

        ShortcutComboBox.ItemsSource = GameLoopService.PubgVersions
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

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        if (_gameLoop.IsGameLoopConnected)
        {
            DisconnectFromGameLoop();
            return;
        }

        await ConnectToGameLoopAsync();
    }

    private void DisconnectFromGameLoop()
    {
        CancelAndDisposeConnection();
        _gameLoop.Disconnect();
        AdbClient.KillAdb();
        ResetConnectionState("Disconnected from GameLoop.");
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
            result = await _gameLoop.ConnectAsync(_connectionCancellation.Token);
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
                : GameLoopService.PubgVersions.Select(pair => new PubgVersion(pair.Key, pair.Value)).ToList();
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
            SetStatus(result.Message, isError: true);
            var danger = FindResource("Danger") as Brush ?? Brushes.Crimson;
            var dangerGlow = new DropShadowEffect { Color = Color.FromRgb(0xEF, 0x44, 0x44), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.85 };
            TopConnectionDot.Fill = danger;
            TopConnectionDot.Effect = dangerGlow;
            TopConnectionPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xEF, 0x44, 0x44));
            SidebarConnectionDot.Fill = danger;
            SidebarConnectionDot.Effect = dangerGlow;
            TopConnectionText.Text = "Connection failed";
            SidebarConnectionText.Text = "FAILED";
            return;
        }

        if (_gameLoop.IsConnected)
        {
            await ApplyLoadedSettingsAsync(_connectionCancellation?.Token ?? CancellationToken.None);
            SetConnectedState(result.Message);
        }
        else if (_gameLoop.IsGameLoopConnected)
        {
            SetTransportConnectedState(result.Message);
        }
        else
        {
            SetStatus(result.Message);
            ConnectionDetail.Text = "Select the PUBG Mobile version to load its settings.";
            var success = FindResource("Success") as Brush ?? Brushes.LimeGreen;
            var emeraldGlow = new DropShadowEffect { Color = Color.FromRgb(0x10, 0xB9, 0x81), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 };
            TopConnectionDot.Fill = success;
            TopConnectionDot.Effect = emeraldGlow;
            TopConnectionPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x10, 0xB9, 0x81));
            TopConnectionText.Text = "GameLoop connected";
            SidebarConnectionDot.Fill = success;
            SidebarConnectionDot.Effect = emeraldGlow;
            SidebarConnectionText.Text = "CONNECTED";
            SidebarAdbText.Text = "ADB: Connected";
            SummaryAdb.Text = "Connected";
        }
    }

    private async void PubgVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || _isBusy || PubgVersionComboBox.SelectedItem is not PubgVersion version || _gameLoop.IsConnected) return;
        _isBusy = true;
        SetStatus($"Loading {version.DisplayName}...");
        var cancellationToken = _connectionCancellation?.Token ?? CancellationToken.None;
        try
        {
            var result = await _gameLoop.LoadVersionAsync(version.PackageName, cancellationToken);
            if (result.Success)
            {
                await ApplyLoadedSettingsAsync(cancellationToken);
                SetConnectedState(result.Message);
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
            SelectedContent(SmoothButton, BalancedButton, HdButton, HdrButton, UltraHdButton, UhdButton) ?? "Smooth",
            SelectedContent(LowButton, MediumButton, HighButton, UltraButton, ExtremeButton, Fps90Button, Fps120Button) ?? "Low",
            SelectedStyle(),
            ShadowEnableButton.IsChecked == true,
            KoreanFullHdButton.IsChecked == true &&
            _gameLoop.CurrentPackage?.Equals("com.pubg.krmobile", StringComparison.OrdinalIgnoreCase) == true);

        _isBusy = true;
        SetBusyState(true);
        SetStatus("Applying graphics settings...");
        OperationResult result;
        var cancellationToken = _connectionCancellation?.Token ?? CancellationToken.None;
        try
        {
            result = await _gameLoop.ApplyGraphicsAsync(selection, cancellationToken);
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
        try
        {
            var update = await _updates.CheckAsync();
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
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Update check failed: {ex.Message}", isError: true);
        }

        await RefreshOptimizerProfileAsync();
    }

    private void StyleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton selected) return;
        foreach (var button in new[] { ClassicButton, ColorfulButton, RealisticButton, SoftButton, MovieButton })
        {
            if (!ReferenceEquals(button, selected)) button.IsChecked = false;
        }
        UpdateSummary();
    }

    private async void TempCleanerButton_Click(object sender, RoutedEventArgs e) =>
        await RunToolAsync(TempCleanerButton, () => _windowsTools.CleanTempAsync(), OptimizerStatusText);

    private async void SmartSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunToolAsync(SmartSettingsButton, () => _performanceEngine.ApplySmartSettings(), OptimizerStatusText);
        await RefreshOptimizerProfileAsync();
    }

    private async void GameLoopOptimizerButton_Click(object sender, RoutedEventArgs e) =>
        await RunToolAsync(GameLoopOptimizerButton, () => _performanceEngine.OptimizeGameLoop(), OptimizerStatusText);

    private async void AllRecommendedButton_Click(object sender, RoutedEventArgs e)
    {
        await RunToolAsync(AllRecommendedButton, () => _performanceEngine.OptimizeAll(), OptimizerStatusText);
        await RefreshOptimizerProfileAsync();
    }

    private async void ForceCloseButton_Click(object sender, RoutedEventArgs e) =>
        await RunToolAsync(ForceCloseButton, () => _windowsTools.KillGameLoopProcesses(), OptimizerStatusText);

    private async void PerformanceSessionButton_Click(object sender, RoutedEventArgs e) =>
        await RunToolAsync(PerformanceSessionButton, () => _performanceEngine.ApplyPerformanceSession(), OptimizerStatusText);

    private async void RestoreSessionButton_Click(object sender, RoutedEventArgs e) =>
        await RunToolAsync(RestoreSessionButton, () => _performanceEngine.RestorePerformanceSessionAsync(), OptimizerStatusText);

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
            UpdateOptimizerPanel(hardware, plan);
        }
        catch (Exception ex)
        {
            OptimizerStatusText.Text = $"Hardware detection failed: {ex.Message}";
            OptimizerStatusText.Foreground = FindResource("Danger") as Brush;
        }
        finally
        {
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

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CancelAndDisposeConnection();
        _chromeHook?.Dispose();
        _chromeHook = null;

        // Restore performance session synchronously before shutdown so the
        // system's power plan and process priorities are not left modified.
        // A bounded timeout ensures shutdown can never hang indefinitely.
        try
        {
            var restoreTask = _performanceEngine.RestorePerformanceSessionAsync();
            restoreTask.Wait(AppConstants.Timeouts.ShutdownRestoreTimeout);
        }
        catch (AggregateException)
        {
            // Restoration failed or timed out; the system changes are best-effort.
            // Logging infrastructure is unavailable at shutdown.
        }
        catch (Exception)
        {
            // Best-effort teardown during window closure.
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

    private async void DnsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DnsComboBox.SelectedItem is not string label || !DnsCatalog.TryGet(label, out var entry) || entry is null) return;
        var selectedLabel = label;
        DnsStatusText.Text = $"{entry.ShortName} • Testing response...";
        var ping = await _windowsTools.PingDnsAsync(entry.Primary);

        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

        try
        {
            Dispatcher.Invoke(() =>
            {
                if (DnsComboBox.SelectedItem is not string currentLabel ||
                    !string.Equals(currentLabel, selectedLabel, StringComparison.OrdinalIgnoreCase)) return;

                DnsStatusText.Text = ping is null
                    ? $"{entry.ShortName} • No response from DNS server"
                    : $"{entry.ShortName} • Ping: {ping}ms • Ready to apply";
            });
        }
        catch (Exception)
        {
            // Suppress dispatcher invocation errors during application shutdown.
        }
    }

    private async void ChangeDnsButton_Click(object sender, RoutedEventArgs e)
    {
        if (DnsComboBox.SelectedItem is not string label || !DnsCatalog.TryGet(label, out var entry) || entry is null) return;
        var result = await RunToolAsync(ChangeDnsButton, () => _windowsTools.ChangeDns(entry.Primary, entry.Secondary), null);
        DnsStatusText.Text = result.Success
            ? $"{entry.ShortName} • Applied: {entry.Primary} / {entry.Secondary}"
            : result.Message;
    }

    private async void ChangeIpadButton_Click(object sender, RoutedEventArgs e)
    {
        var preset = GetSelectedIpadPreset();
        if (preset is null) return;
        var result = await RunToolAsync(ChangeIpadButton, () => _windowsTools.SetIpadResolution(preset.Width, preset.Height), null);
        IpadApplyStatusText.Text = result.Success
            ? $"Applied: {preset.Label} • {preset.Width} × {preset.Height}. Restart GameLoop to load it."
            : result.Message;
        IpadApplyStatusText.Foreground = FindResource(result.Success ? "Accent" : "Danger") as Brush;
    }

    private void IpadComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateIpadPresetDetails();

    private void UpdateIpadPresetDetails()
    {
        var preset = GetSelectedIpadPreset();
        if (preset is not null)
        {
            IpadPresetDetailsText.Text = preset.Details;
            IpadApplyStatusText.Text = "Ready to apply this profile.";
            IpadApplyStatusText.Foreground = FindResource("TextMuted") as Brush;
            ChangeIpadButton.IsEnabled = true;
        }
        else
        {
            IpadPresetDetailsText.Text = "Choose a tested iPad display profile.";
            IpadApplyStatusText.Text = "No profile selected.";
            IpadApplyStatusText.Foreground = FindResource("TextMuted") as Brush;
            ChangeIpadButton.IsEnabled = false;
        }
    }

    private IpadResolutionPreset? GetSelectedIpadPreset() =>
        IpadPresetCatalog.FindByDisplayName(IpadComboBox.SelectedItem as string);

    private async void ResetIpadButton_Click(object sender, RoutedEventArgs e) =>
        await RunToolAsync(ResetIpadButton, () => _windowsTools.ResetIpadResolution(), null);

    private async void CreateShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (ShortcutComboBox.SelectedItem is not PubgVersion version) return;
        await RunToolAsync(CreateShortcutButton, () => _windowsTools.CreateShortcut(version.DisplayName, version.PackageName), null);
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

        var iconPath = Path.Combine(AppContext.BaseDirectory, AppConstants.Assets.DirectoryName, AppConstants.Assets.IconsDirectoryName, $"{version.PackageName}.ico");
        ShortcutIcon.Source = IconImageLoader.TryLoadIcon(iconPath);
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton button || button.Tag is not string page) return;
        GraphicsView.Visibility = page == "Graphics" ? Visibility.Visible : Visibility.Collapsed;
        OptimizerView.Visibility = page == "Optimizer" ? Visibility.Visible : Visibility.Collapsed;
        NetworkView.Visibility = page == "Network" ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsView.Visibility = page == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        AboutView.Visibility = page == "About" ? Visibility.Visible : Visibility.Collapsed;
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
        AboutView.Margin = pageMargin;
    }

    private async Task<OperationResult> RunToolAsync(Button button, Func<OperationResult> action, TextBlock? resultLabel)
    {
        if (_isBusy) return OperationResult.Fail("Another operation is already running.");
        _isBusy = true;
        button.IsEnabled = false;
        SetStatus("Working...");
        if (resultLabel is not null)
        {
            resultLabel.Text = "Working...";
            resultLabel.Foreground = FindResource("TextSecondary") as Brush;
            if (ReferenceEquals(resultLabel, OptimizerStatusText) && OptimizerActivityText is not null)
            {
                OptimizerActivityText.Text = "Running each boost step...";
                OptimizerActivityText.Foreground = FindResource("TextSecondary") as Brush;
            }
        }

        OperationResult result;
        try { result = await Task.Run(action); }
        catch (Exception ex) { result = OperationResult.Fail(ex.Message); }
        finally { button.IsEnabled = true; _isBusy = false; }

        if (resultLabel is not null)
        {
            if (ReferenceEquals(resultLabel, OptimizerStatusText))
            {
                RenderOptimizerReport(result);
            }
            else
            {
                resultLabel.Text = result.Message;
                resultLabel.Foreground = FindResource(result.Success ? "Success" : "Danger") as Brush;
            }
        }

        var statusLine = ReferenceEquals(resultLabel, OptimizerStatusText)
            ? ActivityReportFormatter.Format(result).StatusLine
            : result.Message;
        SetStatus(statusLine, !result.Success);
        return result;
    }

    private async Task<OperationResult> RunToolAsync(Button button, Func<Task<OperationResult>> action, TextBlock? resultLabel)
    {
        if (_isBusy) return OperationResult.Fail("Another operation is already running.");
        _isBusy = true;
        button.IsEnabled = false;
        SetStatus("Working...");
        if (resultLabel is not null)
        {
            resultLabel.Text = "Working...";
            resultLabel.Foreground = FindResource("TextSecondary") as Brush;
            if (ReferenceEquals(resultLabel, OptimizerStatusText) && OptimizerActivityText is not null)
            {
                OptimizerActivityText.Text = "Running each boost step...";
                OptimizerActivityText.Foreground = FindResource("TextSecondary") as Brush;
            }
        }

        OperationResult result;
        try { result = await action(); }
        catch (Exception ex) { result = OperationResult.Fail(ex.Message); }
        finally { button.IsEnabled = true; _isBusy = false; }

        if (resultLabel is not null)
        {
            if (ReferenceEquals(resultLabel, OptimizerStatusText))
            {
                RenderOptimizerReport(result);
            }
            else
            {
                resultLabel.Text = result.Message;
                resultLabel.Foreground = FindResource(result.Success ? "Success" : "Danger") as Brush;
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
            OptimizerActivityText.Foreground = FindResource(display.IsError ? "Danger" : "TextSecondary") as Brush;
        }

        var statusBrush = result.Success
            ? result.IsSkipped ? "Accent" : "Success"
            : "Danger";
        OptimizerStatusText.Foreground = FindResource(statusBrush) as Brush;
    }

    private async Task ApplyLoadedSettingsAsync(CancellationToken cancellationToken)
    {
        _suppressSelection = true;
        try
        {
            SelectContent(_gameLoop.GetGraphicsQuality(), new[] { SmoothButton, BalancedButton, HdButton, HdrButton, UltraHdButton, UhdButton });
            SelectContent(_gameLoop.GetFrameRate(), new[] { LowButton, MediumButton, HighButton, UltraButton, ExtremeButton, Fps90Button, Fps120Button });
            var style = _gameLoop.GetGraphicsStyle();
            foreach (var button in new[] { ClassicButton, ColorfulButton, RealisticButton, SoftButton, MovieButton })
            {
                button.IsChecked = string.Equals(button.Tag?.ToString(), style, StringComparison.OrdinalIgnoreCase);
            }

            var shadow = await _gameLoop.GetShadowAsync(cancellationToken);
            ShadowDisableButton.IsChecked = string.Equals(shadow, "Disable", StringComparison.OrdinalIgnoreCase);
            ShadowEnableButton.IsChecked = string.Equals(shadow, "Enable", StringComparison.OrdinalIgnoreCase);
            ShadowDisableButton.IsEnabled = true;
            ShadowEnableButton.IsEnabled = true;

            var isKoreanVersion = string.Equals(_gameLoop.CurrentPackage, "com.pubg.krmobile", StringComparison.OrdinalIgnoreCase);
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

    private void SetConnectedState(string message)
    {
        var success = FindResource("Success") as Brush ?? Brushes.LimeGreen;
        var emeraldGlow = new DropShadowEffect { Color = Color.FromRgb(0x10, 0xB9, 0x81), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 };
        ConnectionDot.Fill = success;
        ConnectionDot.Effect = emeraldGlow;
        TopConnectionDot.Fill = success;
        TopConnectionDot.Effect = emeraldGlow;
        TopConnectionText.Text = "Connected to GameLoop";
        TopConnectionPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x10, 0xB9, 0x81));
        SidebarConnectionDot.Fill = success;
        SidebarConnectionDot.Effect = emeraldGlow;
        SidebarConnectionText.Text = "CONNECTED";
        SidebarAdbText.Text = "ADB: Connected";
        SummaryAdb.Text = "Connected";
        ConnectButton.Content = "DISCONNECT";
        ApplyButton.IsEnabled = true;
        ConnectionDetail.Text = message;
        SetStatus(message);
        UpdateSummary();
    }

    private void SetTransportConnectedState(string message)
    {
        var success = FindResource("Success") as Brush ?? Brushes.LimeGreen;
        var emeraldGlow = new DropShadowEffect { Color = Color.FromRgb(0x10, 0xB9, 0x81), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 };
        ConnectionDot.Fill = success;
        ConnectionDot.Effect = emeraldGlow;
        TopConnectionDot.Fill = success;
        TopConnectionDot.Effect = emeraldGlow;
        TopConnectionText.Text = "Connected to GameLoop";
        TopConnectionPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x10, 0xB9, 0x81));
        SidebarConnectionDot.Fill = success;
        SidebarConnectionDot.Effect = emeraldGlow;
        SidebarConnectionText.Text = "CONNECTED";
        SidebarAdbText.Text = "ADB: Connected";
        SummaryAdb.Text = "Connected";
        ConnectButton.Content = "DISCONNECT";
        ApplyButton.IsEnabled = false;
        ShadowDisableButton.IsEnabled = false;
        ShadowEnableButton.IsEnabled = false;
        ConnectionDetail.Text = message;
        SetStatus(message);
        UpdateSummary();
    }

    private void ResetConnectionState(string message)
    {
        var muted = FindResource("TextMuted") as Brush ?? Brushes.Gray;
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
        ConnectionDetail.Text = "Connect to GameLoop to load current settings.";
        ConnectButton.Content = "CONNECT";
        ApplyButton.IsEnabled = false;
        ShadowDisableButton.IsEnabled = false;
        ShadowEnableButton.IsEnabled = false;
        ShadowDisableButton.IsChecked = false;
        ShadowEnableButton.IsChecked = false;
        KoreanResolutionPanel.Visibility = Visibility.Collapsed;
        KoreanFullHdButton.IsEnabled = false;
        KoreanFullHdButton.IsChecked = false;
        SetStatus(message);
        UpdateSummary();
    }

    private void SetBusyState(bool busy)
    {
        ConnectButton.IsEnabled = !busy;
        RefreshConnectionButton.IsEnabled = !busy;
        ApplyButton.IsEnabled = !busy && _gameLoop.IsConnected;
        ShadowDisableButton.IsEnabled = !busy && _gameLoop.IsConnected;
        ShadowEnableButton.IsEnabled = !busy && _gameLoop.IsConnected;
        KoreanFullHdButton.IsEnabled = !busy &&
            string.Equals(_gameLoop.CurrentPackage, "com.pubg.krmobile", StringComparison.OrdinalIgnoreCase);
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = FindResource(isError ? "Danger" : "TextSecondary") as Brush;
    }

    private void UpdateSummary()
    {
        SummaryVersion.Text = PubgVersionComboBox.SelectedItem is PubgVersion version ? version.DisplayName : "—";
        SummaryQuality.Text = SelectedContent(SmoothButton, BalancedButton, HdButton, HdrButton, UltraHdButton, UhdButton) ?? "—";
        SummaryFps.Text = SelectedContent(LowButton, MediumButton, HighButton, UltraButton, ExtremeButton, Fps90Button, Fps120Button) ?? "—";
        SummaryStyle.Text = SelectedStyle();
        SummaryShadow.Text = ShadowEnableButton.IsChecked == true
            ? "Enabled"
            : ShadowDisableButton.IsChecked == true
                ? "Disabled"
                : "—";
    }

    private string SelectedStyle() =>
        new[] { ClassicButton, ColorfulButton, RealisticButton, SoftButton, MovieButton }
            .FirstOrDefault(button => button.IsChecked == true)?.Tag?.ToString() ?? "Classic";

    private static string? SelectedContent(params RadioButton[] buttons) =>
        buttons.FirstOrDefault(button => button.IsChecked == true)?.Content?.ToString();

    private static void SelectContent(string content, IEnumerable<RadioButton> buttons)
    {
        foreach (var button in buttons)
        {
            button.IsChecked = string.Equals(button.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase);
        }
    }
}
