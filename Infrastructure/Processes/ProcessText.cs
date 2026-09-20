namespace Nexora.Infrastructure.Processes;

internal static class ProcessText
{
    internal static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    internal static string GetError(ProcessResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        detail = detail.Trim();
        return string.IsNullOrWhiteSpace(detail)
            ? "No additional details were returned."
            : detail.Length > 240 ? detail[..240] : detail;
    }
}
