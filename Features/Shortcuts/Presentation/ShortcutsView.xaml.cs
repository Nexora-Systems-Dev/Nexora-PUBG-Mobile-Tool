using System.Windows;
using System.Windows.Controls;
using Nexora.Configuration;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.Graphics.Domain;
using Nexora.Features.Shortcuts.Application;
using Nexora.Infrastructure.GameLoop;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;
using Nexora.Shared.Contracts;
using Nexora.UI.Presentation;

namespace Nexora.Features.Shortcuts.Presentation;

/// <summary>
/// The Shortcuts page: version picker, launcher preview card and the desktop
/// shortcut creation. Thin by design — creation and icon resolution delegate
/// to <see cref="ShortcutsViewModel"/>, the preview text comes from the
/// domain model, and the available-versions list is seeded here from the
/// catalog and refreshed by the shell when Graphics detects installs.
/// </summary>
public partial class ShortcutsView : UserControl
{
    private readonly ShortcutsViewModel _viewModel;

    public ShortcutsView()
        : this(shortcuts: null, operationBus: null)
    {
    }

    public ShortcutsView(
        IShortcutService? shortcuts = null,
        IPageOperationBus? operationBus = null)
    {
        InitializeComponent();

        _viewModel = BuildViewModel(shortcuts, operationBus);
        _viewModel.StatusChanged += (message, isError) => StatusChanged?.Invoke(message, isError);

        ShortcutComboBox.DisplayMemberPath = nameof(PubgVersion.DisplayName);
        ShortcutComboBox.ItemsSource = PubgVersionCatalog.PubgVersions
            .Select(pair => new PubgVersion(pair.Key, pair.Value))
            .ToList();
        ShortcutComboBox.SelectedIndex = 0;
        UpdatePreview();
    }

    /// <summary>
    /// The page's orchestration seam. Exposed for the shell, which cancels an
    /// in-flight creation at close and forwards statuses to the window bar.
    /// </summary>
    public ShortcutsViewModel ViewModel => _viewModel;

    /// <summary>
    /// Re-emitted for the shell, which owns the window status bar: the
    /// "Working..." line from the ViewModel and every creation result.
    /// </summary>
    public event Action<string, bool>? StatusChanged;

    /// <summary>
    /// Replaces the version list with the installs Graphics detected, falling
    /// back to the catalog when none were found. Cross-page wiring owned by
    /// the shell: a single detected install pre-selects it, several leave the
    /// choice to the user — verbatim the pre-extraction shell behavior.
    /// </summary>
    public void SetAvailableVersions(IReadOnlyList<PubgVersion> versions)
    {
        ShortcutComboBox.ItemsSource = versions.Count > 0
            ? versions
            : PubgVersionCatalog.PubgVersions.Select(pair => new PubgVersion(pair.Key, pair.Value)).ToList();
        ShortcutComboBox.SelectedIndex = versions.Count == 1 ? 0 : -1;
    }

    private static ShortcutsViewModel BuildViewModel(
        IShortcutService? shortcuts,
        IPageOperationBus? operationBus)
    {
        // XAML constructs this view with the parameterless ctor, so the view
        // takes its ViewModel from the running container when there is one
        // and only falls back to a locally built graph for the designer and
        // for direct construction (see ShellHelper.TryResolveViewModel). The
        // fallback mirrors the container's singletons: one runner, one path
        // resolver, the same asset root.
        if (shortcuts is null && operationBus is null
            && ShellHelper.TryResolveViewModel(out ShortcutsViewModel? resolved)
            && resolved is not null)
        {
            return resolved;
        }

        var runner = new ProcessRunner();
        var pathResolver = new GameLoopPathResolver(new RegistryService());
        return new ShortcutsViewModel(
            shortcuts ?? new ShortcutService(
                runner,
                pathResolver,
                Path.Combine(AppContext.BaseDirectory, new EmulatorOptions().Assets.DirectoryName)),
            operationBus ?? new PageOperationBus());
    }

    private async void CreateShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (ShortcutComboBox.SelectedItem is not PubgVersion version) return;
        await RunShortcutToolAsync(CreateShortcutButton, ct => Task.Run(() => _viewModel.Shortcuts.CreateShortcut(version.DisplayName, version.PackageName), ct));
    }

    private void ShortcutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        var preview = ShortcutsViewModel.GetPreview(ShortcutComboBox.SelectedItem as PubgVersion);

        ShortcutDisplayNameText.Text = preview.DisplayName;
        ShortcutPackageText.Text = preview.PackageName;
        ShortcutDestinationText.Text = preview.Destination;
        CreateShortcutButton.IsEnabled = preview.CanCreate;

        ShortcutIcon.Source = ShortcutComboBox.SelectedItem is PubgVersion version
            ? _viewModel.Shortcuts.GetIcon(version.PackageName)
            : null;
    }

    /// <summary>
    /// The page half of the creation the shell's <c>RunToolAsync</c> used to
    /// own for this page: dim the button, run through the ViewModel's
    /// bus-guarded core, and forward the result line to the shell. The page
    /// has no result label of its own, so the outcome travels only through
    /// <see cref="StatusChanged"/> — exactly as the pre-extraction helper did
    /// with its null label.
    /// </summary>
    private async Task RunShortcutToolAsync(Button button, Func<CancellationToken, Task<OperationResult>> action)
    {
        button.IsEnabled = false;
        OperationResult result;
        try
        {
            result = await _viewModel.ExecuteAsync(action);
        }
        finally
        {
            button.IsEnabled = true;
        }

        StatusChanged?.Invoke(result.Message, !result.Success);
    }
}
