using System.Collections.Concurrent;
using Savepoint.Core.Diagnostics;

namespace Savepoint.Core.Launch.Orchestration;

// Runs posted actions one at a time, in the order they were posted, on a thread-pool thread and never inside the call that posted
// them, so a subscriber that calls back into the code that raised the event cannot deadlock it.
internal sealed class EventPump(IDiagnosticLog log)
{
    private readonly ConcurrentQueue<Action> _queue = new();
    private int _draining;

    public void Post(Action action)
    {
        _queue.Enqueue(action);
        Schedule();
    }

    private void Schedule()
    {
        if (Interlocked.CompareExchange(ref _draining, 1, 0) == 0)
        {
            ThreadPool.UnsafeQueueUserWorkItem(static pump => pump.Drain(), this, preferLocal: false);
        }
    }

    private void Drain()
    {
        while (true)
        {
            while (_queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    // A subscriber's failure must reach neither the work nor the other subscribers; the log is where it shows.
                    log.Warning("A subscriber to a session change threw; the session and the other subscribers carried on.", e);
                }
            }

            Interlocked.Exchange(ref _draining, 0);

            // Something may have been queued after the last dequeue and before the flag cleared.
            if (_queue.IsEmpty || Interlocked.CompareExchange(ref _draining, 1, 0) != 0)
            {
                return;
            }
        }
    }
}
