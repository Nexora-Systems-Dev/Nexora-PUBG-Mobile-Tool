using Nexora.Shared.Kernel;

namespace Nexora.Services;

/// <summary>
/// Contract for checking, downloading, and applying application updates.
/// </summary>
public interface IUpdateService
{
    Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> DownloadAndLaunchAsync(UpdateInfo update, CancellationToken cancellationToken = default);
}
