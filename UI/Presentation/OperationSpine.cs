using Nexora.Shared.Contracts;

namespace Nexora.UI.Presentation;

/// <summary>
/// The collaborators a page ViewModel already holds and hands to
/// <see cref="OperationSpine.RunAsync"/>: the window-wide operation bus and the
/// shell status-line sink. Bundling the two keeps that method's parameter list
/// at three instead of four.
/// </summary>
public sealed record OperationContext(IPageOperationBus OperationBus, Action<string, bool>? StatusChanged);

/// <summary>
/// The acquire → status → run → translate → release spine shared by the page
/// ViewModels that run user-triggered work, so the window-wide "one long
/// operation at a time" plumbing has one home instead of one copy per page.
/// </summary>
/// <remarks>
/// The caller keeps its own cancellation ownership: it creates the
/// <see cref="CancellationTokenSource"/>, passes the token here, and clears its
/// field when this returns. This helper never reaches into a ViewModel's
/// privates — it only orchestrates the bus and translates outcomes.
/// </remarks>
public static class OperationSpine
{
    /// <summary>
    /// Acquires the bus, reports "Working...", runs the action on the token,
    /// translates cancellation and failure into results, and always releases.
    /// </summary>
    public static async Task<OperationResult> RunAsync(
        OperationContext context,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<OperationResult>> action)
    {
        if (!context.OperationBus.TryAcquire())
        {
            return OperationResult.Fail("Another operation is already running.");
        }

        context.StatusChanged?.Invoke("Working...", false);

        try
        {
            return await action(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Skip("Operation canceled.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
        finally
        {
            context.OperationBus.Release();
        }
    }
}
