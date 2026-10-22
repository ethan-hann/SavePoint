using Savepoint.Core.Api;

namespace Savepoint.App.Hosting;

/// <summary>Stops the backend's jobs on the way out of a crash.</summary>
internal static class RunningJobs
{
    /// <summary>Asks every running job that can be cancelled to stop, and waits a moment for the requests to be taken.</summary>
    /// <param name="jobs">The jobs Api.</param>
    /// <param name="wait">The longest the call waits.</param>
    public static void CancelAll(IJobsApi jobs, TimeSpan wait)
    {
        var requests = new List<Task>();
        foreach (var job in jobs.List().Where(job => job.Status == JobStatus.Running && job.Cancellable))
        {
            try
            {
                requests.Add(jobs.CancelAsync(job.JobId));
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
    }
}
