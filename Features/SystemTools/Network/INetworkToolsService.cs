using Nexora.Shared.Kernel;

namespace Nexora.Features.SystemTools.Network;

/// <summary>
/// Contract for network configuration and DNS diagnostic operations.
/// </summary>
public interface INetworkToolsService
{
    OperationResult ChangeDns(string primary, string secondary);

    Task<int?> PingDnsAsync(string host, CancellationToken cancellationToken = default);
}
