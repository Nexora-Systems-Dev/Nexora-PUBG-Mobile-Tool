using Nexora.Features.Network.Application;
using Nexora.Features.Network.Domain;
using Nexora.Shared.Contracts;
using Nexora.UI.Presentation;

namespace Nexora.Features.Network.Presentation;

/// <summary>
/// A DNS probe outcome: the catalog entry that was tested and its round-trip
/// in milliseconds, or null when the server never answered.
/// </summary>
public sealed record DnsProbeResult(DnsEntry Entry, int? PingMs);

/// <summary>
/// Owns the Network page's DNS and iPad flows: probing/applying a DNS
/// provider and applying/resetting an iPad display profile.
/// </summary>
/// <remarks>
/// The page's controls stay in the view; this is the seam they delegate to.
/// The iPad GameLoop running-state guard, backup, and restore live entirely
/// in <see cref="IIpadLayoutService"/> — this layer only surfaces their
/// outcomes verbatim and never decides them.
/// </remarks>
public sealed class NetworkViewModel
{
    private readonly INetworkToolsService _networkTools;
    private readonly IIpadLayoutService _ipadLayout;
    private readonly IPageOperationBus _operationBus;

    /// <summary>
    /// The in-flight probe's cancellation: each new selection supersedes the
    /// previous ping (which honors the token) instead of stacking behind it.
    /// A superseded probe settles as a discard, never a throw.
    /// </summary>
    private CancellationTokenSource? _probeCancellation;

    /// <summary>
    /// The in-flight mutating operation's cancellation (Tuning's
    /// _toolCancellation template): the shell's Cancel() pre-empts it at
    /// close/navigate-away. The underlying PowerShell/registry/file calls
    /// cannot pre-empt mid-call, so cancellation is cooperative — a close
    /// landing before the call starts settles Failed without touching the
    /// tree, while one landing mid-call lets the call finish and the view's
    /// shutdown guards drop the paint.
    /// </summary>
    private CancellationTokenSource? _toolCancellation;

    public NetworkViewModel(
        INetworkToolsService networkTools,
        IIpadLayoutService ipadLayout,
        IPageOperationBus operationBus)
    {
        _networkTools = networkTools;
        _ipadLayout = ipadLayout;
        _operationBus = operationBus;
    }

    /// <summary>
    /// A status line for the shell's window status bar. DNS probes deliberately
    /// stay page-local (telemetry cell only); every mutating operation reports
    /// here so the bar keeps one writer per message.
    /// </summary>
    public event Action<string, bool>? StatusChanged;

    /// <summary>The shared guard, so the page's handlers can refuse while another operation runs.</summary>
    public bool IsBusy => _operationBus.IsBusy;

    /// <summary>The catalog lookup behind the iPad combobox. Presentation truth only.</summary>
    public static IpadResolutionPreset? FindPreset(string? displayName) =>
        IpadPresetCatalog.FindByDisplayName(displayName);

    /// <summary>
    /// Pings a DNS provider and returns the outcome, or null when the selection
    /// moved on while the ping was in flight — the stale result is discarded
    /// rather than painted over the newly selected provider. Probes are reads,
    /// so they bypass the operation bus exactly as before.
    /// </summary>
    public async Task<DnsProbeResult?> ProbeDnsAsync(
        string label,
        Func<string?> getSelectedLabel,
        CancellationToken cancellationToken = default)
    {
        if (!DnsCatalog.TryGet(label, out var entry) || entry is null) return null;

        // Cancel only; the superseded probe disposes its own CTS in its finally.
        // Eagerly disposing here would leave a disposed source in the hands of a
        // still-running probe (audit PRP-018) — benign for Register on this runtime
        // by experiment, but a trap for any future WaitHandle use, so cancel-only
        // is the shape.
        _probeCancellation?.Cancel();
        var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _probeCancellation = probeCancellation;
        try
        {
            var ping = await _networkTools.PingDnsAsync(entry.Primary, probeCancellation.Token);

            // The selection may have changed (or the page closed) while the ping
            // was in flight; a stale answer must never overwrite the new row.
            if (!string.Equals(getSelectedLabel(), label, StringComparison.OrdinalIgnoreCase)) return null;

            return new DnsProbeResult(entry, ping);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection (or the caller's token): discard
            // exactly like a stale answer so the view's await never throws.
            return null;
        }
        finally
        {
            if (_probeCancellation == probeCancellation) _probeCancellation = null;
            probeCancellation.Dispose();
        }
    }

    /// <summary>
    /// Routes the active adapters through a DNS provider. Runs off-thread;
    /// the caller paints the telemetry cell from the returned result.
    /// </summary>
    public async Task<OperationResult?> ApplyDnsAsync(string label, CancellationToken cancellationToken = default)
    {
        if (!DnsCatalog.TryGet(label, out var entry) || entry is null) return null;

        return await RunOperationAsync(() => _networkTools.ChangeDns(entry.Primary, entry.Secondary), cancellationToken);
    }

    /// <summary>
    /// Applies an iPad display profile. The service refuses while GameLoop
    /// runs and owns the backup/restore; that refusal is rendered verbatim.
    /// </summary>
    public Task<OperationResult> ApplyIpadAsync(IpadResolutionPreset preset, CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => _ipadLayout.SetIpadResolution(preset.Width, preset.Height), cancellationToken);

    /// <summary>Restores the pre-iPad display state from the service's backup.</summary>
    public Task<OperationResult> ResetIpadAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync(_ipadLayout.ResetIpadResolution, cancellationToken);

    /// <summary>
    /// Cancels whatever mutating operation is in flight. Called at window
    /// close and navigate-away so an outstanding DNS change or iPad
    /// apply/reset can never outlive the shell.
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

    /// <summary>
    /// Runs one mutating operation off-thread under the shared bus: its result
    /// is surfaced verbatim, cancellation becomes a Skip, any other failure is
    /// reported rather than swallowed, and the bus is always released so a
    /// throw can never leave the page permanently disabled. A cancel that
    /// lands before the call starts settles Failed without invoking the
    /// operation at all, so close-during-apply never begins new system
    /// writes on a dying window.
    /// </summary>
    private async Task<OperationResult> RunOperationAsync(Func<OperationResult> operation, CancellationToken callerToken)
    {
        if (!_operationBus.TryAcquire()) return OperationResult.Fail("Another operation is already running.");

        _toolCancellation = new CancellationTokenSource();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_toolCancellation.Token, callerToken);
            if (linked.Token.IsCancellationRequested) return OperationResult.Fail("Operation canceled.");

            var result = await Task.Run(operation, linked.Token);
            StatusChanged?.Invoke(result.Message, !result.Success);
            return result;
        }
        catch (OperationCanceledException)
        {
            var canceled = OperationResult.Skip("Operation canceled.");
            StatusChanged?.Invoke(canceled.Message, false);
            return canceled;
        }
        catch (Exception ex)
        {
            var failed = OperationResult.Fail(ex.Message);
            StatusChanged?.Invoke(failed.Message, true);
            return failed;
        }
        finally
        {
            _operationBus.Release();
            CancelAndDisposeTool();
        }
    }
}
