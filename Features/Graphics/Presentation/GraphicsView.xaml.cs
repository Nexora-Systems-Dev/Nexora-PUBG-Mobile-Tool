using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.GameLoop.Infrastructure;
using Nexora.Features.Graphics.Application;
using Nexora.Features.Graphics.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Infrastructure.Files;
using Nexora.Infrastructure.GameLoop;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;
using Nexora.UI.Presentation;

namespace Nexora.Features.Graphics.Presentation;

/// <summary>
/// The Graphics page: the selection controls, the connection panel and the live
/// configuration summary. Thin by design — every action delegates to
/// <see cref="GraphicsViewModel"/> and every display state comes back through
/// one of the render methods below.
/// </summary>
public partial class GraphicsView : UserControl
{
    private readonly GraphicsViewModel _viewModel;
    private bool _suppressSelection;
    private ToggleButton[]? _styleButtons;
    private RadioButton[]? _qualityButtons;
    private RadioButton[]? _frameRateButtons;

    public GraphicsView()
        : this(connection: null, graphics: null, adb: null, operationBus: null)
    {
    }

    public GraphicsView(
        IGameLoopConnection? connection = null,
        IGraphicsSettingsService? graphics = null,
        IAdbClient? adb = null,
        IPageOperationBus? operationBus = null)
    {
        InitializeComponent();

        _viewModel = BuildViewModel(connection, graphics, adb, operationBus);
        _viewModel.ConnectionStateChanged += OnConnectionStateChanged;
        _viewModel.StatusChanged += (message, isError) => SetStatus(message, isError);
        _viewModel.SettingsLoaded += OnSettingsLoaded;
        _viewModel.BusyVisualChanged += OnBusyVisualChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        PubgVersionComboBox.DisplayMemberPath = nameof(PubgVersion.DisplayName);

        ClassicButton.IsChecked = true;
        SmoothButton.IsChecked = true;
        LowButton.IsChecked = true;
        RenderSummary();
    }

    /// <summary>
    /// The page's orchestration seam. Exposed for the shell, which still owns
    /// the sidebar's connect button and the window-close cancellation.
    /// </summary>
    public GraphicsViewModel ViewModel => _viewModel;

    /// <summary>
    /// The page's status cell. The shell writes its own statuses through here
    /// — the surface stays in one place even though the bar moved with the page.
    /// </summary>
    public void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = GetBrush(isError ? "Danger" : "TextSecondary");
    }

    private static GraphicsViewModel BuildViewModel(
        IGameLoopConnection? connection,
        IGraphicsSettingsService? graphics,
        IAdbClient? adb,
        IPageOperationBus? operationBus)
    {
        // XAML constructs this view with the parameterless ctor, so the view
        // takes its ViewModel from the running container when there is one and
        // only falls back to a locally built graph for the designer and for
        // direct construction. The fallback shares one GameLoopService between
        // both facets, mirroring the container's factory-forwards, so the
        // connection and the profile store never fork.
        if (connection is null && graphics is null && adb is null && operationBus is null
            && System.Windows.Application.Current is App && App.Services is IServiceProvider services)
        {
            if (services.GetService<GraphicsViewModel>() is { } resolved) return resolved;
        }

        var registry = new RegistryService();
        var runner = new ProcessRunner();
        var pathResolver = new GameLoopPathResolver(registry);
        var adbClient = adb ?? new AdbClient(runner, pathResolver);
        var processSvc = new GameLoopProcessService(runner, pathResolver);
        GameLoopService? loopFallback = null;
        GameLoopService LoopFallback() => loopFallback ??= new GameLoopService(
            registry, adbClient,
            new GameLoopWorkingStorage(new PhysicalFileSystem(), new GameLoopWorkRootProvider()),
            new PhysicalFileSystem(), processSvc);

        return new GraphicsViewModel(
            connection ?? LoopFallback(),
            graphics ?? new GraphicsSettingsService(LoopFallback(), LoopFallback()),
            adbClient,
            operationBus ?? new PageOperationBus());
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e) => await _viewModel.ConnectAsync();

    private async void PubgVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (PubgVersionComboBox.SelectedItem is not PubgVersion version) return;
        await _viewModel.LoadVersionAsync(version);
    }

    private void SettingRadioButton_Checked(object sender, RoutedEventArgs e) => RenderSummary();

    private void StyleButton_Checked(object sender, RoutedEventArgs e)
    {
        // Mutual exclusion of the style cards is presentation: the cards are
        // independent toggles in the template, so the view keeps them exclusive.
        if (sender is not ToggleButton selected) return;
        foreach (var button in StyleButtons)
        {
            if (!ReferenceEquals(button, selected)) button.IsChecked = false;
        }
        RenderSummary();
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;

        var result = await _viewModel.ApplyAsync(BuildSelection());
        SetStatus(result.Message, !result.Success);
    }

    /// <summary>
    /// Assembles the apply payload from the selected controls. The Korean 1080p
    /// flag is gated by the ViewModel so the toggle, the summary and this
    /// payload cannot disagree about which version is loaded.
    /// </summary>
    private GraphicsSelection BuildSelection() => new(
        SelectedContent(QualityButtons) ?? GraphicsSelection.Defaults.Quality,
        SelectedContent(FrameRateButtons) ?? GraphicsSelection.Defaults.FrameRate,
        SelectedStyle(),
        ShadowEnableButton.IsChecked == true,
        KoreanFullHdButton.IsChecked == true && _viewModel.IsKoreanVersion);

    /// <summary>
    /// Paints the detected versions into the page's own list. Suppressed so the
    /// programmatic selection does not look like a user picking a version.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GraphicsViewModel.InstalledVersions)) return;

        _suppressSelection = true;
        try
        {
            var versions = _viewModel.InstalledVersions;
            PubgVersionComboBox.ItemsSource = versions;
            PubgVersionComboBox.SelectedIndex = versions.Count == 1 ? 0 : -1;
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    /// <summary>
    /// Repaints the selection controls from a freshly read profile. Null or
    /// unrecognized values clear the controls rather than rendering as a
    /// confident setting; the whole repaint is suppressed because every checked
    /// state it sets would otherwise look like a user choice.
    /// </summary>
    private void OnSettingsLoaded(GraphicsCurrentSettings? current)
    {
        _suppressSelection = true;
        try
        {
            if (current is not null)
            {
                SelectContent(current.Quality, QualityButtons);
                SelectContent(current.FrameRate, FrameRateButtons);
                var style = current.Style;
                foreach (var button in StyleButtons)
                {
                    button.IsChecked = style is not null && string.Equals(button.Tag?.ToString(), style, StringComparison.OrdinalIgnoreCase);
                }

                ShadowDisableButton.IsChecked = current.ShadowEnabled == false;
                ShadowEnableButton.IsChecked = current.ShadowEnabled == true;
            }

            ShadowDisableButton.IsEnabled = true;
            ShadowEnableButton.IsEnabled = true;

            var isKoreanVersion = _viewModel.IsKoreanVersion;
            KoreanResolutionPanel.Visibility = isKoreanVersion ? Visibility.Visible : Visibility.Collapsed;
            KoreanFullHdButton.IsEnabled = isKoreanVersion;
            KoreanFullHdButton.IsChecked = isKoreanVersion;
        }
        finally
        {
            _suppressSelection = false;
        }

        RenderSummary();
    }

    /// <summary>
    /// Page-local enablement while an operation runs. The shell's sidebar
    /// refresh button is toggled from the same signal on the ViewModel.
    /// </summary>
    private void OnBusyVisualChanged(bool busy)
    {
        var canEdit = !busy && _viewModel.IsVersionLoaded;
        ConnectButton.IsEnabled = !busy;
        ApplyButton.IsEnabled = canEdit;
        ShadowDisableButton.IsEnabled = canEdit;
        ShadowEnableButton.IsEnabled = canEdit;
        KoreanFullHdButton.IsEnabled = !busy && _viewModel.IsKoreanVersion;
    }

    /// <summary>
    /// Page half of the connection paint. The shell paints the title-bar pill
    /// and the sidebar indicator from the same event, so the two halves stay in
    /// step without either reaching into the other's controls.
    /// </summary>
    private void OnConnectionStateChanged(ConnectionState state, string message)
    {
        switch (state)
        {
            case ConnectionState.Failed:
                // Nothing page-local to paint on failure: the dot, the detail
                // line and the summary keep their last state, exactly as the
                // pre-extraction failure branch did.
                break;
            case ConnectionState.AwaitingVersion:
                // Reachable only when a connect reports success without a
                // transport state. It deliberately leaves ConnectionDot
                // untouched — do not fold it into the success paint.
                SummaryAdb.Text = "Connected";
                ConnectionDetail.Text = "Select the PUBG Mobile version to load its settings.";
                break;
            case ConnectionState.TransportConnected:
            case ConnectionState.FullyConnected:
                PaintSuccessDot();
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
                RenderSummary();
                break;
            default:
                PaintDisconnectedDot();
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
                ConnectionDetail.Text = "Connect to GameLoop to load current settings.";
                RenderSummary();
                break;
        }
    }

    private void PaintSuccessDot()
    {
        ConnectionDot.Fill = GetBrush("Success") ?? Brushes.LimeGreen;
        ConnectionDot.Effect = CreateSuccessGlow();
        SummaryAdb.Text = "Connected";
    }

    private void PaintDisconnectedDot()
    {
        ConnectionDot.Fill = GetBrush("TextMuted") ?? Brushes.Gray;
        ConnectionDot.Effect = null;
    }

    /// <summary>
    /// The summary bar. Nulls and whitespace fall back to the placeholder so an
    /// unread or unrecognized profile never looks like a chosen setting.
    /// </summary>
    private void RenderSummary()
    {
        var version = PubgVersionComboBox.SelectedItem is PubgVersion selected ? selected.DisplayName : null;
        bool? shadowEnabled = ShadowEnableButton.IsChecked == true
            ? true
            : ShadowDisableButton.IsChecked == true ? false : null;

        var display = GraphicsDisplayFormatter.FormatSummary(
            version: version,
            quality: SelectedContent(QualityButtons),
            frameRate: SelectedContent(FrameRateButtons),
            style: SelectedStyleOrNull(),
            shadowEnabled: shadowEnabled,
            // The ADB cell is owned by the connection paint above, not by the
            // selection summary.
            adb: null);

        SummaryVersion.Text = display.Version;
        SummaryQuality.Text = display.Quality;
        SummaryFps.Text = display.Fps;
        SummaryStyle.Text = display.Style;
        SummaryShadow.Text = display.Shadow;
    }

    // Built once: the cards and segments never change identity, so caching the
    // arrays avoids allocating them on every access (QA §3.5).
    private ToggleButton[] StyleButtons => _styleButtons ??= new[] { ClassicButton, ColorfulButton, RealisticButton, SoftButton, MovieButton };
    private RadioButton[] QualityButtons => _qualityButtons ??= new[] { SmoothButton, BalancedButton, HdButton, HdrButton, UltraHdButton, UhdButton };
    private RadioButton[] FrameRateButtons => _frameRateButtons ??= new[] { LowButton, MediumButton, HighButton, UltraButton, ExtremeButton, Fps90Button, Fps120Button };

    private string SelectedStyle() => SelectedStyleOrNull() ?? GraphicsSelection.Defaults.Style;

    private string? SelectedStyleOrNull() =>
        StyleButtons.FirstOrDefault(button => button.IsChecked == true)?.Tag?.ToString();

    private static string? SelectedContent(IEnumerable<RadioButton> buttons) =>
        buttons.FirstOrDefault(button => button.IsChecked == true)?.Content?.ToString();

    private static void SelectContent(string? content, IEnumerable<RadioButton> buttons)
    {
        foreach (var button in buttons)
        {
            button.IsChecked = string.Equals(button.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase);
        }
    }

    private Brush? GetBrush(string key) => FindResource(key) as Brush;

    private static DropShadowEffect CreateSuccessGlow() =>
        new() { Color = Color.FromRgb(0x10, 0xB9, 0x81), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 };
}