using Nexora.Shared.Kernel;
using Nexora.Features.Updates.Domain;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Updates.Application;

/// <summary>
/// Contract for checking, downloading, and applying application updates.
/// </summary>
public interface IUpdateService
{
    Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> DownloadAndLaunchAsync(UpdateInfo update, CancellationToken cancellationToken = default);
}
