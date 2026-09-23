namespace Nexora.UI.Presentation;

/// <summary>
/// Single-instance implementation of <see cref="IPageOperationBus"/>. Owned by
/// the shell, shared with every page ViewModel, so the pages agree on what is
/// running without a private handshake each.
/// </summary>
public sealed class PageOperationBus : IPageOperationBus
{
    // Guards the check-then-act acquire/release pair. Callers are UI-thread
    // affine in practice, but the bus is shared across every page, so two
    // near-simultaneous clicks must not both observe an idle bus and acquire.
    private readonly object _gate = new();

    // Volatile so an IsBusy read outside the lock still observes a fresh write.
    private volatile bool _isBusy;

    public bool IsBusy => _isBusy;

    public event EventHandler? BusyChanged;

    public bool TryAcquire()
    {
        lock (_gate)
        {
            if (_isBusy) return false;
            _isBusy = true;
        }

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
        lock (_gate)
        {
            if (!_isBusy) return;
            _isBusy = false;
        }

        BusyChanged?.Invoke(this, EventArgs.Empty);
    }
}
