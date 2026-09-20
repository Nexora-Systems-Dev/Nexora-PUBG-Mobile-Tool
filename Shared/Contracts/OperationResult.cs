namespace Nexora.Shared.Contracts;
using Nexora.Shared.Contracts;

/// <summary>
/// Outcome of a single optimization step: actually applied,
/// skipped as not applicable, or failed.
/// </summary>
public enum StepOutcome
{
    Applied,
    Skipped,
    Failed
}

/// <summary>
/// Result of a single optimization step. The state is a deliberate tri-state:
/// <see cref="StepOutcome"/> is the source of truth (Applied / Skipped / Failed)
/// while <see cref="Success"/> is the gate signal — true for both Applied and
/// Skipped, false only for Failed — so callers can branch on success without
/// caring whether work was needed, and reporting can still distinguish "did
/// work" from "nothing to do" via <see cref="Outcome"/>/<see cref="IsSkipped"/>.
/// </summary>
public sealed record OperationResult(bool Success, string Message, StepOutcome Outcome = StepOutcome.Applied)
{
    public static OperationResult Ok(string message) => new(true, message, StepOutcome.Applied);
    public static OperationResult Fail(string message) => new(false, message, StepOutcome.Failed);
    public static OperationResult Skip(string message) => new(true, message, StepOutcome.Skipped);

    public bool IsSkipped => Outcome == StepOutcome.Skipped;
}
