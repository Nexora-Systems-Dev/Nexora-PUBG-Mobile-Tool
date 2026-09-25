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
    private readonly CancellationTokenSource _loadedCts = new();

    /// <summary>
    /// The page on screen. Starts on Graphics — the XAML default visible page —
    /// so the first navigate-away cancels nothing.
    /// </summary>
    private string _currentPage = NavigationItem.Graphics.Key;

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
        // First paint already uses tier values: the XAML-declared 1440 width
        // selects the tier before first measure, so SizeChanged on show only
        // re-applies identical values (dependency-property equal-sets are
        // layout no-ops). If the OS shows the window at a different size,
        // that single SizeChanged pass is the one correction.
        ApplyResponsiveTier(Width);
        ResourceFreezer.FreezeAll(GraphicsView, OptimizerView, TuningView, NetworkView, ShortcutsView, AboutView);
        _gameLoopOptions = gameLoopOptions ?? new GameLoopOptions();
        var fallback = DesignerFallback.Create();

        _performanceEngine = performanceEngine ?? fallback.PerformanceEngine;
        _updateHandoff = new UpdateHandoff(
            updates ?? fallback.Updates,
            () => Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished,
            SetStatus,
            CloseOnce);

        // The Graphics page owns its connection orchestration; the shell keeps
        // only the surfaces that live outside it — the title-bar pill, the
        // sidebar indicator, the sidebar connect button and this status line.
        _graphicsViewModel = GraphicsView.ViewModel;
        _graphicsViewModel.BusyVisualChanged += busy => RefreshConnectionButton.IsEnabled = !busy;
        _graphicsViewModel.PropertyChanged += OnGraphicsVersionsChanged;

        // Shell half of the connection paint, behind the presenter: the page
        // paints its own surfaces from the same event.
        _connection = CreateConnectionPresenter();
        _graphicsViewModel.ConnectionStateChanged += _connection.Show;

        // The Tuning and Network pages own their flows; the shell keeps only
        // the window status bar, which the pages' statuses forward to.
        TuningView.ViewModel.StatusChanged += SetStatus;
        NetworkView.ViewModel.StatusChanged += SetStatus;

        // The Optimizer page owns its profile and tool flows; the shell keeps
        // only the window status bar and the startup/close lifecycle around it.
        OptimizerView.StatusChanged += SetStatus;

        // The Shortcuts page owns its version seeding, preview and creation;
        // the shell keeps only the window status bar and the detected-version
        // feed from Graphics below.
        ShortcutsView.StatusChanged += SetStatus;

        // The About page owns its own content, including the version pill —
        // the shell keeps no About state.
        _navigator = new ShellNavigator(BuildShowMap(), BuildRefreshMap(() => TuningView.RefreshAsync()));
        SetStatus("Ready to connect to GameLoop.");
    }

    /// <summary>
    /// The page-visibility table: one show/hide callback per page key in
    /// sidebar order. Sits beside <see cref="BuildRefreshMap(Func{Task})"/> so
    /// the navigator pairs visibility with refresh-on-arrive, and the shell
    /// never names a page outside these two tables.
    /// </summary>
    private IReadOnlyDictionary<string, Action<bool>> BuildShowMap() =>
        new Dictionary<string, Action<bool>>
        {
            [NavigationItem.Graphics.Key] = show => GraphicsView.Visibility = ToVisibility(show),
            [NavigationItem.Optimizer.Key] = show => OptimizerView.Visibility = ToVisibility(show),
            [NavigationItem.Tuning.Key] = show => TuningView.Visibility = ToVisibility(show),
            [NavigationItem.Network.Key] = show => NetworkView.Visibility = ToVisibility(show),
            [NavigationItem.Shortcuts.Key] = show => ShortcutsView.Visibility = ToVisibility(show),
            [NavigationItem.About.Key] = show => AboutView.Visibility = ToVisibility(show),
        };

    /// <summary>Shell half of the connection paint, behind the presenter: the page
    /// paints its own surfaces from the same event.</summary>
    private ShellConnectionPresenter CreateConnectionPresenter() =>
        new(this, TopConnectionDot, TopConnectionPill, TopConnectionText,
            SidebarConnectionDot, SidebarConnectionText, SidebarAdbText, SetStatus);

    private static Visibility ToVisibility(bool show) => show ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// The single home of the refresh-on-arrive registrations: one entry per
    /// page flagged <see cref="NavigationItem.RefreshOnNavigate"/>. The delegate
    /// arrives as a parameter rather than being captured from a page control, so
    /// the wiring is testable without a Window instance —
    /// <see cref="Nexora.Tests.UI.ShellNavigatorTests"/> covers it off the STA
    /// thread, and a flagged page with no entry here fails that test in CI
    /// instead of silently skipping its refresh.
    /// </summary>
    internal static IReadOnlyDictionary<string, Func<Task>> BuildRefreshMap(Func<Task> tuningRefresh) =>
        new Dictionary<string, Func<Task>> { [NavigationItem.Tuning.Key] = tuningRefresh };

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
        // suppresses the other (QA F-006). Both share the close-linked token,
        // so a user close settles them as failed results instead of letting
        // them outlive the window — neither ever throws out of this handler.
        await Task.WhenAll(_updateHandoff.RunAsync(_loadedCts.Token), OptimizerView.RefreshProfileAsync(_loadedCts.Token));
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
        TuningView.ViewModel.Cancel();
        NetworkView.ViewModel.Cancel();
        OptimizerView.ViewModel.Cancel();
        ShortcutsView.ViewModel.Cancel();
        _loadedCts.Cancel();
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
            // The last step of every close path, so teardown order reads:
            // cancel in-flight work, restore the session, then close once.
            CloseOnce();
        }
    }

    /// <summary>
    /// Close exactly once, and only while the dispatcher is still alive.
    /// The e.Cancel guard above normally holds the window open for the
    /// whole restore, but a close request can still land in the narrow
    /// window between setting _closeCompleted and calling Close(); on an
    /// already-closing window that throws InvalidOperationException, which
    /// in an async void method would terminate the process (QA F-001).
    /// </summary>
    private void CloseOnce()
    {
        if (_closeCompleted) return;
        _closeCompleted = true;
        try
        {
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished) Close();
        }
        catch (InvalidOperationException)
        {
            // Window already closed via the racing close request; teardown done.
        }
    }

    /// <summary>
    /// Pure navigation command: the router owns the page-visibility map, so
    /// the shell never names a page twice. A page opts into refresh-on-arrival
    /// by setting <see cref="NavigationItem.RefreshOnNavigate"/> and adding an
    /// entry in <see cref="BuildRefreshMap(Func{Task})"/>; a flagged page with
    /// no entry is a no-op rather than an error, so adding a refresh target is
    /// one map entry plus one flag — no shell logic to edit.
    /// </summary>
    private async void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton button || button.Tag is not string page) return;
        // Navigate-away mirror of the close-path cancels: an in-flight connect
        // poll (or tool run) on the page being left must settle before the new
        // page paints, or its late completion repaints the wrong page through
        // the shared status/connection events. Same page set as Window_Closing.
        if (!string.Equals(page, _currentPage, StringComparison.Ordinal))
        {
            CancelPageWork(_currentPage);
            _currentPage = page;
        }

        var item = _navigator.Navigate(page);
        if (item is not { RefreshOnNavigate: true }) return;
        if (_navigator.TryGetRefresh(item.Key, out var refresh) && refresh is not null)
        {
            await refresh();
        }
    }

    /// <summary>
    /// Cancels the leavable work of one page. Mirrors <see cref="Window_Closing"/>
    /// exactly: Graphics (orphaned connect poll), Tuning, Network and
    /// Optimizer (tool runs plus the Optimizer profile refresh), and Shortcuts
    /// (tool runs). About owns no cancellable work.
    /// </summary>
    private void CancelPageWork(string page)
    {
        if (string.Equals(page, NavigationItem.Graphics.Key, StringComparison.Ordinal))
        {
            _graphicsViewModel.Cancel();
        }
        else if (string.Equals(page, NavigationItem.Tuning.Key, StringComparison.Ordinal))
        {
            TuningView.ViewModel.Cancel();
        }
        else if (string.Equals(page, NavigationItem.Network.Key, StringComparison.Ordinal))
        {
            NetworkView.ViewModel.Cancel();
        }
        else if (string.Equals(page, NavigationItem.Optimizer.Key, StringComparison.Ordinal))
        {
            OptimizerView.ViewModel.Cancel();
        }
        else if (string.Equals(page, NavigationItem.Shortcuts.Key, StringComparison.Ordinal))
        {
            ShortcutsView.ViewModel.Cancel();
        }
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

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveTier(e.NewSize.Width);

    /// <summary>
    /// Applies one responsive tier — sidebar width plus all six page margins —
    /// from the pure <see cref="ResponsiveLayoutManager"/> selectors, adding no
    /// logic of its own. Called once in the ctor (pre-first-measure) and on
    /// every <see cref="Window_SizeChanged"/> after.
    /// </summary>
    private void ApplyResponsiveTier(double windowWidth)
    {
        var sidebarWidth = ResponsiveLayoutManager.GetSidebarWidth(windowWidth);
        SidebarColumn.Width = new GridLength(sidebarWidth);
        TitleBrandColumn.Width = new GridLength(sidebarWidth);

        var pageMargin = ResponsiveLayoutManager.GetPageMargin(windowWidth);
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
