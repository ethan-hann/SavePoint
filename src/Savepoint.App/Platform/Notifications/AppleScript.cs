using System.Text;

namespace Savepoint.App.Platform.Notifications;

/// <summary>Builds the AppleScript text that macOS shows a notification with.</summary>
internal static class AppleScript
{
    /// <summary>
    /// Turns text into an AppleScript string literal, quotes included, that reads back as exactly that text. Backslash and
    /// double quote are the only characters that can end a literal early; line breaks and tabs get their escapes, and other
    /// control characters, which AppleScript cannot spell, become a space.
    /// </summary>
    public static string Quote(string text)
    {
        var literal = new StringBuilder(text.Length + 2);
        literal.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\':
                    literal.Append("\\\\");
                    break;
                case '"':
                    literal.Append("\\\"");
                    break;
                case '\n':
                    literal.Append("\\n");
                    break;
                case '\r':
                    literal.Append("\\r");
                    break;
                case '\t':
                    literal.Append("\\t");
                    break;
                case '\u2028' or '\u2029':
                    literal.Append(' ');
                    break;
                default:
                    literal.Append(char.IsControl(c) ? ' ' : c);
                    break;
            }
        }

        return literal.Append('"').ToString();
    }

    /// <summary>The one-line script that posts a notification.</summary>
    public static string DisplayNotification(string title, string body) =>
        $"display notification {Quote(body)} with title {Quote(title)}";
}
