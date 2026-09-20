using Nexora.Shared.Kernel;
using Nexora.Features.Network.Domain;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Network.Application;

/// <summary>
/// Contract for network configuration and DNS diagnostic operations.
/// </summary>
public interface INetworkToolsService
{
    OperationResult ChangeDns(string primary, string secondary);

    Task<int?> PingDnsAsync(string host, CancellationToken cancellationToken = default);
}
