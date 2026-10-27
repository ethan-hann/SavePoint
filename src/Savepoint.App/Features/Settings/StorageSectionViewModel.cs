using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Settings;

/// <summary>
/// The "Where SavePoint keeps your snapshots" section: the storage folder with Change, how much of the drive
/// SavePoint uses, and the Free up space row. Changing the folder reads what the move would do, asks once naming
/// the size and the destination, then follows the move as a job with progress and Cancel. Both long jobs are followed
/// through the jobs Api, so the section is right when the screen is opened while one is already running.
/// </summary>
public sealed partial class StorageSectionViewModel : ObservableObject
{
    private readonly SettingsDependencies _deps;
    private readonly SettingsStore _store;
    private readonly Dictionary<Guid, long> _revisions = [];
    private Guid? _optimizeJob;
    private Guid? _relocationJob;
    private readonly ProgressMilestones _milestones = new();
    private readonly FreeUpSizeEstimate _size;
    private int _previewVersion;

    /// <summary>The folder SavePoint keeps its data in, shown in monospace.</summary>
    [ObservableProperty]
    private string _storagePath = string.Empty;

    /// <summary>True while the storage folder lies where installing or removing SavePoint would delete it.</summary>
    [ObservableProperty]
    private bool _isStorageUnsafe;

    /// <summary>Tells the player why the folder is unsafe and what to do; empty while it is not.</summary>
    [ObservableProperty]
    private string _unsafeNote = string.Empty;

    /// <summary>True when the usage bar and its two captions have numbers to show.</summary>
    [ObservableProperty]
    private bool _hasSummary;

    /// <summary>"79.3 GB used by 3 games".</summary>
    [ObservableProperty]
    private string _usedText = string.Empty;

    /// <summary>"131 GB free".</summary>
    [ObservableProperty]
    private string _freeText = string.Empty;

    /// <summary>How much of the drive SavePoint uses, from 0 to 1.</summary>
    [ObservableProperty]
    private double _usedFraction;

    /// <summary>What Free up space does and when it last ran.</summary>
    [ObservableProperty]
    private string _optimizeDescription = string.Empty;

    /// <summary>What Free up space would delete for good right now, or empty while that cannot be read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOptimizePreview))]
    private string _optimizePreview = string.Empty;

    /// <summary>True when <see cref="OptimizePreview"/> has something to say.</summary>
    public bool HasOptimizePreview => OptimizePreview.Length > 0;

    /// <summary>True while the optimization of every game runs; the Free up space button gives way to Cancel.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunNowCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelOptimizeCommand))]
    private bool _isOptimizing;

    /// <summary>Why Cancel is not offered while the optimization runs: it has deleted snapshots and cannot be stopped. Empty otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStopNote))]
    [NotifyCanExecuteChangedFor(nameof(CancelOptimizeCommand))]
    private string _stopNote = string.Empty;

    /// <summary>True when <see cref="StopNote"/> has something to say.</summary>
    public bool HasStopNote => StopNote.Length > 0;

    /// <summary>True while the storage move runs.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelRelocationCommand))]
    private bool _isRelocating;

    /// <summary>How far the move is, from 0 to 1; null while it cannot be told, which the bar shows as indeterminate.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RelocationValue))]
    [NotifyPropertyChangedFor(nameof(IsRelocationIndeterminate))]
    private double? _relocationFraction;

    /// <summary>What the hidden live region says: the start of a move and each quarter of it, once.</summary>
    [ObservableProperty]
    private string _announcement = string.Empty;

    /// <summary>"19.8 GB of 79.2 GB", or empty while the totals are not known.</summary>
    [ObservableProperty]
    private string _relocationDetail = string.Empty;

    /// <summary>Why the last attempt to move or optimize did not go ahead or failed; null otherwise.</summary>
    [ObservableProperty]
    private ErrorCardViewModel? _problem;

    /// <summary>What a move that worked could not remove from the old folder, so the user can delete it by hand; null otherwise.</summary>
    [ObservableProperty]
    private ErrorCardViewModel? _leftBehind;

    internal StorageSectionViewModel(SettingsDependencies dependencies, SettingsStore store)
    {
        _deps = dependencies;
        _store = store;
        _size = new FreeUpSizeEstimate(dependencies.Clock);
        store.Changed += (_, settings) => Show(settings);
    }

    /// <summary>The bar's value: <see cref="RelocationFraction"/>, or 0 while that is not known.</summary>
    public double RelocationValue => RelocationFraction ?? 0;

    /// <summary>True while the move's size is not known yet, so the bar sweeps instead of filling.</summary>
    public bool IsRelocationIndeterminate => RelocationFraction is null;

    private bool CanChange => !IsRelocating;

    private bool CanRunNow => !IsOptimizing;

    private bool CanCancelOptimize => IsOptimizing && !HasStopNote;

    private bool CanCancelRelocation => IsRelocating;

    // Reads the numbers and takes up any long job that is already running, once the settings are in.
    internal async Task LoadAsync(IReadOnlyList<JobSnapshot> jobs)
    {
        foreach (var job in jobs)
        {
            Handle(job, seeding: true);
        }

        await RefreshSummaryAsync();
        await RefreshPreviewAsync();
    }

    // The line under Free up space is a nicety: when the count cannot be read it goes away and the button still works. The count shows
    // at once; the size joins it when the backend has worked it out, and is simply never said when it cannot be.
    private async Task RefreshPreviewAsync()
    {
        var shown = ++_previewVersion;
        var read = await _deps.Settings.GetFreeUpSpacePreviewAsync();
        if (shown != _previewVersion)
        {
            return;
        }

        if (!read.Ok)
        {
            OptimizePreview = string.Empty;
            _size.Cancel();
            return;
        }

        var removed = read.Value.RecentlyRemovedSnapshots;
        if (removed <= 0)
        {
            OptimizePreview = _deps.Text.Message("settings.storage.optimizePreview.none");
            _size.Cancel();
            return;
        }

        OptimizePreview = _deps.Text.Message("settings.storage.optimizePreview.some", ("count", removed));

        // Not awaited: the screen is ready now, and the size follows whenever the backend has it.
        _ = ShowSizeAsync(read.Value.Token, removed, shown);
    }

    private async Task ShowSizeAsync(FreeUpSpaceToken token, int removed, int shown)
    {
        var size = await _size.Start(token, _deps.Settings.EstimateFreeUpSpaceAsync);
        if (size is { } bytes && shown == _previewVersion)
        {
            OptimizePreview = _deps.Text.Message("settings.storage.optimizePreview.someWithSize", ("count", removed), ("size", _deps.Text.FileSize(bytes)));
        }
    }

    // Takes a job change, on the UI thread. A snapshot that is not newer than the one held for the job is dropped.
    internal void Handle(JobSnapshot job, bool seeding = false)
    {
        if (job.Kind == JobKind.Compact && job.GameId is null)
        {
            if (IsNewer(job))
            {
                HandleOptimization(job, seeding);
            }
        }
        else if (job.Kind == JobKind.RelocateStorage && IsNewer(job))
        {
            HandleRelocation(job, seeding);
        }
    }

    private bool IsNewer(JobSnapshot job)
    {
        if (_revisions.TryGetValue(job.JobId, out var held) && job.Revision <= held)
        {
            return false;
        }

        _revisions[job.JobId] = job.Revision;
        return true;
    }

    private void HandleOptimization(JobSnapshot job, bool seeding)
    {
        if (job.Status == JobStatus.Running)
        {
            _optimizeJob = job.JobId;
            IsOptimizing = true;

            // A run that has deleted the snapshots the player chose to lose is past stopping, and says so where Cancel was.
            StopNote = job.Cancellable ? string.Empty : _deps.Text.Message("activity.stop.compact");
            return;
        }

        // A job that ended before the screen looked is history, not news.
        if (_optimizeJob == job.JobId)
        {
            _optimizeJob = null;
            IsOptimizing = false;
            StopNote = string.Empty;
            if (!seeding)
            {
                _ = RefreshAfterJobAsync(tellShell: false);
            }
        }
    }

    private void HandleRelocation(JobSnapshot job, bool seeding)
    {
        if (job.Status == JobStatus.Running)
        {
            var started = _relocationJob != job.JobId && !seeding;
            _relocationJob = job.JobId;
            IsRelocating = true;
            if (started)
            {
                // Said once the panel with the live region is showing, or nobody hears it.
                _milestones.Reset();
                Announcement = _deps.Text.Message("settings.storage.moving");
            }

            ShowProgress(job.Progress);
            return;
        }

        if (_relocationJob != job.JobId)
        {
            return;
        }

        _relocationJob = null;
        IsRelocating = false;
        ShowProgress(JobProgress.None);
        if (seeding)
        {
            return;
        }

        switch (job.Status)
        {
            case JobStatus.Succeeded:
                // The settings now point at the new folder, and the shell's first-run note names it.
                _ = RefreshAfterJobAsync(tellShell: true);
                break;
            case JobStatus.Cancelled:
                break;
            case JobStatus.Partial:
                // The move worked and the settings follow it; only some old copies stayed, and the user is told which.
                _ = RefreshAfterJobAsync(tellShell: true);
                LeftBehind = LeftBehindCard(job.Result);
                break;
            default:
                Problem = Card(job.Error ?? Unknown());
                break;
        }
    }

    private ErrorCardViewModel? LeftBehindCard(JobResult? result) =>
        result is StorageRelocationResult { NotRemoved.Count: > 0 } moved
            ? ErrorCardViewModel.Custom(
                _deps.Text,
                Icons.Warning,
                ErrorTone.Warning,
                _deps.Text.Message("settings.storage.leftBehindTitle"),
                _deps.Text.Message("settings.storage.leftBehindBody"),
                path: string.Join(Environment.NewLine, SnapshotFolderNames.Plain(moved.NotRemoved.Select(failure => failure.Path))))
            : null;

    private void ShowProgress(JobProgress progress)
    {
        var done = progress.BytesDone;
        var total = progress.BytesTotal;
        var known = done is not null && total is > 0;
        RelocationFraction = known ? Math.Clamp((double)done!.Value / total!.Value, 0, 1) : null;
        RelocationDetail = known
            ? _deps.Text.Message("settings.storage.movingDetail", ("done", _deps.Text.FileSize(done!.Value)), ("total", _deps.Text.FileSize(total!.Value)))
            : string.Empty;

        if (RelocationFraction is { } fraction && _milestones.Crossed((int)Math.Floor(fraction * 100)) is { } crossed)
        {
            Announcement = _deps.Text.Message("settings.storage.announcePercent", ("percent", crossed));
        }
    }

    private async Task RefreshAfterJobAsync(bool tellShell)
    {
        await _store.ReadAsync(tellShell);
        await RefreshSummaryAsync();
        await RefreshPreviewAsync();
    }

    private async Task RefreshSummaryAsync()
    {
        var read = await _deps.Settings.GetStorageSummaryAsync();

        // The numbers are a nicety: without them the bar goes away and everything else works.
        HasSummary = read.Ok;
        if (!read.Ok)
        {
            return;
        }

        var summary = read.Value;
        IsStorageUnsafe = summary.InInstallerFolder;
        UnsafeNote = summary.InInstallerFolder ? _deps.Text.Message("settings.storage.unsafeNote") : string.Empty;
        UsedText = _deps.Text.Message("settings.storage.used", ("size", _deps.Text.FileSize(summary.BytesUsed)), ("count", summary.Games.Count));
        FreeText = _deps.Text.Message("settings.storage.free", ("size", _deps.Text.FileSize(summary.VolumeFreeBytes)));
        UsedFraction = summary.VolumeTotalBytes > 0 ? Math.Clamp((double)summary.BytesUsed / summary.VolumeTotalBytes, 0, 1) : 0;
    }

    private void Show(AppSettings settings)
    {
        StoragePath = settings.StorageRoot;
        OptimizeDescription = settings.LastOptimizedAt is { } last
            ? _deps.Text.Message("settings.storage.optimizeLastRun", ("date", _deps.Text.ShortDate(last)))
            : _deps.Text.Message("settings.storage.optimizeNeverRun");
    }

    // The generated ChangeCommand picks a new folder, shows what the move would do, asks, and starts it.
    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task ChangeAsync()
    {
        var picked = await _deps.Dialogs.PickFolderAsync(new FolderPickRequest(_deps.Text.Message("settings.storage.pickTitle"), StoragePath));
        Problem = null;
        LeftBehind = null;
        if (picked is null)
        {
            return;
        }

        var read = await _deps.Settings.GetRelocationPreviewAsync(picked);
        if (!read.Ok)
        {
            Problem = Card(read.Error);
            return;
        }

        var preview = read.Value;
        if (preview.BlockedBy is { } blocked)
        {
            Problem = Card(blocked);
            return;
        }

        var confirmed = await _deps.Dialogs.ConfirmAsync(new ConfirmRequest(
            _deps.Text.Message("settings.storage.moveTitle"),
            _deps.Text.Message(
                "settings.storage.moveMessage",
                ("size", _deps.Text.FileSize(preview.BytesToCopy)),
                ("count", preview.FileCount),
                ("files", _deps.Text.Number(preview.FileCount)),
                ("destination", preview.NewRoot)),
            _deps.Text.Message("settings.storage.moveConfirm")));
        if (!confirmed)
        {
            return;
        }

        var started = await _deps.Settings.RelocateStorageAsync(preview.NewRoot);
        if (!started.Ok)
        {
            Problem = Card(started.Error);
            return;
        }

        // The job may already have reported before its id was known here.
        if (_deps.Jobs.Get(started.Value.JobId) is { } job)
        {
            Handle(job);
        }
    }

    // The generated RunNowCommand optimizes the storage of every game.
    [RelayCommand(CanExecute = nameof(CanRunNow))]
    private async Task RunNowAsync()
    {
        Problem = null;

        // Freeing up space deletes recently removed snapshots for good, so when there are some the player is told how many and asked first.
        var preview = await _deps.Settings.GetFreeUpSpacePreviewAsync();
        if (!preview.Ok)
        {
            _deps.Toasts.Show(new ToastMessage(_deps.Text.Message(preview.Error, ErrorOperation.FreeUpSpace), ToastKind.Error));
            return;
        }

        if (preview.Value.RecentlyRemovedSnapshots is > 0 and var removed)
        {
            // The size is in the question when it is ready, or comes within a moment; otherwise the question does not wait for it.
            var size = await _size.WaitAsync(_size.Start(preview.Value.Token, _deps.Settings.EstimateFreeUpSpaceAsync));
            var message = size is { } bytes
                ? _deps.Text.Message("settings.storage.optimizeConfirm.messageWithSize", ("count", removed), ("size", _deps.Text.FileSize(bytes)))
                : _deps.Text.Message("settings.storage.optimizeConfirm.message", ("count", removed));
            if (!await _deps.Dialogs.ConfirmAsync(new ConfirmRequest(
                _deps.Text.Message("settings.storage.optimizeConfirm.title"),
                message,
                _deps.Text.Message("settings.storage.optimizeConfirm.confirm", ("count", removed)),
                Destructive: true)))
            {
                return;
            }
        }

        // The estimate reads the store the run is about to repack and prune, so it ends first; a late answer then says nothing.
        _size.Cancel();

        // The run deletes the records the player was told about and none written since.
        var started = await _deps.Settings.OptimizeAllStorageAsync(preview.Value.Token);
        if (!started.Ok)
        {
            _deps.Toasts.Show(new ToastMessage(_deps.Text.Message(started.Error, ErrorOperation.FreeUpSpace), ToastKind.Error));
            return;
        }

        if (_deps.Jobs.Get(started.Value.JobId) is { } job)
        {
            Handle(job);
        }
    }

    // The generated CancelOptimizeCommand cancels the optimization that is running.
    [RelayCommand(CanExecute = nameof(CanCancelOptimize))]
    private Task CancelOptimize() => CancelAsync(_optimizeJob);

    // The generated CancelRelocationCommand cancels the move that is running.
    [RelayCommand(CanExecute = nameof(CanCancelRelocation))]
    private Task CancelRelocation() => CancelAsync(_relocationJob);

    private async Task CancelAsync(Guid? jobId)
    {
        if (jobId is { } id)
        {
            await _deps.Jobs.CancelAsync(id);
        }
    }

    // Every message here ends "then try again", so the card carries the way to do it.
    private ErrorCardViewModel Card(ErrorInfo error) =>
        new(error, _deps.Text, [new ErrorCardAction(_deps.Text.Message("settings.storage.chooseAnother"), ChangeCommand, IsPrimary: true)], operation: ErrorOperation.MoveSnapshots);

    private static ErrorInfo Unknown() => new("error.unknown", new Dictionary<string, object?>(), string.Empty);
}
