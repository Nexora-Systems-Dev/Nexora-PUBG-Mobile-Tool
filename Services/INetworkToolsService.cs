using Nexora.Shared.Kernel;

namespace Nexora.Services;

/// <summary>
/// Contract for network configuration and DNS diagnostic operations.
/// </summary>
public interface INetworkToolsService
{
    OperationResult ChangeDns(string primary, string secondary);

    int? PingDns(string host);

    Task<int?> PingDnsAsync(string host, CancellationToken cancellationToken = default);
}
