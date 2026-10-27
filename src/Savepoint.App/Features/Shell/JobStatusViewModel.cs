using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Shell;

/// <summary>
/// The shell-level job indicator: a pill with a spinner while any job runs, and a toast when one finishes. It sees
/// every game's jobs and the global ones in both layouts (the shell, not a screen, owns it). Launch jobs are left out:
/// the launch flow shows its own progress. A job that finished only in part is never reported as a success (D13.20).
/// </summary>
public sealed partial class JobStatusViewModel : ObservableObject
{
    private readonly UiFormatter _formatter;
    private readonly ToastHostViewModel _toasts;
    private readonly Func<Guid, bool>? _showsOutcome;
    private readonly Func<string, string?> _gameName;
    private readonly Dictionary<Guid, JobSnapshot> _jobs = [];
    private readonly HashSet<Guid> _reported = [];
    private readonly Action<string?>? _showActivity;

    // What pressing the pill opens: the game of the one running job, or all games. Kept after the last job ends, so a press just then still lands.
    private string? _opens;

    /// <summary>True while at least one job other than a launch runs.</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>The pill's text ("Taking a snapshot of Skyrim Special Edition..."); empty while no job runs.</summary>
    [ObservableProperty]
    private string _pillText = string.Empty;

    /// <summary>What the live region says: the pill's text each time it changes to something new. Completions are announced by the toast.</summary>
    [ObservableProperty]
    private string _announcement = string.Empty;

    /// <summary>What a screen reader says for the pill: its text, then that pressing it opens Activity; empty while no job runs.</summary>
    [ObservableProperty]
    private string _openName = string.Empty;

    /// <summary>Opens Activity on what the pill stands for: the one running job's game, or all games.</summary>
    public IRelayCommand OpenCommand { get; }

    /// <param name="text">Formats the pill and toast copy.</param>
    /// <param name="toasts">Receives a toast for each finished job.</param>
    /// <param name="gameName">Looks up a game's name by id; null for a game that is no longer in the library.</param>
    /// <param name="showsOutcome">Says whether something else already shows how a job ended, as the launch flow's card does for the save after play; null when nothing does.</param>
    /// <param name="showActivity">Opens Activity on a game, or on all games for null; the pill calls it when pressed.</param>
    public JobStatusViewModel(UiFormatter text, ToastHostViewModel toasts, Func<string, string?> gameName, Func<Guid, bool>? showsOutcome = null, Action<string?>? showActivity = null)
    {
        _showsOutcome = showsOutcome;
        _formatter = text;
        _toasts = toasts;
        _gameName = gameName;
        _showActivity = showActivity;
        OpenCommand = new RelayCommand(() => _showActivity?.Invoke(_opens));
    }

    /// <summary>Takes the jobs that exist when the shell starts. Running ones show in the pill; finished ones are history and get no toast.</summary>
    public void Seed(IReadOnlyList<JobSnapshot> jobs)
    {
        foreach (var job in jobs)
        {
            if (_jobs.TryGetValue(job.JobId, out var held) && job.Revision <= held.Revision)
            {
                continue;
            }

            _jobs[job.JobId] = job;
            if (job.Status != JobStatus.Running)
            {
                _reported.Add(job.JobId);
            }
        }

        Refresh();
    }

    /// <summary>Takes a job change, on the UI thread. A snapshot that is not newer than the one held for the job is dropped.</summary>
    public void Handle(JobSnapshot job)
    {
        if (_jobs.TryGetValue(job.JobId, out var held) && job.Revision <= held.Revision)
        {
            return;
        }

        _jobs[job.JobId] = job;
        if (job.Status != JobStatus.Running && _reported.Add(job.JobId) && job.Kind is not (JobKind.Launch or JobKind.Switch) && !OutcomeShownElsewhere(job))
        {
            _toasts.Show(ToastFor(job));
        }

        Refresh();
    }

    // A failure or half a save that a card already reports would be said twice, once more on top of the card's buttons.
    private bool OutcomeShownElsewhere(JobSnapshot job) =>
        job.Status is JobStatus.Failed or JobStatus.Partial && _showsOutcome?.Invoke(job.JobId) == true;

    /// <summary>Recomputes the pill from the jobs already held; the shell calls it when the games reload, so a pill written before a game's name was known catches up.</summary>
    public void Refresh()
    {
        var running = _jobs.Values
            .Where(job => job.Status == JobStatus.Running && job.Kind is not (JobKind.Launch or JobKind.Switch))
            .OrderBy(job => job.StartedAt)
            .ToList();

        IsVisible = running.Count > 0;
        if (running.Count > 0)
        {
            _opens = running.Count == 1 ? running[0].GameId : null;
        }

        var text = running.Count switch
        {
            0 => string.Empty,
            1 => RunningText(running[0]),
            _ => _formatter.Message("shell.jobStatus.multiple", ("count", running.Count)),
        };

        if (text != PillText)
        {
            PillText = text;
            OpenName = text.Length == 0 ? string.Empty : _formatter.Message("shell.jobStatus.open", ("status", text));
            if (text.Length > 0)
            {
                Announcement = text;
            }
        }

        // Only the newest few finished jobs are worth remembering; the registry keeps five for each game.
        if (_jobs.Count > 64)
        {
            foreach (var old in _jobs.Values.Where(job => job.Status != JobStatus.Running).OrderBy(job => job.FinishedAt).Take(_jobs.Count - 32).ToList())
            {
                _jobs.Remove(old.JobId);
            }
        }
    }

    private string RunningText(JobSnapshot job) => job.Kind switch
    {
        JobKind.RelocateStorage => _formatter.Message("shell.jobStatus.relocating"),
        JobKind.Compact when job.GameId is null => _formatter.Message("shell.jobStatus.compactingAll"),
        JobKind.Compact => _formatter.Message("shell.jobStatus.compacting", ("gameName", NameOf(job))),
        JobKind.PackStorage => _formatter.Message("shell.jobStatus.packing", ("gameName", NameOf(job))),
        JobKind.ClearData => _formatter.Message("shell.jobStatus.clearing"),
        _ => _formatter.Message("shell.jobStatus.saving", ("gameName", NameOf(job))),
    };

    private ToastMessage ToastFor(JobSnapshot job)
    {
        var name = ("gameName", (object?)NameOf(job));
        var (key, kind) = (job.Kind, job.Status) switch
        {
            (JobKind.RelocateStorage, JobStatus.Succeeded) => ("shell.jobStatus.relocateSuccess", ToastKind.Success),
            (JobKind.RelocateStorage, JobStatus.Cancelled) => ("shell.jobStatus.relocateCancelled", ToastKind.Info),
            (JobKind.RelocateStorage, JobStatus.Partial) => ("shell.jobStatus.relocatePartial", ToastKind.Warning),
            (JobKind.RelocateStorage, _) => ("shell.jobStatus.relocateFailure", ToastKind.Error),

            (JobKind.ClearData, JobStatus.Succeeded) => ("shell.jobStatus.clearSuccess", ToastKind.Success),
            (JobKind.ClearData, JobStatus.Partial) => ("shell.jobStatus.clearPartial", ToastKind.Warning),
            (JobKind.ClearData, _) => ("shell.jobStatus.clearFailure", ToastKind.Error),

            (JobKind.Compact, JobStatus.Succeeded) when job.GameId is null => ("shell.jobStatus.compactAllSuccess", ToastKind.Success),
            (JobKind.Compact, JobStatus.Partial) when job.GameId is null => ("shell.jobStatus.compactAllPartial", ToastKind.Warning),
            (JobKind.Compact, JobStatus.Cancelled) when job.GameId is null => ("shell.jobStatus.compactAllCancelled", ToastKind.Info),
            (JobKind.Compact, _) when job.GameId is null => ("shell.jobStatus.compactAllFailure", ToastKind.Error),
            (JobKind.Compact, JobStatus.Succeeded) => ("shell.jobStatus.compactSuccess", ToastKind.Success),
            (JobKind.Compact, JobStatus.Partial) => ("shell.jobStatus.compactPartial", ToastKind.Warning),
            (JobKind.Compact, JobStatus.Cancelled) => ("shell.jobStatus.compactCancelled", ToastKind.Info),
            (JobKind.Compact, _) => ("shell.jobStatus.compactFailure", ToastKind.Error),

            (JobKind.PackStorage, JobStatus.Succeeded) => ("shell.jobStatus.packSuccess", ToastKind.Success),
            (JobKind.PackStorage, JobStatus.Partial) => ("shell.jobStatus.packPartial", ToastKind.Warning),
            (JobKind.PackStorage, JobStatus.Cancelled) => ("shell.jobStatus.packCancelled", ToastKind.Info),
            (JobKind.PackStorage, _) => ("shell.jobStatus.packFailure", ToastKind.Error),

            (_, JobStatus.Succeeded) => ("shell.jobStatus.success", ToastKind.Success),
            (_, JobStatus.Partial) => ("shell.jobStatus.partial", ToastKind.Warning),
            (_, JobStatus.Cancelled) => ("shell.jobStatus.cancelled", ToastKind.Info),
            _ => ("shell.jobStatus.failure", ToastKind.Error),
        };

        return new ToastMessage(_formatter.Message(key, [name]), kind, kind is ToastKind.Error or ToastKind.Warning ? ReasonFor(job) : null);
    }

    // The first thing that went wrong, so a failure says why and a partial result says which half.
    private string? ReasonFor(JobSnapshot job)
    {
        var error = job.Error ?? JobErrors.FolderErrors(job.Result).FirstOrDefault();
        return error is null ? null : _formatter.Message(error, ErrorOperations.Of(job.Kind));
    }

    private string NameOf(JobSnapshot job) =>
        (job.GameId is null ? null : _gameName(job.GameId)) ?? _formatter.Message("shell.jobStatus.thisGame");
}
