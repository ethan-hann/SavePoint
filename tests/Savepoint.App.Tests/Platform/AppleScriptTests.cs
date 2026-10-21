using Savepoint.App.Platform.Notifications;
using Savepoint.App.Tests.Platform.Fakes;

namespace Savepoint.App.Tests.Platform;

public class AppleScriptTests
{
    public static TheoryData<string> HostileTexts => new()
    {
        string.Empty,
        "plain",
        "say \"hi\"",
        "back\\slash",
        "\\\"",
        "\\\\\"",
        "\" & (do shell script \"touch x\") & \"",
        "\\\" & quit & \\\"",
        "line one\nline two",
        "windows\r\nline",
        "tab\there",
        "single 'quotes' and $(subshell) and `backticks`",
        "semi;colon | pipe & amp < > redirect",
        "\u65E5\u672C\u8A9E \U0001F600 emoji",
        new string('x', 5000),
    };

    [Theory]
    [MemberData(nameof(HostileTexts))]
    public void A_quoted_text_reads_back_as_itself(string text)
    {
        var literal = AppleScript.Quote(text);

        Assert.Equal(text, AppleScriptReader.ReadLiteral(literal));
    }

    [Fact]
    public void A_plain_text_is_wrapped_in_double_quotes_and_nothing_else()
    {
        Assert.Equal("\"hello world\"", AppleScript.Quote("hello world"));
    }

    [Fact]
    public void A_quote_is_escaped_with_a_backslash()
    {
        Assert.Equal("\"say \\\"hi\\\"\"", AppleScript.Quote("say \"hi\""));
    }

    [Fact]
    public void A_backslash_in_front_of_a_quote_is_doubled_so_the_quote_still_needs_its_own_escape()
    {
        // Escaping only the quote would leave the text's own backslash eating the escape and close the literal early.
        Assert.Equal("\"\\\\\\\"\"", AppleScript.Quote("\\\""));
    }

    [Fact]
    public void Line_breaks_and_tabs_become_escapes_so_the_literal_stays_on_one_line()
    {
        Assert.Equal("\"a\\nb\\rc\\td\"", AppleScript.Quote("a\nb\rc\td"));
    }

    [Theory]
    [InlineData('\0')]
    [InlineData('\u0001')]
    [InlineData('\u001B')]
    [InlineData('\u007F')]
    [InlineData('\u0085')]
    [InlineData('\u2028')]
    [InlineData('\u2029')]
    public void Other_control_characters_become_a_space_because_AppleScript_has_no_escape_for_them(char control)
    {
        Assert.Equal("\"a b\"", AppleScript.Quote("a" + control + "b"));
    }

    [Fact]
    public void Random_text_never_breaks_out_of_its_literal()
    {
        var random = new Random(20261020);
        const string Alphabet = "ab \"\\\n\r\t&;'\u00E9\U0001F600";
        var characters = Alphabet.EnumerateRunes().Select(rune => rune.ToString()).ToArray();

        for (var i = 0; i < 500; i++)
        {
            var text = string.Concat(Enumerable.Range(0, random.Next(0, 40)).Select(_ => characters[random.Next(characters.Length)]));

            Assert.Equal(text, AppleScriptReader.ReadLiteral(AppleScript.Quote(text)));
        }
    }

    [Theory]
    [MemberData(nameof(HostileTexts))]
    public void The_notification_script_holds_exactly_the_body_and_the_title(string text)
    {
        var script = AppleScript.DisplayNotification(title: "Title " + text, body: "Body " + text);

        var (body, title) = AppleScriptReader.ReadDisplayNotification(script);

        Assert.Equal("Body " + text, body);
        Assert.Equal("Title " + text, title);
    }

    [Fact]
    public void The_notification_script_is_the_display_notification_command()
    {
        Assert.Equal("display notification \"Saved\" with title \"SavePoint\"", AppleScript.DisplayNotification("SavePoint", "Saved"));
    }
}
