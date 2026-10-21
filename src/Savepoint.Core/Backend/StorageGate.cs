namespace Savepoint.Core.Backend;

/// <summary>
/// An async reader/writer gate around the storage root's identity. Every operation that reads or writes a git directory
/// holds it shared; a storage move or a clear-all holds it exclusive for the moment the root changes, so nothing is ever
/// working on the old root while it is deleted or replaced. A waiting exclusive request keeps new shared ones out, so it
/// cannot starve.
/// </summary>
internal sealed class StorageGate
{
    private readonly SemaphoreSlim _turnstile = new(1, 1);
    private readonly SemaphoreSlim _roomEmpty = new(1, 1);
    private readonly SemaphoreSlim _counter = new(1, 1);
    private int _readers;

    public async Task<IDisposable> EnterSharedAsync(CancellationToken cancellationToken = default)
    {
        // Passing through the turnstile is what a waiting writer blocks.
        await _turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
        _turnstile.Release();

        await _counter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_readers == 0)
            {
                await _roomEmpty.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            _readers++;
        }
        finally
        {
            _counter.Release();
        }

        return new Lease(ExitShared);
    }

    public async Task<IDisposable> EnterExclusiveAsync(CancellationToken cancellationToken = default)
    {
        await _turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _roomEmpty.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _turnstile.Release();
            throw;
        }

        return new Lease(() =>
        {
            _roomEmpty.Release();
            _turnstile.Release();
        });
    }

    private void ExitShared()
    {
        _counter.Wait();
        try
        {
            if (--_readers == 0)
            {
                _roomEmpty.Release();
            }
        }
        finally
        {
            _counter.Release();
        }
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
