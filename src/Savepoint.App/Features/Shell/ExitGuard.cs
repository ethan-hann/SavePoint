using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Shell;

/// <summary>
/// Decides whether the main window may close while jobs run (D31). A clear-all or a storage move that is cut short leaves a
/// half-done job behind, so closing asks first; whatever can be cancelled is cancelled, and the close waits briefly for it. A
/// free-up that has already deleted the snapshots the player chose to lose cannot be stopped and must not be cut off either, so
/// closing asks, offers no way to quit now, and waits for the run to finish however long that takes.
/// </summary>
public sealed class ExitGuard
{
    private readonly IJobsApi _jobs;
    private readonly IDialogs _dialogs;
    private readonly UiFormatter _text;
    private readonly TimeSpan _wait;

    /// <param name="jobs">The jobs Api, to see what is running.</param>
    /// <param name="dialogs">Asks the question.</param>
    /// <param name="text">Formats the question.</param>
    /// <param name="wait">How long to wait for cancelled jobs to end; three seconds when null.</param>
    public ExitGuard(IJobsApi jobs, IDialogs dialogs, UiFormatter text, TimeSpan? wait = null)
    {
        _jobs = jobs;
        _dialogs = dialogs;
        _text = text;
        _wait = wait ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>
    /// Asks about each running job that must not be cut short, cancels the jobs that can be cancelled and waits briefly for them, and
    /// waits for as long as it takes for a free-up that cannot be stopped.
    /// </summary>
    /// <returns>True when the window may close; false when the user chose to stay.</returns>
    public async Task<bool> PrepareToCloseAsync()
    {
        var answered = new HashSet<Guid>();
        var cancelled = new List<Guid>();
        while (true)
        {
            // Read again each time round: a question can stay open a while, a save that completed in the meantime queues an
            // optimization, and a free-up can pass its point of no return between one read and the next.
            var running = Running();
            if (running.Count == 0)
            {
                return true;
            }

            if (running.FirstOrDefault(job => NeedsQuestion(job) && !answered.Contains(job.JobId)) is { } toAsk)
            {
                if (!await AskAsync(toAsk).ConfigureAwait(true))
                {
                    return false;
                }

                answered.Add(toAsk.JobId);
                continue;
            }

            foreach (var job in running.Where(job => job.Cancellable && !cancelled.Contains(job.JobId)))
            {
                await _jobs.CancelAsync(job.JobId).ConfigureAwait(true);
                cancelled.Add(job.JobId);
            }

            if (running.Where(IsPastItsClear).Select(job => job.JobId).ToList() is { Count: > 0 } mustFinish)
            {
                await WaitUntilEndedAsync(mustFinish).ConfigureAwait(true);
                continue;
            }

            await WaitForAsync(cancelled).ConfigureAwait(true);
            return true;
        }
    }

    // Only these leave something half done that the user has to know about; a save, or an optimization that has not deleted anything yet, is simply stopped.
    private static bool NeedsQuestion(JobSnapshot job) =>
        job.Kind is JobKind.ClearData or JobKind.RelocateStorage || IsPastItsClear(job);

    // A free-up that has deleted the snapshots it was asked to cannot be stopped, and a git process left running would outlive the app.
    private static bool IsPastItsClear(JobSnapshot job) => job is { Kind: JobKind.Compact, Cancellable: false };

    private Task<bool> AskAsync(JobSnapshot job)
    {
        var (message, confirm) = job.Kind switch
        {
            JobKind.ClearData => ("shell.exit.clearMessage", "shell.exit.confirm"),
            JobKind.Compact => ("shell.exit.freeUpMessage", "shell.exit.freeUpConfirm"),
            _ => ("shell.exit.moveMessage", "shell.exit.confirm"),
        };
        return _dialogs.ConfirmAsync(new ConfirmRequest(
            _text.Message("shell.exit.title"),
            _text.Message(message),
            _text.Message(confirm),
            Destructive: true,
            CancelLabel: _text.Message("shell.exit.stay")));
    }

    private List<JobSnapshot> Running() => _jobs.List().Where(job => job.Status == JobStatus.Running).ToList();

    // Bounded: a job that does not end in time is left to the process ending.
    private async Task WaitForAsync(IReadOnlyList<Guid> jobIds)
    {
        var until = DateTime.UtcNow + _wait;
        while (DateTime.UtcNow < until && jobIds.Any(id => _jobs!.Get(id) is { Status: JobStatus.Running }))
        {
            await Task.Delay(25).ConfigureAwait(true);
        }
    }

    // Not bounded: cutting this job off is the harm the wait prevents. A job the registry has let go of has ended.
    private async Task WaitUntilEndedAsync(IReadOnlyList<Guid> jobIds)
    {
        while (jobIds.Any(id => _jobs.Get(id) is { Status: JobStatus.Running }))
        {
            await Task.Delay(25).ConfigureAwait(true);
        }
    }
}
