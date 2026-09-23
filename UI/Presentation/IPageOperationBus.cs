namespace Nexora.UI.Presentation;

/// <summary>
/// The window-wide "one long operation at a time" guard. Every page that runs
/// user-triggered work — a Graphics apply, an Optimizer boost, a Tuning refresh
/// — goes through this bus, so a click on one page can never stack work on an
/// operation another page already started.
/// </summary>
/// <remarks>
/// Callers are async methods resuming on the UI thread, but the bus is shared
/// across every page, so the implementation lock-guards its check-then-act
/// acquire/release pair: concurrent callers can never both observe an idle bus.
/// <see cref="TryAcquire"/> is not re-entrant — a busy bus refuses, and the
/// caller renders that refusal instead of queueing — and every acquire is
/// paired with a <see cref="Release"/> in a finally block.
/// </remarks>
public interface IPageOperationBus
{
    bool IsBusy { get; }

    event EventHandler? BusyChanged;

    bool TryAcquire();

    void Release();
}
