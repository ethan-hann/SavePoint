using System.Diagnostics;
using Savepoint.Core.Api;

namespace Savepoint.App.Hosting;

/// <summary>Stops the backend's jobs on the way out of a crash.</summary>
internal static class RunningJobs
{
    private static readonly TimeSpan _pollEvery = TimeSpan.FromMilliseconds(25);

    /// <summary>Asks every running job that can be cancelled to stop, then waits for each to leave the running state.</summary>
    /// <param name="jobs">The jobs Api.</param>
    /// <param name="wait">The longest the call waits for all of it: the requests and the jobs unwinding after them.</param>
    public static void CancelAll(IJobsApi jobs, TimeSpan wait)
    {
        var clock = Stopwatch.StartNew();
        var asked = new List<Guid>();
        var requests = new List<Task>();
        foreach (var job in jobs.List().Where(job => job.Status == JobStatus.Running && job.Cancellable))
        {
            try
            {
                requests.Add(jobs.CancelAsync(job.JobId));
                asked.Add(job.JobId);
            }
            catch (Exception)
            {
                // The rest still get their request; the process ends after the window either way.
            }
        }

        try
        {
            Task.WaitAll([.. requests], wait);
        }
        catch (AggregateException)
        {
            // A request that failed has nothing more to tell a crash that is already being reported.
        }

        // A cancel only requests the stop; the job goes on unwinding, and no window should come up over a git process still writing.
        // This thread may be the window's own, with no message pump, so the wait polls instead of awaiting.
        try
        {
            while (clock.Elapsed < wait && asked.Any(id => jobs.Get(id) is { Status: JobStatus.Running }))
            {
                Thread.Sleep(_pollEvery);
            }
        }
        catch (Exception)
        {
            // A jobs Api that cannot answer is the crash's own trouble; the process ends after the window either way.
        }
    }
}
