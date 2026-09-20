namespace Nexora.UI.Presentation;

/// <summary>
/// Single-instance implementation of <see cref="IPageOperationBus"/>. Owned by
/// the shell, shared with every page ViewModel, so the pages agree on what is
/// running without a private handshake each.
/// </summary>
public sealed class PageOperationBus : IPageOperationBus
{
    public bool IsBusy { get; private set; }

    public event EventHandler? BusyChanged;

    public bool TryAcquire()
    {
        if (IsBusy) return false;
        IsBusy = true;
        BusyChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Idempotent: a release on an idle bus is a no-op rather than an error, so
    /// the finally blocks that hold it never have to track whether the
    /// corresponding acquire succeeded.
    /// </summary>
    public void Release()
    {
        if (!IsBusy) return;
        IsBusy = false;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }
}
