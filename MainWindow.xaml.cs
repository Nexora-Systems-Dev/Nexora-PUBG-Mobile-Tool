using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Nexora.Bootstrap;
using Nexora.Configuration;
using Nexora.Features.Graphics.Presentation;
using Nexora.Features.Performance.Application;
using Nexora.Features.Updates.Application;
using Nexora.Features.Updates.Presentation;
using Nexora.UI.Behaviors;
using Nexora.UI.Layout;
using Nexora.UI.Navigation;
using Nexora.UI.Presentation;

namespace Nexora;

public partial class MainWindow : Window
{
    private readonly IGameLoopPerformanceEngine _performanceEngine;
    private readonly GameLoopOptions _gameLoopOptions;
    private readonly UpdateHandoff _updateHandoff;

    private readonly GraphicsViewModel _graphicsViewModel;
    private readonly ShellNavigator _navigator;
    private readonly ShellConnectionPresenter _connection;

    private IDisposable? _chromeHook;

    public MainWindow()
        : this(null, null, null)
    {
    }

    public MainWindow(
        IGameLoopPerformanceEngine? performanceEngine = null,
        IUpdateService? updates = null,
        GameLoopOptions? gameLoopOptions = null)
    {
        InitializeComponent();
        ResourceFreezer.FreezeAll(GraphicsView, OptimizerView, TuningView, NetworkView, ShortcutsView, AboutView);
        _gameLoopOptions = gameLoopOptions ?? new GameLoopOptions();
        var fallback = DesignerFallback.Create();

        _performanceEngine = performanceEngine ?? fallback.PerformanceEngine;
        _updateHandoff = new UpdateHandoff(
            updates ?? fallback.Updates,
            () => Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished,
            SetStatus,
            Close);

        // The Graphics page owns its connection orchestration; the shell keeps
        // only the surfaces that live outside it — the title-bar pill, the
        // sidebar indicator, the sidebar connect button and this status line.
        _graphicsViewModel = GraphicsView.ViewModel;
        _graphicsViewModel.BusyVisualChanged += busy => RefreshConnectionButton.IsEnabled = !busy;
        _graphicsViewModel.PropertyChanged += OnGraphicsVersionsChanged;

        // Shell half of the connection paint, behind the presenter: the page
        // paints its own surfaces from the same event.
        _connection = new ShellConnectionPresenter(
            this,
            TopConnectionDot,
            TopConnectionPill,
            TopConnectionText,
            SidebarConnectionDot,
            SidebarConnectionText,
            SidebarAdbText,
            SetStatus);
        _graphicsViewModel.ConnectionStateChanged += _connection.Show;

        // The Tuning and Network pages own their flows; the shell keeps only
        // the window status bar, which the pages' statuses forward to.
        TuningView.ViewModel.StatusChanged += (message, isError) => SetStatus(message, isError);
        NetworkView.ViewModel.StatusChanged += (message, isError) => SetStatus(message, isError);

        // The Optimizer page owns its profile and tool flows; the shell keeps
        // only the window status bar and the startup/close lifecycle around it.
        OptimizerView.StatusChanged += (message, isError) => SetStatus(message, isError);

        // The Shortcuts page owns its version seeding, preview and creation;
        // the shell keeps only the window status bar and the detected-version
        // feed from Graphics below.
        ShortcutsView.StatusChanged += (message, isError) => SetStatus(message, isError);

        // The About page owns its own content, including the version pill —
        // the shell keeps no About state.
        _navigator = new ShellNavigator(new Dictionary<string, Action<bool>>
        {
            [NavigationItem.Graphics.Key] = show => GraphicsView.Visibility = show ? Visibility.Visible : Visibility.Collapsed,
            [NavigationItem.Optimizer.Key] = show => OptimizerView.Visibility = show ? Visibility.Visible : Visibility.Collapsed,
            [NavigationItem.Tuning.Key] = show => TuningView.Visibility = show ? Visibility.Visible : Visibility.Collapsed,
            [NavigationItem.Network.Key] = show => NetworkView.Visibility = show ? Visibility.Visible : Visibility.Collapsed,
            [NavigationItem.Shortcuts.Key] = show => ShortcutsView.Visibility = show ? Visibility.Visible : Visibility.Collapsed,
            [NavigationItem.About.Key] = show => AboutView.Visibility = show ? Visibility.Visible : Visibility.Collapsed,
        });
        SetStatus("Ready to connect to GameLoop.");
    }

    /// <summary>
    /// The sidebar's connect button. It drives the Graphics page's connection
    /// command so the title-bar pill, the sidebar indicator and the page's own
    /// connect button can never disagree about whether a transport is being
    /// brought up or torn down.
    /// </summary>
    private async void RefreshConnectionButton_Click(object sender, RoutedEventArgs e) =>
        await _graphicsViewModel.ConnectAsync();

    /// <summary>
    /// The versions the connect found on the device. Feeding the Shortcuts
    /// page from them is cross-page wiring, so it belongs to the shell rather
    /// than to the Graphics feature — the page itself owns the catalog
    /// fallback and the selection behavior behind this call.
    /// </summary>
    private void OnGraphicsVersionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GraphicsViewModel.InstalledVersions)) return;

        ShortcutsView.SetAvailableVersions(_graphicsViewModel.InstalledVersions);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // The GitHub update check (up to the 12 s HttpTimeout) and the Optimizer
        // profile refresh are independent, so they run concurrently: an offline
        // or slow network can no longer leave the panel empty for the whole
        // timeout window. Each task owns its own error handling and
        // shutdown-guarded UI writes, so a failure in one never blocks or
        // suppresses the other (QA F-006).
        await Task.WhenAll(_updateHandoff.RunAsync(), OptimizerView.RefreshProfileAsync());
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

        _graphicsViewModel.Cancel();
        ShortcutsView.ViewModel.Cancel();
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

    /// <summary>
    /// Pure navigation command: the router owns the page-visibility map, so
    /// the shell never names a page twice. Only Tuning refreshes on arrival.
    /// </summary>
    private async void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton button || button.Tag is not string page) return;
        if (_navigator.Navigate(page)?.RefreshOnNavigate is true) await TuningView.RefreshAsync();
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

    /// <summary>
    /// The status bar surface. It lives visually inside the Graphics page but
    /// the shell still owns what it writes here — the update handoff, the
    /// page forwards and this connection paint — so the bar keeps exactly one
    /// writer per message even though the control moved with the page.
    /// </summary>
    private void SetStatus(string message, bool isError = false) =>
        GraphicsView.SetStatus(message, isError);
}
