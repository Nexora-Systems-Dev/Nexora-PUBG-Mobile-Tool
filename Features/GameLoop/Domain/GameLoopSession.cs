namespace Nexora.Features.GameLoop.Domain;

/// <summary>
/// Mutable connection state shared by the GameLoop collaborators: the loaded
/// save buffer, the active PUBG package, and the ADB connection flag.
/// State changes go through the explicit methods below — never through
/// public setters — and production callers must hold the
/// <see cref="GameLoopService"/> operation gate so concurrent operations
/// cannot publish torn (buffer, package) pairs or mutate the save buffer
/// outside serialization.
/// </summary>
public sealed class GameLoopSession
{
    private readonly object _mutationSync = new();

    public byte[]? ActiveSavContent { get; private set; }

    public string? CurrentPackage { get; private set; }

    public bool IsAdbConnected { get; private set; }

    public bool IsConnected => ActiveSavContent is not null && !string.IsNullOrWhiteSpace(CurrentPackage);

    /// Publishes a freshly pulled save buffer under its package as one
    /// explicit step. Either half may be absent; <see cref="IsConnected"/>
    /// reports whether the pair is complete. Locked against <see cref="Reset"/>
    /// so a concurrent <c>Disconnect</c> can never split the pair.
    /// </summary>
    public void LoadVersion(byte[]? savContent, string? packageName)
    {
        lock (_mutationSync)
        {
            ActiveSavContent = savContent;
            CurrentPackage = packageName;
        }
    }

    /// <summary>
    /// Marks the ADB transport connected without touching the save state.
    /// </summary>
    public void MarkAdbConnected()
    {
        lock (_mutationSync)
        {
            IsAdbConnected = true;
        }
    }

    /// <summary>
    /// Resets connection state and clears in-memory save data.
    /// Safe to call outside the operation gate: the reset is atomic against
    /// concurrent gated mutations, so no torn (buffer, package) pair survives.
    /// </summary>
    public void Reset()
    {
        lock (_mutationSync)
        {
            ActiveSavContent = null;
            CurrentPackage = null;
            IsAdbConnected = false;
        }
    }
}
