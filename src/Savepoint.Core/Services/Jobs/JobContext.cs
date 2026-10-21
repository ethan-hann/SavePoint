using Savepoint.Core.Api;

namespace Savepoint.Core.Services.Jobs;

/// <summary>What a job's work uses to learn it was cancelled and to report how far it has got.</summary>
/// <remarks>Everything here is safe to call from any thread. Reports made after the job has ended are dropped.</remarks>
public sealed class JobContext
{
    private readonly Action<string> _addLine;

    internal JobContext(Guid jobId, CancellationToken cancellationToken, Action<JobProgress> report, Action<string> addLine)
    {
        JobId = jobId;
        CancellationToken = cancellationToken;
        _addLine = addLine;
        Progress = new Reporter(report);
    }

    /// <summary>The id the registry gave the job, for work that needs to cancel itself or be preempted by id.</summary>
    public Guid JobId { get; }

    /// <summary>Cancelled when the job is cancelled through <see cref="JobRegistry.Cancel"/> or <c>IJobsApi.CancelAsync</c>.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Reports progress. Each report replaces the previous one, and a non-null <c>CurrentPath</c> is also appended to the
    /// job's recent lines. Subscribers see at most about twenty updates a second; the latest report always arrives.
    /// </summary>
    public IProgress<JobProgress> Progress { get; }

    /// <summary>Appends a line to the job's recent lines (the newest 200 are kept) without changing its progress.</summary>
    /// <param name="line">Text such as a line of git output; null or empty lines are ignored.</param>
    public void AddLine(string line) => _addLine(line);

    private sealed class Reporter(Action<JobProgress> report) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => report(value);
    }
}
