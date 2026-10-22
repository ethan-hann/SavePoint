using Savepoint.Core.Api;

namespace Savepoint.Core.Services.Jobs;

/// <summary>What a job's work uses to learn it was cancelled, to report how far it has got, and to say what it is doing.</summary>
/// <remarks>Everything here is safe to call from any thread. Reports made after the job has ended are dropped.</remarks>
public sealed class JobContext
{
    private readonly Action<JobLine> _addLine;
    private readonly Action _noLongerCancellable;

    internal JobContext(Guid jobId, CancellationToken cancellationToken, Action<JobProgress> report, Action<JobLine> addLine, Action noLongerCancellable)
    {
        JobId = jobId;
        CancellationToken = cancellationToken;
        _addLine = addLine;
        _noLongerCancellable = noLongerCancellable;
        Progress = new Reporter(report);
    }

    /// <summary>The id the registry gave the job, for work that needs to cancel itself or be preempted by id.</summary>
    public Guid JobId { get; }

    /// <summary>Cancelled when the job is cancelled through <see cref="JobRegistry.Cancel"/> or <c>IJobsApi.CancelAsync</c>.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Reports progress. Each report replaces the previous one, and a non-null <c>CurrentPath</c> is also appended to the
    /// job's recent lines as a path. Subscribers see at most about twenty updates a second; the latest report always arrives.
    /// </summary>
    public IProgress<JobProgress> Progress { get; }

    /// <summary>Appends a file path to the job's recent lines (the newest 200 are kept) without changing its progress.</summary>
    /// <param name="path">A file or folder as the player should see it; null or empty is ignored.</param>
    public void AddPath(string? path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            _addLine(JobLine.ForPath(path));
        }
    }

    /// <summary>Appends a step in plain words to the job's recent lines (the newest 200 are kept) without changing its progress.</summary>
    /// <param name="messageId">The catalog key of the step; use a constant of <see cref="JobSteps"/>.</param>
    /// <param name="args">The values for the message's placeholders; see <see cref="JobSteps"/> for what each step takes.</param>
    public void AddStep(string messageId, params (string Name, object? Value)[] args)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageId);
        _addLine(JobLine.ForStep(messageId, args));
    }

    /// <summary>
    /// Says the job has passed the point where stopping it would do harm, so its snapshot stops being cancellable and a later cancel is
    /// left alone. A job that cannot be stopped from here on calls it once, before the work that must finish.
    /// </summary>
    public void NoLongerCancellable() => _noLongerCancellable();

    private sealed class Reporter(Action<JobProgress> report) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => report(value);
    }
}
