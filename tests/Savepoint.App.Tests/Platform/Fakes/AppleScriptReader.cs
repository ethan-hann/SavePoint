using System.Text;

namespace Savepoint.App.Tests.Platform.Fakes;

// Reads back the one script shape the notification layer writes, the way AppleScript would, so a test can tell what
// text the script would show and whether anything other than two string literals sits in it.
internal static class AppleScriptReader
{
    private const string _prefix = "display notification ";
    private const string _middle = " with title ";

    public static (string Body, string Title) ReadDisplayNotification(string script)
    {
        if (!script.StartsWith(_prefix, StringComparison.Ordinal))
        {
            throw new FormatException($"The script does not start with '{_prefix}': {script}");
        }

        var position = _prefix.Length;
        var body = ReadLiteral(script, ref position);
        if (string.CompareOrdinal(script, position, _middle, 0, _middle.Length) != 0)
        {
            throw new FormatException($"Expected '{_middle}' at {position}: {script}");
        }

        position += _middle.Length;
        var title = ReadLiteral(script, ref position);
        if (position != script.Length)
        {
            throw new FormatException($"Unexpected script after the title at {position}: {script}");
        }

        return (body, title);
    }

    public static string ReadLiteral(string script)
    {
        var position = 0;
        var text = ReadLiteral(script, ref position);
        if (position != script.Length)
        {
            throw new FormatException($"Unexpected script after the literal at {position}: {script}");
        }

        return text;
    }

    private static string ReadLiteral(string script, ref int position)
    {
        if (position >= script.Length || script[position] != '"')
        {
            throw new FormatException($"Expected an opening quote at {position}: {script}");
        }

        position++;
        var text = new StringBuilder();
        while (position < script.Length)
        {
            var c = script[position++];
            if (c == '"')
            {
                return text.ToString();
            }

            // U+2028 and U+2029 are line breaks to a script parser even though .NET does not call them control characters.
            if (char.IsControl(c) || c is '\u2028' or '\u2029')
            {
                throw new FormatException($"A raw control character (U+{(int)c:X4}) sits inside the literal: {script}");
            }

            if (c != '\\')
            {
                text.Append(c);
                continue;
            }

            if (position >= script.Length)
            {
                break;
            }

            text.Append(script[position++] switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '\\' => '\\',
                '"' => '"',
                var other => throw new FormatException($"AppleScript has no escape \\{other}: {script}"),
            });
        }

        throw new FormatException($"The literal is not closed: {script}");
    }
}
