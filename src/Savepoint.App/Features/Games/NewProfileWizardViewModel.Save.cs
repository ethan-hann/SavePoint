using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Games;

public sealed partial class NewProfileWizardViewModel
{
    private readonly HashSet<RepoRole> _savedRoles = [];
    private List<RepoRole> _failedRoles = [];
    private Guid? _jobId;
    private long _revision;
    private int _milestone;
    private bool _finishingAnnounced;
    private bool _stopRequested;
    private string? _profileId;

    // A profile exists once any folder was saved, which is when the rest of the app has something to show.
    private bool Created => _savedRoles.Count > 0;

    // The profile is created from what is on disk now; its first point carries the profile's name.
    private async Task BeginSaveAsync()
    {
        SubmitError = string.Empty;
        _jobs.JobChanged -= OnJobChanged;
        _jobs.JobChanged += OnJobChanged;
        Step = WizardStep.Save;
        EnterSaving();

        var notes = TrimmedNotes.Length > 0 ? TrimmedNotes : null;
        var created = await _profiles.CreateAsync(_game.Id, TrimmedName, notes, TrimmedName);
        if (!created.Ok)
        {
            Step = WizardStep.Check;
            SubmitError = _text.Message(created.Error, ErrorOperation.Snapshot);
            return;
        }

        Track(created.Value.JobId);
    }

    // Only the folders that failed are captured again, into the profile the first attempt created.
    private async Task RetryAsync()
    {
        if (_profileId is null || _failedRoles.Count == 0)
        {
            return;
        }

        SubmitError = string.Empty;
        var started = await _profiles.CaptureAsync(_game.Id, _profileId, TrimmedName, _failedRoles);
        if (!started.Ok)
        {
            SubmitError = _text.Message(started.Error, ErrorOperation.Snapshot);
            return;
        }

        EnterSaving();
        Track(started.Value.JobId);
    }

    /// <summary>Asks the running capture to stop.</summary>
    [RelayCommand(CanExecute = nameof(CanStopSave))]
    private async Task StopSaveAsync()
    {
        if (_jobId is not { } jobId || _stopRequested)
        {
            return;
        }

        _stopRequested = true;
        StopSaveCommand.NotifyCanExecuteChanged();
        await _jobs.CancelAsync(jobId);
    }

    private void EnterSaving()
    {
        _stopRequested = false;
        _milestone = 0;
        _finishingAnnounced = false;
        ResultCard = null;
        ProgressText = _text.Message("profiles.wizard.save.savingHint");
        CurrentFileText = string.Empty;
        FilesText = string.Empty;
        IsIndeterminate = true;
        ProgressPercent = 0;
        State = SaveState.Saving;
        Announcement = _text.Message("profiles.wizard.save.saving");
    }

    // The job may have changed before its id was known, so the latest snapshot is read once the id is.
    private void Track(Guid jobId)
    {
        _jobId = jobId;
        _revision = 0;
        if (_jobs.Get(jobId) is { } snapshot)
        {
            Apply(snapshot);
        }
    }

    private void OnJobChanged(object? sender, JobSnapshot snapshot) => _dispatcher.Post(() => Apply(snapshot));

    private void Apply(JobSnapshot snapshot)
    {
        if (IsClosed || snapshot.JobId != _jobId || snapshot.Revision <= _revision)
        {
            return;
        }

        _revision = snapshot.Revision;
        if (snapshot.Status == JobStatus.Running)
        {
            ShowProgress(snapshot.Progress);
        }
        else
        {
            Finish(snapshot);
        }
    }

    // The bytes lead whenever they are known, because git names no file while it stores a large one; the file count is the second line.
    private void ShowProgress(JobProgress progress)
    {
        var filesDone = progress.FilesDone;
        var filesTotal = progress.FilesTotal is > 0 ? progress.FilesTotal : null;
        var bytes = progress is { BytesTotal: > 0, BytesDone: not null } ? (Done: progress.BytesDone.Value, Total: progress.BytesTotal.Value) : ((long Done, long Total)?)null;

        double? share = bytes is { } counted
            ? (double)counted.Done / counted.Total
            : filesTotal is { } all ? (double)(filesDone ?? 0) / all : null;
        IsIndeterminate = share is null;
        ProgressPercent = share is { } fraction ? Math.Clamp(100.0 * fraction, 0, 100) : 0;

        if (bytes is { } stored)
        {
            ProgressText = progress.Finishing
                ? _text.Message("common.progress.finishing")
                : _text.Message("profiles.wizard.save.bytesProgress", ("done", _text.FileSize(stored.Done)), ("total", _text.FileSize(stored.Total)));

            // While the job wraps up, git has stopped naming files, so the last count and name would look stuck.
            FilesText = (filesDone, filesTotal) switch
            {
                _ when progress.Finishing => string.Empty,
                ({ } done, { } total) => _text.Message("profiles.wizard.save.filesDetail", ("done", _text.Number(done)), ("total", total), ("totalText", _text.Number(total))),
                ({ } done, null) when done > 0 => _text.Message("profiles.wizard.save.filesCopied", ("count", done), ("countText", _text.Number(done))),
                _ => string.Empty,
            };
        }
        else
        {
            (ProgressText, FilesText) = (filesDone, filesTotal) switch
            {
                ({ } done, { } total) => (_text.Message("profiles.wizard.save.filesProgress", ("done", _text.Number(done)), ("total", total), ("totalText", _text.Number(total))), string.Empty),
                ({ } done, null) => (_text.Counting(), done > 0 ? _text.Message("profiles.wizard.save.filesCopied", ("count", done), ("countText", _text.Number(done))) : string.Empty),
                _ => (_text.Message("profiles.wizard.save.savingHint"), string.Empty),
            };
        }

        CurrentFileText = progress.CurrentPath is { Length: > 0 } path && !progress.Finishing ? _text.Message("profiles.wizard.save.currentFile", ("currentFile", path)) : string.Empty;

        // Each quarter is announced once, so a screen reader hears progress without a flood of updates.
        var milestone = (int)(ProgressPercent / 25) * 25;
        if (milestone is 25 or 50 or 75 && milestone > _milestone)
        {
            _milestone = milestone;
            Announcement = _text.Message("profiles.wizard.save.milestone", ("percent", milestone));
        }

        // The wait at the end has no quarter of its own, so it is spoken once when it begins.
        if (progress.Finishing && !_finishingAnnounced)
        {
            _finishingAnnounced = true;
            Announcement = ProgressText;
        }
    }

    // Per-folder outcomes decide the ending: whole, half (some folder saved and another not), cancelled, or failed.
    private void Finish(JobSnapshot snapshot)
    {
        var outcomes = (snapshot.Result as CaptureResult)?.Folders ?? [];
        if (snapshot.Result is CaptureResult capture)
        {
            _profileId = capture.ProfileId;
        }

        foreach (var outcome in outcomes.Where(outcome => outcome.Ok))
        {
            _savedRoles.Add(outcome.Role);
        }

        var failures = outcomes.Where(outcome => !outcome.Ok).ToList();
        if (snapshot.Status == JobStatus.Succeeded && failures.Count == 0)
        {
            ResultCard = null;
            State = SaveState.Saved;
            Announcement = _text.Message("profiles.wizard.save.success");
            return;
        }

        if (Created && failures.Count > 0)
        {
            ResultCard = PartialCard(failures);
            State = SaveState.Partial;
        }
        else
        {
            var error = snapshot.Error ?? failures.FirstOrDefault()?.Error ?? ErrorInfo.From(new InvalidOperationException("The capture ended without saying why."));
            var tryAgain = new ErrorCardAction(_text.Message("profiles.wizard.save.tryAgain"), new AsyncRelayCommand(BeginSaveAsync), IsPrimary: true);
            ResultCard = new ErrorCardViewModel(error, _text, [tryAgain], operation: ErrorOperation.Snapshot);
            State = snapshot.Status == JobStatus.Cancelled ? SaveState.Cancelled : SaveState.Failed;
        }

        Announcement = ResultCard.Title;
    }

    // The "Only part of the snapshot was taken" card: what was saved, what was not and why, and a retry for just that part.
    private ErrorCardViewModel PartialCard(IReadOnlyList<FolderOutcome> failures)
    {
        _failedRoles = [.. failures.Select(failure => failure.Role)];
        var first = failures[0].Error!;
        var reason = _text.Message(first, ErrorOperation.Snapshot);
        var body = _text.Message("profiles.wizard.partial.body", ("state", PartialSnapshotText.State(_text, _savedRoles, _failedRoles)), ("reason", reason));
        var retryLabel = PartialSnapshotText.RetryLabel(_text, _failedRoles);
        var rows = new[] { RepoRole.Install, RepoRole.Mods, RepoRole.External }
            .Where(role => _savedRoles.Contains(role) || _failedRoles.Contains(role))
            .Select(role => OutcomeRow(role, _savedRoles.Contains(role)))
            .ToList();

        return ErrorCardViewModel.Custom(
            _text,
            Icons.Warning,
            ErrorTone.Warning,
            _text.Message("profiles.wizard.partial.title"),
            body,
            path: ErrorCardViewModel.PathFor(first, _text),
            supportText: first.Detail,
            actions: [new ErrorCardAction(retryLabel, new AsyncRelayCommand(RetryAsync), IsPrimary: true)],
            supplement: new SaveOutcomeListViewModel(rows));
    }

    private SaveOutcomeRow OutcomeRow(RepoRole role, bool saved)
    {
        var folder = _text.Message(role switch
        {
            RepoRole.Install => "profiles.wizard.preview.installTitle",
            RepoRole.Mods => "profiles.wizard.partial.folderMods",
            _ => "profiles.wizard.partial.folderSaves",
        });
        return saved
            ? new SaveOutcomeRow(Icons.CheckCircle, true, _text.Message("profiles.wizard.partial.rowSaved", ("folder", folder)))
            : new SaveOutcomeRow(Icons.Cancel, false, _text.Message("profiles.wizard.partial.rowNotSaved", ("folder", folder)));
    }
}
