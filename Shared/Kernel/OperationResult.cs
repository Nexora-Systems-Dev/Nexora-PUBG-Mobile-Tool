namespace Nexora.Shared.Kernel;

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

public sealed record OperationResult(bool Success, string Message, StepOutcome Outcome = StepOutcome.Applied)
{
    public static OperationResult Ok(string message) => new(true, message, StepOutcome.Applied);
    public static OperationResult Fail(string message) => new(false, message, StepOutcome.Failed);
    public static OperationResult Skip(string message) => new(true, message, StepOutcome.Skipped);

    public bool IsSkipped => Outcome == StepOutcome.Skipped;
}
