using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Activity;

/// <summary>
/// The details of one job: the lines it has written in a list that can be selected and copied, the error that ended it, and Cancel.
/// New lines are added to the end of the list as they arrive, so a reader's place and selection stay where they are.
/// </summary>
public sealed partial class JobDetailsViewModel : ObservableObject
{
    private readonly JobRowViewModel _row;
    private readonly ActivityText _words;
    private readonly IJobsApi _jobs;
    private readonly IDialogs _dialogs;
    private readonly UiFormatter _text;
    private readonly Action<string> _announce;

    private JobSnapshot _job;
    private IReadOnlyList<JobLineViewModel> _selection = [];
    private ErrorInfo? _reason;
    private ErrorCardViewModel? _error;
    private bool _cancelIgnored;

    /// <param name="row">The job's row, which holds its latest snapshot.</param>
    /// <param name="words">Words for the job and its lines.</param>
    /// <param name="jobs">The jobs Api, for Cancel.</param>
    /// <param name="dialogs">Asks before a Cancel that would leave something half done.</param>
    /// <param name="text">Formats the details' own labels and the error card.</param>
    /// <param name="announce">Says something to a screen reader without moving focus.</param>
    public JobDetailsViewModel(JobRowViewModel row, ActivityText words, IJobsApi jobs, IDialogs dialogs, UiFormatter text, Action<string> announce)
    {
        _row = row;
        _job = row.Job;
        _words = words;
        _jobs = jobs;
        _dialogs = dialogs;
        _text = text;
        _announce = announce;
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => CanCancel);
        CopyCommand = new RelayCommand(Copy, () => HasLines);
        Apply(row.Job);
    }

    /// <summary>Raised with the text to put on the clipboard when Copy is pressed.</summary>
    public event EventHandler<string>? CopyRequested;

    /// <summary>The job's row.</summary>
    public JobRowViewModel Row => _row;

    /// <summary>The lines the job has written, oldest first, as far as the job still holds them.</summary>
    public ObservableCollection<JobLineViewModel> Lines { get; } = [];

    /// <summary>True when there is at least one line.</summary>
    public bool HasLines => Lines.Count > 0;

    /// <summary>The error card for a job that failed, finished only in part, or was canceled; null otherwise.</summary>
    public ErrorCardViewModel? Error => _error;

    /// <summary>True when <see cref="Error"/> shows.</summary>
    public bool HasError => _error is not null;

    /// <summary>True while Cancel is offered: the job runs, can be stopped, and has not been asked to stop.</summary>
    public bool CanCancel => _job is { Status: JobStatus.Running, Cancellable: true, CancelRequested: false };

    /// <summary>That the job is stopping, or why it cannot be stopped; empty when there is nothing to say.</summary>
    public string StopNote => _cancelIgnored && CanCancel ? _text.Message("activity.stop.ignored") : _words.StopNote(_job);

    /// <summary>True when <see cref="StopNote"/> has something to say.</summary>
    public bool HasStopNote => StopNote.Length > 0;

    /// <summary>True when SavePoint has stopped keeping this job but still shows it, because the reader has it open.</summary>
    public bool IsNoLongerKept { get; private set; }

    /// <summary>Says <see cref="IsNoLongerKept"/> in words; empty while the job is kept.</summary>
    public string KeptNote => IsNoLongerKept ? _text.Message("activity.details.keptNote") : string.Empty;

    /// <summary>Says the job is shown only because the reader has it open: SavePoint no longer keeps it.</summary>
    public void MarkNoLongerKept()
    {
        if (IsNoLongerKept)
        {
            return;
        }

        IsNoLongerKept = true;
        OnPropertyChanged(nameof(IsNoLongerKept));
        OnPropertyChanged(nameof(KeptNote));
    }

    /// <summary>The accessible name of the Cancel button, which says which job it stops.</summary>
    public string CancelAutomationName => _text.Message("activity.cancel.automationName", ("title", _row.Title));

    /// <summary>The accessible name of the list of lines, which says which job they belong to.</summary>
    public string LinesAutomationName => _text.Message("activity.details.linesName", ("title", _row.Title));

    /// <summary>Stops the job.</summary>
    public IAsyncRelayCommand CancelCommand { get; }

    /// <summary>Puts the selected lines, or every line when none is selected, on the clipboard.</summary>
    public IRelayCommand CopyCommand { get; }

    /// <summary>Every line as text, one per line.</summary>
    public string AllText => string.Join('\n', Lines.Select(line => line.Text));

    /// <summary>True while new lines should scroll into view: the reader has not scrolled up. The view keeps it.</summary>
    [ObservableProperty]
    private bool _isFollowing = true;

    /// <summary>Takes a newer snapshot of the job: new lines, a new state, a new error. The job's row has taken it first.</summary>
    /// <param name="job">The snapshot.</param>
    public void Apply(JobSnapshot job)
    {
        var hadLines = HasLines;
        var hadError = HasError;
        var hadNote = StopNote;
        var couldCancel = CanCancel;
        _job = job;

        ApplyLines(job);
        ApplyError(job);
        if (job.Status != JobStatus.Running)
        {
            _cancelIgnored = false;
        }

        OnPropertyChanged(nameof(AllText));
        if (hadLines != HasLines)
        {
            OnPropertyChanged(nameof(HasLines));
            CopyCommand.NotifyCanExecuteChanged();
        }

        if (hadError != HasError)
        {
            OnPropertyChanged(nameof(HasError));
        }

        OnPropertyChanged(nameof(Error));
        if (couldCancel != CanCancel)
        {
            OnPropertyChanged(nameof(CanCancel));
            CancelCommand.NotifyCanExecuteChanged();
        }

        if (hadNote != StopNote)
        {
            OnPropertyChanged(nameof(StopNote));
            OnPropertyChanged(nameof(HasStopNote));
        }
    }

    /// <summary>Tells the details which lines the reader has selected.</summary>
    /// <param name="selected">The selected lines, in any order.</param>
    public void SetSelection(IReadOnlyList<JobLineViewModel> selected) => _selection = selected;

    // Only what the job wrote since the last snapshot is added, and what fell off its front is dropped, so the lines already shown stay as they are.
    private void ApplyLines(JobSnapshot job)
    {
        var first = job.LinesDropped;
        if (Lines.Count > 0 && first > Lines[^1].Number + 1)
        {
            Lines.Clear();
        }

        while (Lines.Count > 0 && Lines[0].Number < first)
        {
            Lines.RemoveAt(0);
        }

        var next = Lines.Count > 0 ? Lines[^1].Number + 1 : first;
        for (var index = (int)(next - first); index < job.RecentLines.Count; index++)
        {
            var line = job.RecentLines[index];
            Lines.Add(new JobLineViewModel(first + index, _words.Lines.Format(line), line is JobLine.Step));
        }
    }

    private void ApplyError(JobSnapshot job)
    {
        var reason = _words.Reason(job);
        if (Equals(reason, _reason))
        {
            return;
        }

        _reason = reason;
        _error = reason is null ? null : new ErrorCardViewModel(reason, _text);
    }

    private async Task CancelAsync()
    {
        var id = _row.JobId;

        // Stopping a switch leaves the game's folder with a mix of two snapshots, so it is asked as the launch dialog's own stop is.
        if (ActivityText.IsSwitching(_job)
            && !await _dialogs.ConfirmAsync(new ConfirmRequest(
                _text.Message("activity.cancel.switchTitle"),
                _text.Message("launch.progress.stopNote"),
                _text.Message("launch.progress.stop"),
                Destructive: true,
                CancelLabel: _text.Message("activity.cancel.switchStay"))))
        {
            return;
        }

        await _jobs.CancelAsync(id);

        // A job that reached a point it cannot stop at between the press and the call leaves the request unheard; the reader is told.
        if (_jobs.Get(id) is { Status: JobStatus.Running, CancelRequested: false })
        {
            _cancelIgnored = true;
            OnPropertyChanged(nameof(StopNote));
            OnPropertyChanged(nameof(HasStopNote));
        }
    }

    private void Copy()
    {
        var chosen = _selection.Where(Lines.Contains).OrderBy(line => line.Number).ToList();
        IReadOnlyList<JobLineViewModel> lines = chosen.Count > 0 ? chosen : [.. Lines];
        CopyRequested?.Invoke(this, string.Join('\n', lines.Select(line => line.Text)));
        _announce(_text.Message("activity.details.copied", ("count", lines.Count)));
    }
}
