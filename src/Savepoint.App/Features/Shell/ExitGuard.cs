using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Shell;

/// <summary>
/// Decides whether the main window may close while jobs run (D31). A clear-all or a storage move that is cut short leaves a
/// half-done job behind, and so does a free-up that has already deleted the snapshots the player chose to lose, so closing asks
/// first; whatever can be cancelled is cancelled, and the close waits briefly for it.
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

    /// <summary>Asks if a job that must not be cut short is running, then cancels the jobs that can be cancelled and waits for them.</summary>
    /// <returns>True when the window may close; false when the user chose to stay.</returns>
    public async Task<bool> PrepareToCloseAsync()
    {
        var running = Running();
        if (running.Count == 0)
        {
            return true;
        }

        // Only these leave something half done that the user has to know about; a save, or an optimization that has not deleted anything yet, is simply stopped.
        if (running.FirstOrDefault(job => job.Kind is JobKind.ClearData or JobKind.RelocateStorage || job is { Kind: JobKind.Compact, Cancellable: false }) is { } unsafeToStop
            && !await _dialogs.ConfirmAsync(new ConfirmRequest(
                _text.Message("shell.exit.title"),
                _text.Message(unsafeToStop.Kind switch
                {
                    JobKind.ClearData => "shell.exit.clearMessage",
                    JobKind.Compact => "shell.exit.freeUpMessage",
                    _ => "shell.exit.moveMessage",
                }),
                _text.Message("shell.exit.confirm"),
                Destructive: true,
                CancelLabel: _text.Message("shell.exit.stay"))).ConfigureAwait(true))
        {
            return false;
        }

        // The question can stay open a while, and a save that completed in the meantime queues an optimization, so read the jobs again.
        var cancelled = new List<Guid>();
        foreach (var job in Running().Where(job => job.Cancellable))
        {
            await _jobs.CancelAsync(job.JobId).ConfigureAwait(true);
            cancelled.Add(job.JobId);
        }

        await WaitForAsync(cancelled).ConfigureAwait(true);
        return true;
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
}
