using Nexora.Shared.Kernel;

namespace Nexora.Services;

/// <summary>
/// Contract for managing iPad aspect ratio resolution and emulator keymap patching.
/// </summary>
public interface IIpadLayoutService
{
    OperationResult Apply(int width, int height);

    OperationResult Reset();
}
