using Nexora.Shared.Kernel;

namespace Nexora.Features.Layout;

/// <summary>
/// Contract for managing iPad aspect ratio resolution and emulator keymap patching.
/// </summary>
public interface IIpadLayoutService
{
    OperationResult SetIpadResolution(int width, int height);

    OperationResult ResetIpadResolution();
}
