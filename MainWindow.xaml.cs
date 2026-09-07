using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Nexora.Models;
using Nexora.Services;
using Nexora.Services.Performance;

namespace Nexora;

public partial class MainWindow : Window
{
    private readonly ProcessRunner _runner = new();
    private readonly RegistryService _registry = new();
    private readonly AdbClient _adb;
    private readonly GameLoopService _gameLoop;
    private readonly WindowsToolsService _windowsTools;
    private readonly IGameLoopPerformanceEngine _performanceEngine;
    private readonly UpdateService _updates = new();
    private readonly Dictionary<string, string[]> _dnsServers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Google DNS - 8.8.8.8"] = new[] { "8.8.8.8", "8.8.4.4" },
        ["Cloudflare DNS - 1.1.1.1"] = new[] { "1.1.1.1", "1.0.0.1" },
        ["Quad9 DNS - 9.9.9.9"] = new[] { "9.9.9.9", "149.112.112.112" },
        ["Cisco Umbrella - 208.67.222.222"] = new[] { "208.67.222.222", "208.67.220.220" },
        ["Yandex DNS - 77.88.8.1"] = new[] { "77.88.8.1", "77.88.8.8" }
    };

    private static readonly IReadOnlyList<IpadResolutionPreset> IpadPresets = new[]
    {
        new IpadResolutionPreset("Competitive 4:3 (Recommended)", 1920, 1440, "Best balance of clarity and emulator performance"),
        new IpadResolutionPreset("Balanced 4:3", 1600, 1200, "Lower load for entry-level systems"),
        new IpadResolutionPreset("Classic iPad 4:3", 2048, 1536, "Native-style 4:3 iPad canvas"),
        new IpadResolutionPreset("iPad 10.2-inch", 2160, 1620, "Apple 4:3 display profile"),
        new IpadResolutionPreset("iPad Air 11-inch", 2360, 1640, "Wide iPad Air display profile"),
        new IpadResolutionPreset("iPad Pro 11-inch (classic)", 2388, 1668, "Previous-generation Pro display profile"),
        new IpadResolutionPreset("iPad Pro 11-inch (current)", 2420, 1668, "Current Pro display profile"),
        new IpadResolutionPreset("iPad mini 8.3-inch", 2266, 1488, "Compact iPad display profile"),
        new IpadResolutionPreset("iPad Pro 12.9-inch", 2732, 2048, "High-detail 4:3 Pro canvas"),
        new IpadResolutionPreset("iPad Pro 13-inch (current)", 2752, 2064, "Maximum detail; highest emulator load")
    };

    private CancellationTokenSource? _connectionCancellation;
    private bool _suppressSelection;
    private bool _isBusy;
    private HardwareSnapshot? _hardwareSnapshot;
    private OptimizerPlan? _optimizerPlan;

    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    public MainWindow()
    {
        InitializeComponent();
        _adb = new AdbClient(_runner, _registry);
        _gameLoop = new GameLoopService(_registry, _adb);
        _windowsTools = new WindowsToolsService(_runner, _registry);
        _performanceEngine = _windowsTools;

        PubgVersionComboBox.DisplayMemberPath = nameof(PubgVersion.DisplayName);
        ShortcutComboBox.DisplayMemberPath = nameof(PubgVersion.DisplayName);
        // The custom ComboBox template renders string items reliably. Keep the
        // strongly typed preset list in code and map the selected index back to
        // its width/height when the user applies it.
        IpadComboBox.ItemsSource = IpadPresets.Select(preset => preset.DisplayName).ToList();
        IpadComboBox.SelectedIndex = -1;
        UpdateIpadPresetDetails();
        DnsComboBox.ItemsSource = _dnsServers.Keys.ToList();
        DnsComboBox.SelectedIndex = 0;
        ShortcutComboBox.ItemsSource = GameLoopService.PubgVersions
            .Select(pair => new PubgVersion(pair.Key, pair.Value))
            .ToList();
        ShortcutComboBox.SelectedIndex = 0;
        UpdateShortcutPreview();

        ClassicButton.IsChecked = true;
        SmoothButton.IsChecked = true;
        LowButton.IsChecked = true;
        SetStatus("Ready to connect to GameLoop.");
        UpdateSummary();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        if (_gameLoop.IsConnected)
        {
            _connectionCancellation?.Cancel();
            _gameLoop.Disconnect();
            AdbClient.KillAdb();
            ResetConnectionState("Disconnected from GameLoop.");
            return;
        }

        _isBusy = true;
        _connectionCancellation = new CancellationTokenSource();
        SetBusyState(true);
        SetStatus("Connecting to GameLoop...");
        ConnectionResult result;
        try
        {
            result = await Task.Run(() => _gameLoop.Connect(_connectionCancellation.Token));
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
            if (result.InstalledVersions.Count == 1) PubgVersionComboBox.SelectedIndex = 0;
        }
        finally
        {
            _suppressSelection = false;
        }

        if (!result.Success)
        {
            SetStatus(result.Message, isError: true);
            return;
        }

        if (_gameLoop.IsConnected)
        {
            ApplyLoadedSettings();
            SetConnectedState(result.Message);
        }
        else
        {
            SetStatus(result.Message);
            ConnectionDetail.Text = "Select the PUBG Mobile version to load its settings.";
            TopConnectionDot.Fill = FindResource("Success") as Brush;
            TopConnectionText.Text = "GameLoop connected";
            SidebarConnectionDot.Fill = FindResource("Success") as Brush;
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
        try
        {
            var result = await Task.Run(() => _gameLoop.LoadVersion(version.PackageName));
            if (result.Success)
            {
                ApplyLoadedSettings();
                SetConnectedState(result.Message);
            }
            else SetStatus(result.Message, isError: true);
        }
        finally { _isBusy = false; }
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
        try { result = await Task.Run(() => _gameLoop.ApplyGraphics(selection)); }
        catch (Exception ex) { result = OperationResult.Fail(ex.Message); }
        finally { _isBusy = false; SetBusyState(false); }
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

    private async void TempCleanerButton_Click(object sender, RoutedEventArgs e) => await RunToolAsync(TempCleanerButton, () => _windowsTools.CleanTemp(), OptimizerStatusText);

    private async void SmartSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunToolAsync(SmartSettingsButton, () => _performanceEngine.ApplySmartSettings(), OptimizerStatusText);
        await RefreshOptimizerProfileAsync();
    }

    private async void GameLoopOptimizerButton_Click(object sender, RoutedEventArgs e)
    {
        await RunToolAsync(GameLoopOptimizerButton, () => _performanceEngine.OptimizeGameLoop(), OptimizerStatusText);
    }

    private async void AllRecommendedButton_Click(object sender, RoutedEventArgs e)
    {
        await RunToolAsync(AllRecommendedButton, () => _performanceEngine.OptimizeAll(), OptimizerStatusText);
        await RefreshOptimizerProfileAsync();
    }

    private async void ForceCloseButton_Click(object sender, RoutedEventArgs e) => await RunToolAsync(ForceCloseButton, () => _windowsTools.KillGameLoopProcesses(), OptimizerStatusText);

    private async void PerformanceSessionButton_Click(object sender, RoutedEventArgs e) => await RunToolAsync(PerformanceSessionButton, () => _performanceEngine.ApplyPerformanceSession(), OptimizerStatusText);
    private async void RestoreSessionButton_Click(object sender, RoutedEventArgs e) => await RunToolAsync(RestoreSessionButton, () => _performanceEngine.RestorePerformanceSession(), OptimizerStatusText);

    private async void RefreshOptimizerButton_Click(object sender, RoutedEventArgs e) => await RefreshOptimizerProfileAsync();

    private async Task RefreshOptimizerProfileAsync()
    {
        if (_isBusy || RefreshOptimizerButton is null) return;
        RefreshOptimizerButton.IsEnabled = false;
        try
        {
            var detected = await Task.Run(() =>
            {
                var hardware = _performanceEngine.GetHardwareSnapshot();
                return (Hardware: hardware, Plan: _performanceEngine.GetRecommendedPlan(hardware));
            });
            _hardwareSnapshot = detected.Hardware;
            _optimizerPlan = detected.Plan;
            UpdateOptimizerPanel(detected.Hardware, detected.Plan);
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
        HardwareProfileText.Text = $"{plan.Tier.ToUpperInvariant()} HARDWARE PROFILE";
        HardwareGpuText.Text = $"{hardware.GpuVendor} • {hardware.GpuName}";
        HardwareCpuText.Text = $"{hardware.PhysicalCores} cores / {hardware.LogicalCores} threads";
        HardwareMemoryText.Text = $"{hardware.TotalMemoryGb} GB RAM";
        HardwareDisplayText.Text = $"{hardware.RefreshRateHz} Hz display";
        HardwarePowerText.Text = hardware.IsLaptop
            ? hardware.IsOnAcPower ? "AC / performance ready" : "Battery / balanced"
            : "Desktop / performance ready";
        HardwareVirtualizationText.Text = hardware.VirtualizationEnabled
            ? hardware.HypervisorDetected ? "VT on / hypervisor" : "VT on"
            : "VT off";

        PlanTierText.Text = $"{plan.Tier.ToUpperInvariant()} • {plan.PowerMode}";
        PlanCpuText.Text = $"{plan.EmulatorCpuCores} physical cores";
        PlanMemoryText.Text = $"{plan.EmulatorMemoryMb / 1024d:0.#} GB";
        PlanRenderText.Text = $"{plan.ContentScale}x • FXAA {plan.FxaaQuality}";
        PlanFpsText.Text = plan.RecommendedFps;
        PlanGpuText.Text = plan.GpuRoute;
        SmartPlanText.Text = $"{plan.GpuRoute}; {plan.EmulatorCpuCores} CPU cores and {plan.EmulatorMemoryMb / 1024d:0.#} GB RAM recommended. Frame rate mode: {plan.RecommendedFps}.";
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _performanceEngine.RestorePerformanceSession();
    }

    private async void DnsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DnsComboBox.SelectedItem is not string label || !_dnsServers.TryGetValue(label, out var servers)) return;
        var selectedLabel = label;
        DnsStatusText.Text = $"{selectedLabel.Split(" - ")[0]} • Testing response...";
        var ping = await Task.Run(() => _windowsTools.PingDns(servers[0]));
        if (DnsComboBox.SelectedItem is not string currentLabel ||
            !string.Equals(currentLabel, selectedLabel, StringComparison.OrdinalIgnoreCase)) return;

        DnsStatusText.Text = ping is null
            ? $"{selectedLabel.Split(" - ")[0]} • No response from DNS server"
            : $"{selectedLabel.Split(" - ")[0]} • Ping: {ping}ms • Ready to apply";
    }

    private async void ChangeDnsButton_Click(object sender, RoutedEventArgs e)
    {
        if (DnsComboBox.SelectedItem is not string label || !_dnsServers.TryGetValue(label, out var servers)) return;
        var result = await RunToolAsync(ChangeDnsButton, () => _windowsTools.ChangeDns(servers[0], servers[1]), null);
        DnsStatusText.Text = result.Success
            ? $"{label.Split(" - ")[0]} • Applied: {servers[0]} / {servers[1]}"
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
        IpadComboBox.SelectedItem is string selectedDisplayName
            ? IpadPresets.FirstOrDefault(preset => string.Equals(preset.DisplayName, selectedDisplayName, StringComparison.Ordinal))
            : null;

    private async void ResetIpadButton_Click(object sender, RoutedEventArgs e) => await RunToolAsync(ResetIpadButton, () => _windowsTools.ResetIpadResolution(), null);

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

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", $"{version.PackageName}.ico");
        if (!File.Exists(iconPath))
        {
            ShortcutIcon.Source = null;
            return;
        }

        try
        {
            var icon = new BitmapImage();
            icon.BeginInit();
            icon.UriSource = new Uri(iconPath, UriKind.Absolute);
            icon.CacheOption = BitmapCacheOption.OnLoad;
            icon.EndInit();
            ShortcutIcon.Source = icon;
        }
        catch (Exception)
        {
            ShortcutIcon.Source = null;
        }
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
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WindowProc);
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 1320;
        var tight = e.NewSize.Width < 1200;
        var sidebarWidth = tight ? 228 : compact ? 240 : 260;
        SidebarColumn.Width = new GridLength(sidebarWidth);
        TitleBrandColumn.Width = new GridLength(sidebarWidth);

        var pageMargin = tight
            ? new Thickness(24, 20, 24, 18)
            : compact
                ? new Thickness(32, 22, 32, 20)
                : new Thickness(48, 26, 48, 24);

        GraphicsView.Margin = pageMargin;
        OptimizerView.Margin = pageMargin;
        NetworkView.Margin = pageMargin;
        ShortcutsView.Margin = pageMargin;
        AboutView.Margin = pageMargin;
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmGetMinMaxInfo)
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                    info.MaxPosition = new Point32(
                        monitorInfo.Work.Left - monitorInfo.Monitor.Left,
                        monitorInfo.Work.Top - monitorInfo.Monitor.Top);
                    info.MaxSize = new Point32(
                        monitorInfo.Work.Right - monitorInfo.Work.Left,
                        monitorInfo.Work.Bottom - monitorInfo.Work.Top);
                    Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
                    handled = true;
                }
            }
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;

        public Point32(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point32 Reserved;
        public Point32 MaxSize;
        public Point32 MaxPosition;
        public Point32 MinTrackSize;
        public Point32 MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect32 Monitor;
        public Rect32 Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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
        }
        OperationResult result;
        try { result = await Task.Run(action); }
        catch (Exception ex) { result = OperationResult.Fail(ex.Message); }
        finally { button.IsEnabled = true; _isBusy = false; }
        if (resultLabel is not null)
        {
            resultLabel.Text = result.Message;
            resultLabel.Foreground = FindResource(result.Success ? "Success" : "Danger") as Brush;
        }
        SetStatus(result.Message, !result.Success);
        return result;
    }

    private void ApplyLoadedSettings()
    {
        _suppressSelection = true;
        try
        {
            SelectContent(_gameLoop.GetGraphicsQuality(), new[] { SmoothButton, BalancedButton, HdButton, HdrButton, UltraHdButton, UhdButton });
            SelectContent(_gameLoop.GetFrameRate(), new[] { LowButton, MediumButton, HighButton, UltraButton, ExtremeButton, Fps90Button, Fps120Button });
            var style = _gameLoop.GetGraphicsStyle();
            foreach (var button in new[] { ClassicButton, ColorfulButton, RealisticButton, SoftButton, MovieButton }) button.IsChecked = string.Equals(button.Tag?.ToString(), style, StringComparison.OrdinalIgnoreCase);
            var shadow = _gameLoop.GetShadow();
            ShadowDisableButton.IsChecked = string.Equals(shadow, "Disable", StringComparison.OrdinalIgnoreCase);
            ShadowEnableButton.IsChecked = string.Equals(shadow, "Enable", StringComparison.OrdinalIgnoreCase);
            ShadowDisableButton.IsEnabled = true;
            ShadowEnableButton.IsEnabled = true;
            var isKoreanVersion = string.Equals(_gameLoop.CurrentPackage, "com.pubg.krmobile", StringComparison.OrdinalIgnoreCase);
            KoreanResolutionPanel.Visibility = isKoreanVersion ? Visibility.Visible : Visibility.Collapsed;
            KoreanFullHdButton.IsEnabled = isKoreanVersion;
            KoreanFullHdButton.IsChecked = isKoreanVersion;
        }
        finally { _suppressSelection = false; }
        UpdateSummary();
    }

    private void SetConnectedState(string message)
    {
        var success = FindResource("Success") as Brush ?? Brushes.LimeGreen;
        ConnectionDot.Fill = success;
        TopConnectionDot.Fill = success;
        TopConnectionText.Text = "Connected to GameLoop";
        SidebarConnectionDot.Fill = success;
        SidebarConnectionText.Text = "CONNECTED";
        SidebarAdbText.Text = "ADB: Connected";
        SummaryAdb.Text = "Connected";
        ConnectButton.Content = "DISCONNECT";
        ApplyButton.IsEnabled = true;
        ConnectionDetail.Text = message;
        SetStatus(message);
        UpdateSummary();
    }

    private void ResetConnectionState(string message)
    {
        var muted = FindResource("TextMuted") as Brush ?? Brushes.Gray;
        ConnectionDot.Fill = muted;
        TopConnectionDot.Fill = muted;
        TopConnectionText.Text = "Not connected";
        SidebarConnectionDot.Fill = muted;
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

    private string SelectedStyle() => new[] { ClassicButton, ColorfulButton, RealisticButton, SoftButton, MovieButton }.FirstOrDefault(button => button.IsChecked == true)?.Tag?.ToString() ?? "Classic";
    private static string? SelectedContent(params RadioButton[] buttons) => buttons.FirstOrDefault(button => button.IsChecked == true)?.Content?.ToString();

    private static void SelectContent(string content, IEnumerable<RadioButton> buttons)
    {
        foreach (var button in buttons) button.IsChecked = string.Equals(button.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase);
    }
}
