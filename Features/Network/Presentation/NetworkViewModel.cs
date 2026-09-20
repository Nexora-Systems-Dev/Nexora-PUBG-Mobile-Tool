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

        var ping = await _networkTools.PingDnsAsync(entry.Primary, cancellationToken);

        // The selection may have changed (or the page closed) while the ping
        // was in flight; a stale answer must never overwrite the new row.
        if (!string.Equals(getSelectedLabel(), label, StringComparison.OrdinalIgnoreCase)) return null;

        return new DnsProbeResult(entry, ping);
    }

    /// <summary>
    /// Routes the active adapters through a DNS provider. Runs off-thread;
    /// the caller paints the telemetry cell from the returned result.
    /// </summary>
    public async Task<OperationResult?> ApplyDnsAsync(string label)
    {
        if (!DnsCatalog.TryGet(label, out var entry) || entry is null) return null;
        if (!_operationBus.TryAcquire()) return OperationResult.Fail("Another operation is already running.");

        try
        {
            var result = await Task.Run(() => _networkTools.ChangeDns(entry.Primary, entry.Secondary));
            StatusChanged?.Invoke(result.Message, !result.Success);
            return result;
        }
        catch (OperationCanceledException)
        {
            var canceled = OperationResult.Fail("Operation canceled.");
            StatusChanged?.Invoke(canceled.Message, true);
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
        }
    }

    /// <summary>
    /// Applies an iPad display profile. The service refuses while GameLoop
    /// runs and owns the backup/restore; that refusal is rendered verbatim.
    /// </summary>
    public async Task<OperationResult> ApplyIpadAsync(IpadResolutionPreset preset)
    {
        if (!_operationBus.TryAcquire()) return OperationResult.Fail("Another operation is already running.");

        try
        {
            var result = await Task.Run(() => _ipadLayout.SetIpadResolution(preset.Width, preset.Height));
            StatusChanged?.Invoke(result.Message, !result.Success);
            return result;
        }
        catch (OperationCanceledException)
        {
            var canceled = OperationResult.Fail("Operation canceled.");
            StatusChanged?.Invoke(canceled.Message, true);
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
        }
    }

    /// <summary>Restores the pre-iPad display state from the service's backup.</summary>
    public async Task<OperationResult> ResetIpadAsync()
    {
        if (!_operationBus.TryAcquire()) return OperationResult.Fail("Another operation is already running.");

        try
        {
            var result = await Task.Run(() => _ipadLayout.ResetIpadResolution());
            StatusChanged?.Invoke(result.Message, !result.Success);
            return result;
        }
        catch (OperationCanceledException)
        {
            var canceled = OperationResult.Fail("Operation canceled.");
            StatusChanged?.Invoke(canceled.Message, true);
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
        }
    }
}
