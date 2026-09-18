using System.Windows.Media.Imaging;
using Nexora.Shared.Kernel;

namespace Nexora.Features.GameLoop;

/// <summary>
/// Contract for GameLoop desktop shortcut creation and icon preview resolution.
/// </summary>
public interface IShortcutService
{
    OperationResult CreateShortcut(string displayName, string packageName);

    BitmapImage? GetIcon(string? packageName);
}
