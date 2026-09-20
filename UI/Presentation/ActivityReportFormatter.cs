using Nexora.Shared.Kernel;
using Nexora.Shared.Contracts;

namespace Nexora.UI.Presentation;

/// <summary>
/// Splits a detailed boost <see cref="OperationResult"/> into an overall
/// status line and a per-step details block for the Activity panel.
/// Pure and testable; MainWindow only renders the returned strings.
/// </summary>
public sealed record ActivityReportDisplay(string StatusLine, string Details, bool IsError);

public static class ActivityReportFormatter
{
    public static ActivityReportDisplay Format(OperationResult result)
    {
        var message = result.Message ?? string.Empty;
        var lines = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count == 0)
        {
            return new ActivityReportDisplay("Ready", string.Empty, !result.Success);
        }

        var statusLine = lines[0];
        var details = lines.Count > 1
            ? string.Join("\n", lines.Skip(1))
            : string.Empty;

        return new ActivityReportDisplay(statusLine, details, !result.Success);
    }
}
