namespace Nexora.Infrastructure.Processes;

internal static class ProcessText
{
    private const int MaxErrorDetailLength = 240;

    /// <summary>
    /// Wraps a value in single quotes for a PowerShell argument, doubling any
    /// embedded single quotes — the only in-string escape PowerShell accepts.
    /// </summary>
    internal static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    internal static string GetError(ProcessResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        detail = detail.Trim();
        return string.IsNullOrWhiteSpace(detail)
            ? "No additional details were returned."
            : detail.Length > MaxErrorDetailLength ? detail[..MaxErrorDetailLength] : detail;
    }
}
