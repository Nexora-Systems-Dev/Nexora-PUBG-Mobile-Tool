using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

public sealed record PerformanceStep(string Name, OperationResult Result);

/// <summary>
/// Keeps partial success visible instead of collapsing several operations into
/// one opaque boolean. This will later feed the Optimizer activity panel.
/// </summary>
public sealed class PerformanceExecutionReport
{
    public PerformanceExecutionReport(IReadOnlyList<PerformanceStep> steps)
    {
        Steps = steps;
    }

    public IReadOnlyList<PerformanceStep> Steps { get; }

    public int SuccessfulCount => Steps.Count(step => step.Result.Success);

    public IReadOnlyList<PerformanceStep> Failures =>
        Steps.Where(step => !step.Result.Success).ToList();

    public bool Succeeded => Failures.Count == 0;

    public OperationResult ToOperationResult(string successMessage, string failurePrefix)
    {
        if (Succeeded)
        {
            return OperationResult.Ok(successMessage);
        }

        var details = string.Join("; ", Failures.Select(step => $"{step.Name}: {step.Result.Message}"));
        return OperationResult.Fail($"{failurePrefix} {SuccessfulCount}/{Steps.Count} steps completed. {details}");
    }

    public static PerformanceExecutionReport Create(params (string Name, OperationResult Result)[] operations)
    {
        return new PerformanceExecutionReport(
            operations.Select(operation => new PerformanceStep(operation.Name, operation.Result)).ToList());
    }
}
