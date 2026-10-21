namespace Savepoint.App.Platform;

/// <summary>Who holds the named mutex that marks the running copy of the app.</summary>
internal enum MutexOutcome
{
    /// <summary>This object holds it: the first copy.</summary>
    Held,

    /// <summary>Another copy holds it.</summary>
    Taken,

    /// <summary>The machine would not create the mutex, so nothing can be said about other copies.</summary>
    Unavailable,
}

/// <summary>
/// Holds a named mutex for as long as it lives. A mutex belongs to the thread that took it, so a thread of its own holds
/// it until told to let go. That keeps the result the same whichever thread disposes, and lets two objects in one process
/// contend as two processes would. The thread is a background thread, so it never keeps the process alive.
/// </summary>
internal sealed class InstanceMutex : IDisposable
{
    private readonly ManualResetEventSlim _release = new(false);
    private readonly TaskCompletionSource<MutexOutcome> _outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;

    private InstanceMutex(string name)
    {
        _thread = new Thread(() => Hold(name)) { IsBackground = true, Name = "SavePoint single instance" };
    }

    /// <summary>Answers once the holding thread has tried for the mutex.</summary>
    public MutexOutcome Outcome => _outcome.Task.GetAwaiter().GetResult();

    /// <summary>Tries for the mutex without waiting for it.</summary>
    public static InstanceMutex Acquire(string name)
    {
        var mutex = new InstanceMutex(name);
        mutex._thread.Start();
        return mutex;
    }

    /// <summary>Lets go of the mutex if it was held. Safe to call more than once.</summary>
    public void Dispose()
    {
        _release.Set();
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Hold(string name)
    {
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(false, name);
            try
            {
                _outcome.SetResult(mutex.WaitOne(0) ? MutexOutcome.Held : MutexOutcome.Taken);
            }
            catch (AbandonedMutexException)
            {
                // The copy that held it ended without letting go; the mutex is ours now.
                _outcome.SetResult(MutexOutcome.Held);
            }

            if (_outcome.Task.Result == MutexOutcome.Held)
            {
                _release.Wait();
                mutex.ReleaseMutex();
            }
        }
        catch (UnauthorizedAccessException)
        {
            // The mutex exists and belongs to a copy running with other rights.
            _outcome.TrySetResult(MutexOutcome.Taken);
        }
        catch (Exception)
        {
            _outcome.TrySetResult(MutexOutcome.Unavailable);
        }
        finally
        {
            mutex?.Dispose();
        }
    }
}
