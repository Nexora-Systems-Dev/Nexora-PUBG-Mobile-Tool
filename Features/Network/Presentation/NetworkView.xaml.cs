using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Features.Network.Application;
using Nexora.Features.Network.Domain;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Infrastructure.Files;
using Nexora.Infrastructure.GameLoop;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;
using Nexora.UI.Presentation;

namespace Nexora.Features.Network.Presentation;

/// <summary>
/// The Network page: DNS provider probing/applying and iPad display profiles.
/// Thin by design — every action delegates to <see cref="NetworkViewModel"/>
/// and every display state is painted from the returned outcome below. No
/// adapter, registry, or file calls live here; even the iPad running-state
/// guard is only painted, never decided.
/// </summary>
public partial class NetworkView : UserControl
{
    private readonly NetworkViewModel _viewModel;

    public NetworkView()
        : this(networkTools: null, ipadLayout: null, operationBus: null)
    {
    }

    public NetworkView(
        INetworkToolsService? networkTools = null,
        IIpadLayoutService? ipadLayout = null,
        IPageOperationBus? operationBus = null)
    {
        InitializeComponent();

        _viewModel = BuildViewModel(networkTools, ipadLayout, operationBus);
        _viewModel.StatusChanged += OnStatusChanged;

        IpadComboBox.ItemsSource = IpadPresetCatalog.Presets.Select(preset => preset.DisplayName).ToList();
        IpadComboBox.SelectedIndex = -1;
        UpdateIpadPresetDetails();

        DnsComboBox.ItemsSource = DnsCatalog.Labels;
        DnsComboBox.SelectedIndex = 0;
    }

    /// <summary>
    /// The page's orchestration seam. Exposed for the shell, which forwards
    /// the page's statuses to the window status bar.
    /// </summary>
    public NetworkViewModel ViewModel => _viewModel;

    private static NetworkViewModel BuildViewModel(
        INetworkToolsService? networkTools,
        IIpadLayoutService? ipadLayout,
        IPageOperationBus? operationBus)
    {
        // XAML constructs this view with the parameterless ctor, so the view
        // takes its ViewModel from the running container when there is one
        // and only falls back to a locally built graph for the designer and
        // for direct construction. The fallback mirrors the container's
        // singletons: one registry, one process service, one file system.
        if (networkTools is null && ipadLayout is null && operationBus is null
            && System.Windows.Application.Current is App && App.Services is IServiceProvider services)
        {
            if (services.GetService<NetworkViewModel>() is { } resolved) return resolved;
        }

        var registry = new RegistryService();
        var runner = new ProcessRunner();
        var pathResolver = new GameLoopPathResolver(registry);
        var processSvc = new GameLoopProcessService(runner, pathResolver);

        return new NetworkViewModel(
            networkTools ?? new NetworkToolsService(runner),
            ipadLayout ?? new IpadLayoutService(registry, new PhysicalFileSystem(), processService: processSvc),
            operationBus ?? new PageOperationBus());
    }

    /// <summary>The shell's window status bar. The telemetry cell below is page-local.</summary>
    private void OnStatusChanged(string message, bool isError) =>
        StatusChanged?.Invoke(message, isError);

    /// <summary>
    /// Re-emitted for the shell, which owns the window status bar. The page's
    /// own cells are painted by each handler from its returned outcome.
    /// </summary>
    public event Action<string, bool>? StatusChanged;

    private async void DnsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DnsComboBox.SelectedItem is not string label) return;
        if (!DnsCatalog.TryGet(label, out var entry) || entry is null) return;
        DnsStatusText.Text = $"{entry.ShortName} • Testing response...";
        // Shutdown-aware: a closed window reads as a moved selection, so the
        // stale probe is discarded before it can touch a dead control.
        var probe = await _viewModel.ProbeDnsAsync(label, () =>
            Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished
                ? null
                : DnsComboBox.SelectedItem as string);

        // This continuation can outlive a closed window: the shutdown guard
        // is still required even though no Dispatcher.Invoke is needed (QA F-005).
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

        // A null probe means the selection moved on while the ping was in
        // flight (or the window closed): the stale answer stays unpainted.
        if (probe is null) return;

        DnsStatusText.Text = probe.PingMs is null
            ? $"{probe.Entry.ShortName} • No response from DNS server"
            : $"{probe.Entry.ShortName} • Ping: {probe.PingMs}ms • Ready to apply";
    }

    private async void ChangeDnsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        if (DnsComboBox.SelectedItem is not string label) return;
        ChangeDnsButton.IsEnabled = false;
        try
        {
            var result = await _viewModel.ApplyDnsAsync(label);
            if (result is null) return;
            if (!DnsCatalog.TryGet(label, out var entry) || entry is null) return;
            // Mutating paths outlive the probe's guards: a close landing
            // mid-apply must not paint (or re-enable) a dead tree (QA F-005).
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            DnsStatusText.Text = result.Success
                ? $"{entry.ShortName} • Applied: {entry.Primary} / {entry.Secondary}"
                : result.Message;
        }
        finally
        {
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                ChangeDnsButton.IsEnabled = true;
        }
    }

    private async void ChangeIpadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        var preset = GetSelectedIpadPreset();
        if (preset is null) return;
        ChangeIpadButton.IsEnabled = false;
        try
        {
            var result = await _viewModel.ApplyIpadAsync(preset);
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            IpadApplyStatusText.Text = result.Success
                ? $"Applied: {preset.Label} • {preset.Width} × {preset.Height}. Restart GameLoop to load it."
                : result.Message;
            IpadApplyStatusText.Foreground = GetBrush(result.Success ? "Accent" : "Danger");
        }
        finally
        {
            // Re-enabled only when a preset is selected, per the details paint —
            // and never on a dead tree.
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                UpdateIpadPresetDetails();
        }
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
        NetworkViewModel.FindPreset(IpadComboBox.SelectedItem as string);

    private async void ResetIpadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        ResetIpadButton.IsEnabled = false;
        try
        {
            await _viewModel.ResetIpadAsync();
        }
        finally
        {
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                ResetIpadButton.IsEnabled = true;
        }
    }

    private Brush GetBrush(string key) => ResourceBrushLookup.Get(this, key);
}
