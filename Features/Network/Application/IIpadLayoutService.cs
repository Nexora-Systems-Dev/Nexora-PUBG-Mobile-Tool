using Nexora.Shared.Kernel;
using Nexora.Features.Network.Domain;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Network.Application;

/// <summary>
/// Contract for managing iPad aspect ratio resolution and emulator keymap patching.
/// </summary>
public interface IIpadLayoutService
{
    OperationResult SetIpadResolution(int width, int height);

    OperationResult ResetIpadResolution();
}
