namespace Savepoint.Core.Git;

internal sealed class GitByteSink
{
    private readonly Lock _gate = new();
    private readonly MemoryStream _bytes = new();
    private bool _detached;

    public void Append(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            if (!_detached)
            {
                _bytes.Write(bytes);
            }
        }
    }

    public void Detach()
    {
        lock (_gate)
        {
            _detached = true;
        }
    }

    public byte[] ToArray()
    {
        lock (_gate)
        {
            return _bytes.ToArray();
        }
    }
}
