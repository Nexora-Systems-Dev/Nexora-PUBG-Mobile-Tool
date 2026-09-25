namespace Nexora.Infrastructure.GameLoop;

/// <summary>
/// Quotes one word for the on-device shell behind <c>adb shell</c>.
/// Mirrors <c>ProcessText.Quote</c>'s rule (wrap in single quotes) with the
/// POSIX escaping the device shell requires: PowerShell's doubled-quote
/// escape is wrong here — inside single quotes the device shell accepts no
/// escapes, so an embedded quote closes, escapes, and reopens
/// (<c>'\''</c>). Clean paths quote-neutrally: no spaces, metacharacters, or
/// quotes means the quoted form is the same single shell word.
/// </summary>
internal static class AdbShellText
{
    internal static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
