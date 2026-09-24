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
            if (update.Available) await PromptAndDownloadAsync(update);
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
    /// </summary>
    private async Task PromptAndDownloadAsync(UpdateInfo update)
    {
        var message = $"Nexora update {update.LatestVersion} is available.\n\n{update.ChangeLog}";
        if (MessageBox.Show(message, "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;

        _setStatus("Downloading update...", false);
        var result = await _updates.DownloadAndLaunchAsync(update);
        _setStatus(result.Message, !result.Success);
        // UpdateService has already verified that the new elevated process
        // started successfully. Closing this instance lets the new version
        // take over cleanly.
        if (result.Success) _close();
    }
}
