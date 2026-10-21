using System.Text;

namespace Savepoint.Core.Git;

// git rewrites progress lines with \r, so \r, \n and \r\n all end a line.
internal sealed class LineSplitter
{
    private readonly Action<string> _onLine;
    private readonly StringBuilder _pending = new();

    // The previous chunk ended with \r, so a \n at the start of the next one belongs to the same terminator.
    private bool _skipLineFeed;

    public LineSplitter(Action<string> onLine)
    {
        _onLine = onLine;
    }

    public void Append(ReadOnlySpan<char> text)
    {
        while (!text.IsEmpty)
        {
            if (_skipLineFeed)
            {
                _skipLineFeed = false;
                if (text[0] == '\n')
                {
                    text = text[1..];
                    continue;
                }
            }

            var end = text.IndexOfAny('\r', '\n');
            if (end < 0)
            {
                _pending.Append(text);
                return;
            }

            _pending.Append(text[..end]);
            Emit();
            _skipLineFeed = text[end] == '\r';
            text = text[(end + 1)..];
        }
    }

    public void Flush()
    {
        if (_pending.Length > 0)
        {
            Emit();
        }
    }

    private void Emit()
    {
        var line = _pending.ToString();
        _pending.Clear();
        _onLine(line);
    }
}
