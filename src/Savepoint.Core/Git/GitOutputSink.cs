using System.Runtime.ExceptionServices;
using System.Text;

namespace Savepoint.Core.Git;

internal sealed class CallbackFailure
{
    private ExceptionDispatchInfo? _first;

    public bool HasFailed => Volatile.Read(ref _first) is not null;

    public void Record(Exception exception) =>
        Interlocked.CompareExchange(ref _first, ExceptionDispatchInfo.Capture(exception), null);

    public void ThrowIfFailed() => Volatile.Read(ref _first)?.Throw();
}

internal sealed class GitOutputSink
{
    private readonly StringBuilder? _collected;
    private readonly int _collectedLimit;
    private readonly LineSplitter? _lines;

    public GitOutputSink(bool collect, int collectedLimit, Action<string>? onLine, CallbackFailure failure, Action onFailure)
    {
        _collected = collect ? new StringBuilder() : null;
        _collectedLimit = collectedLimit;
        if (onLine is not null)
        {
            _lines = new LineSplitter(line =>
            {
                if (failure.HasFailed)
                {
                    return; // keep draining so git does not block on a full pipe, but stop calling out
                }

                try
                {
                    onLine(line);
                }
                catch (Exception e)
                {
                    failure.Record(e);
                    onFailure();
                }
            });
        }
    }

    public string Text => _collected?.ToString() ?? string.Empty;

    public void Append(ReadOnlySpan<char> text)
    {
        if (_collected is not null)
        {
            _collected.Append(text);

            // Only the end matters for an error message, and a progress meter can run to megabytes.
            if (_collected.Length > (long)_collectedLimit * 2)
            {
                _collected.Remove(0, _collected.Length - _collectedLimit);
            }
        }

        _lines?.Append(text);
    }

    public void Complete()
    {
        if (_collected is not null && _collected.Length > _collectedLimit)
        {
            _collected.Remove(0, _collected.Length - _collectedLimit);
        }

        _lines?.Flush();
    }
}
