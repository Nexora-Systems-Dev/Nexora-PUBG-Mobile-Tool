using FluentAssertions;
using Nexora.Infrastructure.GameLoop;
using Xunit;

namespace Nexora.Tests.Infrastructure;

/// <summary>
/// Pins the on-device shell quoting rule: one value is always one shell
/// word. Clean inputs are quote-neutral (same word with or without the
/// quotes); metacharacters, whitespace, and quotes are inert inside the
/// quoted span, with the POSIX '\'' escape for embedded quotes (PowerShell's
/// doubled-quote escape would be a live quote on the device shell).
/// </summary>
public sealed class AdbShellTextTests
{
    [Fact]
    public void Quote_CleanPath_WrapsWithoutChangingTheWord() =>
        AdbShellText.Quote("/sdcard/Android/data/com.pubg.krmobile")
            .Should().Be("'/sdcard/Android/data/com.pubg.krmobile'");

    [Fact]
    public void Quote_PackageName_WrapsWithoutChangingTheWord() =>
        AdbShellText.Quote("com.pubg.krmobile").Should().Be("'com.pubg.krmobile'");

    [Fact]
    public void Quote_EmptyValue_ProducesEmptyQuotedWord() =>
        AdbShellText.Quote(string.Empty).Should().Be("''");

    [Fact]
    public void Quote_Space_StaysInsideOneWord() =>
        AdbShellText.Quote("PUBG Mobile").Should().Be("'PUBG Mobile'");

    [Theory]
    [InlineData("a; rm -r /sdcard/mk_safe_folder")]
    [InlineData("$(touch /sdcard/pwned)")]
    [InlineData("`touch /sdcard/pwned`")]
    [InlineData("a|b")]
    [InlineData("a&b")]
    [InlineData("a*b")]
    [InlineData("a$b")]
    public void Quote_Metacharacters_AreInertInsideTheQuotedSpan(string hostile)
    {
        var quoted = AdbShellText.Quote(hostile);

        quoted.Should().Be("'" + hostile + "'");
        quoted.Should().StartWith("'").And.EndWith("'");
    }

    [Fact]
    public void Quote_EmbeddedQuote_UsesThePosixCloseEscapeReopenSequence() =>
        AdbShellText.Quote("a'b").Should().Be("'a'\\''b'");

    [Fact]
    public void Quote_CommandSubstitutionInsideQuotes_DoesNotExecute()
    {
        // The device shell performs no expansion inside single quotes, so the
        // quoted payload is data even when it looks like a command.
        var command = $"pm clear {AdbShellText.Quote("com.evil; touch /sdcard/pwned")}";

        command.Should().Be("pm clear 'com.evil; touch /sdcard/pwned'");
    }
}
