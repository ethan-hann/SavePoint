namespace Savepoint.Core.Services.Locking;

/// <summary>
/// A per-key async mutex. Services take it with a game id as the key, so git work on one game runs
/// one operation at a time while different games never wait on each other.
/// </summary>
/// <remarks>
/// Waiters are served in call order. There is no re-entrancy: asking for a key the caller already holds
/// from inside <c>func</c> waits for itself and never returns. The lock must not be held across a play
/// session. Keys compare ordinally and are forgotten as soon as nobody holds or waits for them.
/// Thread-safe.
/// </remarks>
public sealed class GameLock
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = [];

    internal Lock Gate => _gate;

    internal int ActiveKeyCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    // How many callers are queued for the key behind its holder; tests use it to know a request has reached the queue.
    internal int WaiterCount(string key)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(key, out var entry) ? entry.Waiters.Count : 0;
        }
    }

    /// <summary>Runs <paramref name="func"/> while holding the lock for <paramref name="key"/>.</summary>
    /// <param name="key">The lock to take, usually a game id.</param>
    /// <param name="func">The work to run; it starts only once every earlier caller on the same key has finished.</param>
    /// <param name="cancellationToken">
    /// Abandons the wait for the lock. It has no effect once <paramref name="func"/> has started, and
    /// <paramref name="func"/> is never started with a token that is already cancelled.
    /// </param>
    /// <returns>The result of <paramref name="func"/>.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled before <paramref name="func"/> started.</exception>
    /// <remarks>An exception from <paramref name="func"/> goes to this caller only and does not block the next one.</remarks>
    public Task<T> RunExclusiveAsync<T>(string key, Func<Task<T>> func, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        return RunExclusiveAsync(key, _ => func(), cancellationToken);
    }

    /// <summary>Runs <paramref name="func"/> while holding the lock for <paramref name="key"/>.</summary>
    /// <param name="key">The lock to take, usually a game id.</param>
    /// <param name="func">The work to run; it starts only once every earlier caller on the same key has finished.</param>
    /// <param name="cancellationToken">Abandons the wait for the lock; it has no effect once <paramref name="func"/> has started.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled before <paramref name="func"/> started.</exception>
    public Task RunExclusiveAsync(string key, Func<Task> func, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        return RunExclusiveAsync(key, _ => func(), cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="func"/> while holding the lock for <paramref name="key"/>, handing it a
    /// <see cref="GameLockHandle"/> so the work can allow a later request to preempt it.
    /// </summary>
    /// <param name="key">The lock to take, usually a game id.</param>
    /// <param name="func">The work to run; it starts only once every earlier caller on the same key has finished.</param>
    /// <param name="cancellationToken">Abandons the wait for the lock; it has no effect once <paramref name="func"/> has started.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled before <paramref name="func"/> started.</exception>
    public Task RunExclusiveAsync(string key, Func<GameLockHandle, Task> func, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        return RunExclusiveAsync<bool>(
            key,
            async handle =>
            {
                await func(handle).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="func"/> while holding the lock for <paramref name="key"/>, handing it a
    /// <see cref="GameLockHandle"/> so the work can allow a later request to preempt it.
    /// </summary>
    /// <param name="key">The lock to take, usually a game id.</param>
    /// <param name="func">The work to run; it starts only once every earlier caller on the same key has finished.</param>
    /// <param name="cancellationToken">Abandons the wait for the lock; it has no effect once <paramref name="func"/> has started.</param>
    /// <returns>The result of <paramref name="func"/>.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled before <paramref name="func"/> started.</exception>
    public async Task<T> RunExclusiveAsync<T>(string key, Func<GameLockHandle, Task<T>> func, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(func);
        cancellationToken.ThrowIfCancellationRequested();

        var handle = await AcquireAsync(key, cancellationToken).ConfigureAwait(false);
        try
        {
            // A grant that raced with the cancel must not start work the caller already gave up on.
            cancellationToken.ThrowIfCancellationRequested();
            return await func(handle).ConfigureAwait(false);
        }
        finally
        {
            Release(handle);
        }
    }

    // A cancel callback belongs to its holder; a requester must still queue when it fails.
    internal static void InvokeQuietly(Action? callback)
    {
        try
        {
            callback?.Invoke();
        }
        catch (Exception)
        {
            // Deliberately dropped, see above.
        }
    }

    private async Task<GameLockHandle> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        Waiter waiter;
        Action? preempt;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new Entry(key);
                _entries[key] = entry;
            }

            if (entry.Holder is null)
            {
                entry.Holder = new GameLockHandle(this, entry);
                return entry.Holder;
            }

            waiter = new Waiter(this, entry, cancellationToken);
            waiter.Node = entry.Waiters.AddLast(waiter);
            preempt = entry.Holder.TakePreemption();
        }

        InvokeQuietly(preempt);

        // Disposed after the grant, outside the lock: disposing waits for a callback that is running and that callback takes the lock.
        using var registration = cancellationToken.Register(static state => ((Waiter)state!).Owner.Abandon((Waiter)state!), waiter);
        return await waiter.Source.Task.ConfigureAwait(false);
    }

    private void Abandon(Waiter waiter)
    {
        var removed = false;
        lock (_gate)
        {
            if (waiter.Node is { List: not null } node)
            {
                waiter.Entry.Waiters.Remove(node);
                removed = true;
            }
        }

        if (removed)
        {
            waiter.Source.TrySetCanceled(waiter.Token);
        }
    }

    private void Release(GameLockHandle handle)
    {
        Waiter? next = null;
        GameLockHandle? nextHandle = null;
        lock (_gate)
        {
            var entry = handle.Entry;
            handle.MarkReleased();
            entry.Holder = null;
            if (entry.Waiters.First is { } node)
            {
                entry.Waiters.RemoveFirst();
                next = node.Value;
                nextHandle = new GameLockHandle(this, entry);
                entry.Holder = nextHandle;
            }
            else
            {
                _entries.Remove(entry.Key);
            }
        }

        // Requests still queued behind the new holder are noticed when it marks itself preemptible.
        next?.Source.TrySetResult(nextHandle!);
    }

    internal sealed class Entry(string key)
    {
        public string Key { get; } = key;

        public GameLockHandle? Holder { get; set; }

        public LinkedList<Waiter> Waiters { get; } = new();
    }

    internal sealed class Waiter(GameLock owner, Entry entry, CancellationToken token)
    {
        public GameLock Owner { get; } = owner;

        public Entry Entry { get; } = entry;

        public CancellationToken Token { get; } = token;

        public LinkedListNode<Waiter>? Node { get; set; }

        public TaskCompletionSource<GameLockHandle> Source { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
