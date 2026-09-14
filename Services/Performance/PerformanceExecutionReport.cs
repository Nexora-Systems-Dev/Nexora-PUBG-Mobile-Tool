using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

public sealed record PerformanceStep(string Name, OperationResult Result);

/// <summary>
/// Aggregates execution results and failure details across multiple performance operations.
/// </summary>
public sealed class PerformanceExecutionReport
{
    public PerformanceExecutionReport(IReadOnlyList<PerformanceStep> steps)
    {
        Steps = steps;
    }

    public IReadOnlyList<PerformanceStep> Steps { get; }

    public int SuccessfulCount => Steps.Count(step => step.Result.Success);

    public int AppliedCount => Steps.Count(step => !step.Result.IsSkipped && step.Result.Success);

    public int SkippedCount => Steps.Count(step => step.Result.IsSkipped);

    public int FailedCount => Failures.Count;

    public IReadOnlyList<PerformanceStep> Failures =>
        Steps.Where(step => !step.Result.Success).ToList();

    public bool Succeeded => Failures.Count == 0;

    public string GetSummary() =>
        $"{AppliedCount} applied, {SkippedCount} skipped, {FailedCount} failed.";

    public string FormatDetailed()
    {
        return string.Join("\n", Steps.Select(step =>
            $"• [{StepTag(step.Result)}] {step.Name}: {step.Result.Message}"));
    }

    public OperationResult ToDetailedResult(string title)
    {
        var header = $"{title}: {GetSummary()}";
        var body = FormatDetailed();
        var message = string.IsNullOrWhiteSpace(body) ? header : $"{header}\n{body}";

        if (FailedCount > 0)
        {
            return OperationResult.Fail(message);
        }

        if (AppliedCount == 0)
        {
            return OperationResult.Skip($"{header} No changes were needed.\n{body}");
        }

        return OperationResult.Ok(message);
    }

    private static string StepTag(OperationResult result)
    {
        if (!result.Success) return "Failed";
        return result.IsSkipped ? "Skipped" : "Applied";
    }

    public OperationResult ToOperationResult(string successMessage, string failurePrefix)
    {
        if (Succeeded)
        {
            var body = FormatDetailed();
            var detailed = string.IsNullOrWhiteSpace(body)
                ? $"{successMessage} {GetSummary()}"
                : $"{successMessage} {GetSummary()}\n{body}";
            return AppliedCount == 0
                ? OperationResult.Skip(detailed)
                : OperationResult.Ok(detailed);
        }

        var details = string.Join("; ", Failures.Select(step => $"{step.Name}: {step.Result.Message}"));
        return OperationResult.Fail($"{failurePrefix} {SuccessfulCount}/{Steps.Count} steps completed. {details}\n{FormatDetailed()}");
    }

    public static PerformanceExecutionReport Create(params (string Name, OperationResult Result)[] operations)
    {
        return new PerformanceExecutionReport(
            operations.Select(operation => new PerformanceStep(operation.Name, operation.Result)).ToList());
    }
}
