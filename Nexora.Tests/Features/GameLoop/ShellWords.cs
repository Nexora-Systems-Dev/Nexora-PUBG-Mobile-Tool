namespace Nexora.Tests.Features.GameLoop;

/// <summary>
/// Quote-aware command parsing shared by the applier fakes: the applier now
/// emits every shell word single-quoted (<c>AdbShellText.Quote</c>), so the
/// old <c>Substring</c>/<c>Split(' ')</c> parsing would read the quote
/// characters as path characters. Splitting honors single quotes (with the
/// POSIX <c>'\''</c> escape the helper emits); unquoted words split on
/// whitespace exactly as before, so clean-input commands parse identically.
/// </summary>
internal static class ShellWords
{
    internal static List<string> Split(string command)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        var inWord = false;
        var inQuotes = false;
        var i = 0;
        while (i < command.Length)
        {
            var c = command[i];
            if (inQuotes)
            {
                // A quote followed by \' is the escaped-quote sequence the
                // quoter emits; a lone quote closes the quoted span.
                if (c == '\'' && i + 3 < command.Length && command[i + 1] == '\\' && command[i + 2] == '\'' && command[i + 3] == '\'')
                {
                    current.Append('\'');
                    i += 4;
                }
                else if (c == '\'')
                {
                    inQuotes = false;
                    i++;
                }
                else
                {
                    current.Append(c);
                    i++;
                }
            }
            else if (c == '\'')
            {
                inQuotes = true;
                inWord = true;
                i++;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inWord)
                {
                    words.Add(current.ToString());
                    current.Clear();
                    inWord = false;
                }

                i++;
            }
            else
            {
                inWord = true;
                current.Append(c);
                i++;
            }
        }

        if (inWord)
        {
            words.Add(current.ToString());
        }

        return words;
    }

    /// <summary>Strips one surrounding single-quote pair, unescaping '\''.</summary>
    internal static string Unquote(string word)
    {
        if (word.Length >= 2 && word.StartsWith('\'') && word.EndsWith('\''))
        {
            return word[1..^1].Replace("'\\''", "'", StringComparison.Ordinal);
        }

        return word;
    }
}
