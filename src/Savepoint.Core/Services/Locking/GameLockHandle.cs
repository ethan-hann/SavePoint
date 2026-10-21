namespace Savepoint.Core.Services.Locking;

/// <summary>
/// Given to the work running under a <see cref="GameLock"/>; lets that work allow a later request on
/// the same key to preempt it (long background work such as repacking, which is safe to interrupt).
/// </summary>
/// <remarks>Valid only while the work runs; after the lock is released every member does nothing. Thread-safe.</remarks>
public sealed class GameLockHandle
{
    private readonly GameLock _owner;
    private Registration? _current;
    private bool _released;

    internal GameLockHandle(GameLock owner, GameLock.Entry entry)
    {
        _owner = owner;
        Entry = entry;
    }

    internal GameLock.Entry Entry { get; }

    /// <summary>
    /// Allows a request for the same key to cancel this holder: the request calls <paramref name="cancel"/>
    /// and then waits for the holder to release the lock as usual.
    /// </summary>
    /// <param name="cancel">
    /// Asks the work to stop, typically by cancelling its token. It runs at most once per registration, on the
    /// thread of the requester, outside any internal lock, and an exception it throws is dropped. If a request is
    /// already waiting when this is called, it runs immediately on the caller's thread.
    /// </param>
    /// <returns>A registration; disposing it withdraws the permission.</returns>
    public IDisposable MarkPreemptible(Action cancel)
    {
        ArgumentNullException.ThrowIfNull(cancel);
        var registration = new Registration(this, cancel);
        Action? runNow = null;
        lock (_owner.Gate)
        {
            if (!_released)
            {
                _current = registration;
                if (Entry.Waiters.Count > 0)
                {
                    registration.Fired = true;
                    runNow = cancel;
                }
            }
        }

        GameLock.InvokeQuietly(runNow);
        return registration;
    }

    // The caller holds the lock's gate.
    internal Action? TakePreemption()
    {
        if (_current is { Fired: false } registration)
        {
            registration.Fired = true;
            return registration.Cancel;
        }

        return null;
    }

    // The caller holds the lock's gate.
    internal void MarkReleased()
    {
        _released = true;
        _current = null;
    }

    private sealed class Registration(GameLockHandle handle, Action cancel) : IDisposable
    {
        public Action Cancel { get; } = cancel;

        public bool Fired { get; set; }

        public void Dispose()
        {
            lock (handle._owner.Gate)
            {
                if (ReferenceEquals(handle._current, this))
                {
                    handle._current = null;
                }
            }
        }
    }
}
