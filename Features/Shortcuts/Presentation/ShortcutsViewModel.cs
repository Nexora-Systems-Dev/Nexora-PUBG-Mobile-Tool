using Nexora.Features.GameLoop.Domain;
using Nexora.Features.Shortcuts.Application;
using Nexora.Features.Shortcuts.Domain;
using Nexora.Shared.Contracts;
using Nexora.UI.Presentation;

namespace Nexora.Features.Shortcuts.Presentation;

/// <summary>
/// Owns the Shortcuts page's execution flow: the preview model for whatever
/// version is selected and the long-running desktop-shortcut creation.
/// </summary>
/// <remarks>
/// The page is presentation-thin by design: creation and icon resolution both
/// delegate to <see cref="IShortcutService"/>, the preview text lives in the
/// testable <see cref="ShortcutPreview"/> domain model, and every display
/// state comes back through the members below rather than reaching into the
/// visual tree. The cross-page version seeding (catalog default vs.
/// Graphics-detected installs) stays in the shell, which calls
/// <see cref="ShortcutsView.SetAvailableVersions"/>.
/// </remarks>
public sealed class ShortcutsViewModel
{
    private readonly IShortcutService _shortcuts;
    private readonly IPageOperationBus _operationBus;
    private CancellationTokenSource? _toolCancellation;

    public ShortcutsViewModel(IShortcutService shortcuts, IPageOperationBus operationBus)
    {
        _shortcuts = shortcuts;
        _operationBus = operationBus;
    }

    /// <summary>
    /// A status line for the shell's window status bar. The page has no result
    /// label of its own — creation reports only here — so the view forwards
    /// every outcome through this event.
    /// </summary>
    public event Action<string, bool>? StatusChanged;

    /// <summary>The shared guard, so the page's handler can refuse while another operation runs.</summary>
    public bool IsBusy => _operationBus.IsBusy;

    /// <summary>The shortcut service behind the page. Exposed so the view's
    /// handlers can build the creation call and resolve preview icons without
    /// the ViewModel duplicating the service's vocabulary.</summary>
    public IShortcutService Shortcuts => _shortcuts;

    /// <summary>
    /// The preview card for a selected version, or the empty "choose a
    /// version" card when nothing is selected. Pure and synchronous — a thin
    /// pass over the domain model, kept on the ViewModel so tests pin the
    /// contract in one place.
    /// </summary>
    public static ShortcutPreview GetPreview(PubgVersion? version) =>
        ShortcutPreview.FromVersion(version);

    /// <summary>
    /// The window-wide "one long operation at a time" core that the shell's
    /// <c>RunToolAsync</c> used to own for this page: acquire the bus, run the
    /// operation on a cancellable token, translate cancellation and failure
    /// into results, and always release. Button enablement stays in the view;
    /// the shell status line travels through <see cref="StatusChanged"/>.
    /// </summary>
    public async Task<OperationResult> ExecuteAsync(Func<CancellationToken, Task<OperationResult>> action)
    {
        if (!_operationBus.TryAcquire()) return OperationResult.Fail("Another operation is already running.");
        StatusChanged?.Invoke("Working...", false);

        OperationResult result;
        _toolCancellation = new CancellationTokenSource();
        try
        {
            result = await action(_toolCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            result = OperationResult.Skip("Operation canceled.");
        }
        catch (Exception ex)
        {
            result = OperationResult.Fail(ex.Message);
        }
        finally
        {
            _operationBus.Release();
            CancelAndDisposeTool();
        }

        return result;
    }

    /// <summary>
    /// Cancels whatever creation is in flight. Called at window close so an
    /// outstanding shortcut write can never outlive the shell.
    /// </summary>
    public void Cancel() => CancelAndDisposeTool();

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
}
