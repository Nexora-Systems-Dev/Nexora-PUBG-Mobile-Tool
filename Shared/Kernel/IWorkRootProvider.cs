namespace Nexora.Shared.Kernel;
using Nexora.Shared.Kernel;

/// <summary>
/// Environment boundary for application root directories.
/// </summary>
public interface IWorkRootProvider
{
    string AssetRoot { get; }

    string WorkRoot { get; }
}
