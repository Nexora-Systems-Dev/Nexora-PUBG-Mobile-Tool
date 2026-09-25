using System.Windows;
using Nexora.Features.Updates.Application;
using Nexora.Features.Updates.Domain;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Updates.Presentation;

/// <summary>
/// The startup update handoff: check for a release, prompt, download and
/// relaunch, then close this instance. Runs beside the Optimizer profile
/// refresh at startup; each owns its own error handling and shutdown-guarded
/// UI writes, so a failure here never blocks or suppresses the refresh
/// (QA F-006). Shell-owned app lifecycle, not page logic: it ends by closing
/// the window, which is why it lives behind the shell rather than in a page.
/// </summary>
public sealed class UpdateHandoff
{
    private readonly IUpdateService _updates;
    private readonly Func<bool> _isShutdown;
    private readonly Action<string, bool> _setStatus;
    private readonly Action _close;

    public UpdateHandoff(
        IUpdateService updates,
        Func<bool> isShutdown,
        Action<string, bool> setStatus,
        Action close)
    {
        _updates = updates;
        _isShutdown = isShutdown;
        _setStatus = setStatus;
        _close = close;
    }

    /// <summary>
    /// Runs the check-and-prompt flow once. Every branch below is verbatim
    /// the pre-extraction shell flow: the slow-network shutdown guards, the
    /// prompt text, the downloading line, and the close-on-success handoff.
    /// Cancellation (the shell's close-linked token) settles as a reported
    /// cancel, never a throw — and stays silent when the window is already
    /// going away.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var update = await _updates.CheckAsync(cancellationToken);
            // The check can now outlive the window (a slow network plus a user
            // close, or the refresh settling first); never show UI on a
            // dispatcher that is already torn down.
            if (_isShutdown()) return;
            if (update.Available) await PromptAndDownloadAsync(update, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!_isShutdown()) _setStatus("Update check canceled.", true);
        }
        catch (Exception ex)
        {
            _setStatus($"Update check failed: {ex.Message}", true);
        }
    }

    /// <summary>
    /// Prompts for the release, downloads it, and closes this instance on success
    /// so the elevated handoff can replace the executable. Stays inside
    /// <see cref="RunAsync"/>'s try so a download failure is reported, not thrown.
    /// The download honors the shell's close-linked token; the close itself
    /// goes through <see cref="CompleteHandoff"/>.
    /// </summary>
    private async Task PromptAndDownloadAsync(UpdateInfo update, CancellationToken cancellationToken)
    {
        var message = $"Nexora update {update.LatestVersion} is available.\n\n{update.ChangeLog}";
        if (MessageBox.Show(message, "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;

        _setStatus("Downloading update...", false);
        var result = await _updates.DownloadAndLaunchAsync(update, cancellationToken);
        CompleteHandoff(result);
    }

    /// <summary>
    /// Reports the download outcome and, on success, closes this instance so
    /// the elevated handoff can replace the executable (UpdateService has
    /// already verified the new process started). The close is CloseOnce-safe:
    /// a window already going away is never closed twice, and a racing close
    /// that beats us to a dead window throws InvalidOperationException — which
    /// must stay silent instead of falling into RunAsync's catch-all and
    /// misreporting as a check failure.
    /// </summary>
    internal void CompleteHandoff(OperationResult result)
    {
        // Shutdown first: the status sink paints an unguarded visual-tree
        // cell, so a handoff settling after close stays fully silent — the
        // RunAsync post-check silence, applied to the success path too.
        if (_isShutdown()) return;
        _setStatus(result.Message, !result.Success);
        if (!result.Success) return;
        try
        {
            _close();
        }
        catch (InvalidOperationException)
        {
            // A racing close won and the window is already dead; teardown done.
        }
    }
}
