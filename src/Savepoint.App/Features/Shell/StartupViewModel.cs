using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Settings;
using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Shell;

/// <summary>Where the startup checks stand.</summary>
public enum StartupPhase
{
    /// <summary>The checks have not finished.</summary>
    Checking,

    /// <summary>Everything is fine; the shell takes over.</summary>
    Ready,

    /// <summary>The bundled tool that saves and restores files cannot run (D18).</summary>
    GitUnavailable,

    /// <summary>A catalog file cannot be read; the recovery screen lists them (D13.2).</summary>
    CatalogCorrupt,

    /// <summary>The folder where SavePoint keeps its saved history cannot be reached; the screen offers three ways on (D37).</summary>
    StorageUnreachable,

    /// <summary>The check itself failed.</summary>
    Failed,
}

/// <summary>One unreadable catalog file on the recovery screen: an error card with the choices for it.</summary>
/// <param name="File">The file as the startup check reported it.</param>
/// <param name="Card">The card showing the file, why it is unreadable and what can be done.</param>
public sealed record CorruptFileViewModel(CorruptCatalogFile File, ErrorCardViewModel Card);

/// <summary>
/// The checks that run before any screen (D18): the bundled tool must run and no catalog file may be unreadable. A
/// failure replaces the app with its own screen naming the problem and what to do; nothing is overwritten or
/// deleted on the way. When everything is fine <see cref="Ready"/> is raised and the shell takes over.
/// </summary>
public sealed partial class StartupViewModel : ObservableObject
{
    private readonly IAppStatusApi _status;
    private readonly UiFormatter _text;
    private readonly IUiDispatcher _dispatcher;
    private readonly bool _canTryAgain;
    private readonly string? _logFolder;
    private readonly IFileRevealer? _revealer;
    private readonly IDialogs? _dialogs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCorruptFiles))]
    [NotifyPropertyChangedFor(nameof(IsChecking))]
    private StartupPhase _phase = StartupPhase.Checking;

    private bool _isFinishingEarlierCleanup;
    private bool _isWaitingForEarlierTask;
    private int _checkRun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCard))]
    private ErrorCardViewModel? _card;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecoveryError))]
    private string? _recoveryError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogFolderError))]
    private string? _logFolderError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStorageActionError))]
    private string? _storageActionError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStorageActionErrorPath))]
    private string? _storageActionErrorPath;

    /// <param name="status">The startup Api the checks run against.</param>
    /// <param name="text">Formats the screens' copy.</param>
    /// <param name="dispatcher">Brings the check's progress reports, which the backend raises on its own thread, to the UI thread.</param>
    /// <param name="canTryAgain">False when a failed check cannot succeed on a second try, as when the backend never started.</param>
    /// <param name="logFolder">The folder the diagnostic log is written to; the screen for a backend that never started shows it and offers to open it. Null when there is none.</param>
    /// <param name="revealer">Opens the log folder in the file manager.</param>
    /// <param name="dialogs">Asks before a recovery that begins again with nothing; without it that recovery goes ahead at once.</param>
    public StartupViewModel(
        IAppStatusApi status,
        UiFormatter text,
        IUiDispatcher dispatcher,
        bool canTryAgain = true,
        string? logFolder = null,
        IFileRevealer? revealer = null,
        IDialogs? dialogs = null)
    {
        _logFolder = logFolder;
        _revealer = revealer;
        _dialogs = dialogs;
        _status = status;
        _text = text;
        _dispatcher = dispatcher;
        _canTryAgain = canTryAgain;
    }

    /// <summary>Raised once, when the checks pass.</summary>
    public event EventHandler? Ready;

    /// <summary>
    /// What to tell the player once the shell is up when the snapshot folder lies where installing or removing SavePoint deletes it;
    /// null when the folder is safe or the checks have not passed.
    /// </summary>
    public ToastMessage? StorageNotice { get; private set; }

    /// <summary>The unreadable files, while <see cref="Phase"/> is <see cref="StartupPhase.CatalogCorrupt"/>.</summary>
    public ObservableCollection<CorruptFileViewModel> CorruptFiles { get; } = [];

    /// <summary>Where each recovered file went, so the user knows nothing was lost.</summary>
    public ObservableCollection<string> RecoveryNotes { get; } = [];

    /// <summary>True while the recovery list is the screen.</summary>
    public bool ShowCorruptFiles => Phase == StartupPhase.CatalogCorrupt;

    /// <summary>True while the checks run.</summary>
    public bool IsChecking => Phase == StartupPhase.Checking;

    /// <summary>True while the checks wait for a cleanup an earlier run of SavePoint began and did not finish; the screen then says why it is slow.</summary>
    public bool IsFinishingEarlierCleanup => _isFinishingEarlierCleanup;

    /// <summary>True while the checks wait for a task an earlier run of SavePoint started, and that was left running when it was stopped.</summary>
    public bool IsWaitingForEarlierTask => _isWaitingForEarlierTask;

    /// <summary>True when <see cref="Card"/> is the screen.</summary>
    public bool HasCard => Card is not null;

    /// <summary>True when <see cref="RecoveryError"/> has something to say.</summary>
    public bool HasRecoveryError => RecoveryError is not null;

    /// <summary>True when <see cref="LogFolderError"/> has something to say.</summary>
    public bool HasLogFolderError => LogFolderError is not null;

    /// <summary>True when <see cref="StorageActionError"/> has something to say.</summary>
    public bool HasStorageActionError => StorageActionError is not null;

    /// <summary>True when <see cref="StorageActionErrorPath"/> has a folder to show under the error.</summary>
    public bool HasStorageActionErrorPath => StorageActionErrorPath is not null;

    // The generated CheckAgainCommand runs the checks again.
    [RelayCommand]
    private Task CheckAgain() => CheckAsync();

    /// <summary>Runs the checks and moves to the matching phase.</summary>
    public async Task CheckAsync()
    {
        CorruptFiles.Clear();
        Card = null;
        LogFolderError = null;
        ClearStorageActionError();

        Phase = StartupPhase.Checking;
        SetFinishingEarlierCleanup(false);
        SetWaitingForEarlierTask(false);
        var run = ++_checkRun;
        var checkedStatus = await _status.CheckAsync(progress: new UiProgress(_dispatcher, step => ShowProgress(run, step)));
        SetFinishingEarlierCleanup(false);
        SetWaitingForEarlierTask(false);
        if (!checkedStatus.Ok)
        {
            Phase = StartupPhase.Failed;
            Card = _canTryAgain ? new ErrorCardViewModel(checkedStatus.Error, _text, [TryAgain()]) : RestartCard(checkedStatus.Error);
            return;
        }

        var status = checkedStatus.Value;
        if (!status.Git.Available)
        {
            Phase = StartupPhase.GitUnavailable;
            Card = new ErrorCardViewModel(GitError(status.Git), _text, [TryAgain()]);
            return;
        }

        if (status.Catalog.CorruptFiles.Count > 0)
        {
            foreach (var file in status.Catalog.CorruptFiles)
            {
                CorruptFiles.Add(new CorruptFileViewModel(file, CardFor(file)));
            }

            Phase = StartupPhase.CatalogCorrupt;
            return;
        }

        if (status.Storage is { Reachable: false } storage)
        {
            Phase = StartupPhase.StorageUnreachable;
            Card = StorageCard(storage);
            return;
        }

        StorageNotice = status.Storage is { InInstallerFolder: true }
            ? new ToastMessage(_text.Message("startup.storageUnsafe.title"), ToastKind.Warning, _text.Message("startup.storageUnsafe.body"))
            : null;
        Phase = StartupPhase.Ready;
        Ready?.Invoke(this, EventArgs.Empty);
    }

    private void ShowProgress(int run, StartupProgress progress)
    {
        // A report still queued when its check ended must not show a note for a wait that is over.
        if (run != _checkRun || Phase != StartupPhase.Checking)
        {
            return;
        }

        switch (progress)
        {
            case StartupProgress.FinishingEarlierCleanup:
                SetFinishingEarlierCleanup(true);
                break;
            case StartupProgress.WaitingForEarlierTask:
                SetWaitingForEarlierTask(true);
                break;
        }
    }

    private void SetWaitingForEarlierTask(bool value)
    {
        if (_isWaitingForEarlierTask != value)
        {
            _isWaitingForEarlierTask = value;
            OnPropertyChanged(nameof(IsWaitingForEarlierTask));
        }
    }

    private void SetFinishingEarlierCleanup(bool value)
    {
        if (_isFinishingEarlierCleanup != value)
        {
            _isFinishingEarlierCleanup = value;
            OnPropertyChanged(nameof(IsFinishingEarlierCleanup));
        }
    }

    // The backend reports from its own thread; the screen's state changes where the screen runs.
    private sealed class UiProgress(IUiDispatcher dispatcher, Action<StartupProgress> show) : IProgress<StartupProgress>
    {
        public void Report(StartupProgress value) => dispatcher.Post(() => show(value));
    }

    // The log folder is named here because this is the one screen a person can only leave by closing the app, and what they report from it is the log.
    private ErrorCardViewModel RestartCard(ErrorInfo error)
    {
        var reason = ("reason", (object?)_text.Message(error));
        if (_logFolder is null)
        {
            return ErrorCardViewModel.Custom(_text, Icons.Warning, ErrorTone.Warning, _text.Message("startup.restartNeeded.title"), _text.Message("startup.restartNeeded.body", reason));
        }

        return ErrorCardViewModel.Custom(
            _text,
            Icons.Warning,
            ErrorTone.Warning,
            _text.Message("startup.restartNeeded.title"),
            _text.Message("startup.restartNeeded.bodyWithLog", reason),
            path: _logFolder,
            actions: [new ErrorCardAction(_text.Message("startup.openLogFolder"), OpenLogFolderCommand)]);
    }

    private ErrorCardAction TryAgain() => new(_text.Message("startup.tryAgain"), CheckAgainCommand, IsPrimary: true);

    // The card shows the path SavePoint tried, whether or not the error carried it.
    private static ErrorInfo GitError(GitStatus git)
    {
        var error = git.Error ?? new ErrorInfo("error.git.unavailable", new Dictionary<string, object?>(), string.Empty);
        return error.Args.ContainsKey("path")
            ? error
            : error with { Args = new Dictionary<string, object?>(error.Args) { ["path"] = git.Path } };
    }

    // Which of SavePoint's files this is, as the word the copy selects on.
    private static string KindOf(CorruptCatalogFile file) => System.IO.Path.GetFileName(file.Path.Replace('\\', '/')).ToLowerInvariant() switch
    {
        "games.json" => "games",
        "profiles.json" => "profiles",
        "settings.json" => "settings",
        _ => "other",
    };

    private ErrorCardViewModel CardFor(CorruptCatalogFile file)
    {
        var kind = KindOf(file);
        var restore = new AsyncRelayCommand(() => RecoverAsync(file, CatalogRecovery.RestoreBackup), () => file.BackupPath is not null);
        var startEmpty = new AsyncRelayCommand(() => StartEmptyAsync(file, kind));
        var backupNote = file.BackupPath is { } backup
            ? _text.Message("startup.corrupt.backupAt", ("path", backup))
            : _text.Message("startup.corrupt.noBackup");
        var presentation = ErrorPresentation.For(file.Error.MessageId);

        // Each file says which it is and what its two buttons do, so nobody has to rename files by hand to follow the card.
        return ErrorCardViewModel.Custom(
            _text,
            presentation.Icon,
            presentation.Tone,
            _text.Message("startup.corrupt.title", ("kind", kind)),
            _text.Message("startup.corrupt.body", ("kind", kind)),
            path: file.Path,
            supportText: file.Error.Detail,
            actions:
            [
                new ErrorCardAction(_text.Message("startup.corrupt.restoreBackup"), restore, IsPrimary: true),
                new ErrorCardAction(_text.Message("startup.corrupt.startEmpty"), startEmpty),
            ],
            supplement: backupNote);
    }

    // A folder that is gone for good is not a dead end: the history may be on a drive that has a new letter, or SavePoint can start over.
    private ErrorCardViewModel StorageCard(StorageStatus storage)
    {
        var error = storage.Error ?? new ErrorInfo("error.storage.unreachable", new Dictionary<string, object?> { ["path"] = storage.Root }, string.Empty);
        List<ErrorCardAction> actions = [TryAgain()];
        if (_dialogs is not null)
        {
            actions.Add(new ErrorCardAction(_text.Message("startup.storage.findFolder"), new AsyncRelayCommand(() => FindStorageFolderAsync(storage))));
            actions.Add(new ErrorCardAction(_text.Message("startup.storage.startEmpty"), new AsyncRelayCommand(() => StartWithEmptyStorageAsync(storage))));
        }

        return new ErrorCardViewModel(error, _text, actions, supplement: _text.Message("startup.storage.choices"));
    }

    // The folder an error names goes under its message, as on the error card, because the copy leaves paths out and points to it ("shown below").
    private void ShowStorageActionError(ErrorInfo error)
    {
        StorageActionErrorPath = ErrorCardViewModel.PathFor(error, _text);
        StorageActionError = _text.Message(error);
    }

    private void ClearStorageActionError()
    {
        StorageActionError = null;
        StorageActionErrorPath = null;
    }

    // The generated OpenLogFolderCommand opens the log folder; it says so in LogFolderError when the file manager cannot be started.
    [RelayCommand]
    private async Task OpenLogFolderAsync()
    {
        var opened = _logFolder is { } folder && _revealer is not null && await _revealer.RevealAsync(folder);
        LogFolderError = opened ? null : _text.Message("startup.logFolderFailed");
    }

    // Beginning again with nothing empties a list the player may have spent years on, so it asks, and says the old file is kept.
    private async Task StartEmptyAsync(CorruptCatalogFile file, string kind)
    {
        if (_dialogs is not null)
        {
            var confirmed = await _dialogs.ConfirmAsync(new ConfirmRequest(
                _text.Message("startup.corrupt.confirm.title"),
                _text.Message("startup.corrupt.confirm.message", ("kind", kind)),
                _text.Message("startup.corrupt.startEmpty"),
                Destructive: true));
            if (!confirmed)
            {
                return;
            }
        }

        await RecoverAsync(file, CatalogRecovery.StartEmpty);
    }

    private async Task RecoverAsync(CorruptCatalogFile file, CatalogRecovery recovery)
    {
        var recovered = await _status!.RecoverCatalogFileAsync(file.Path, recovery);
        if (!recovered.Ok)
        {
            RecoveryError = _text.Message(recovered.Error);
            return;
        }

        RecoveryError = null;
        RecoveryNotes.Add(_text.Message("startup.corrupt.movedTo", ("path", recovered.Value)));
        await CheckAsync();
    }

    // Pointing at where the history is now changes only a setting, so it needs no confirmation; the Api checks the folder holds it.
    private async Task FindStorageFolderAsync(StorageStatus storage)
    {
        ClearStorageActionError();
        if (await _dialogs!.PickFolderAsync(new FolderPickRequest(_text.Message("startup.storage.pickTitle"), storage.Root)) is not { } folder)
        {
            return;
        }

        var used = await _status!.UseStorageFolderAsync(folder);
        if (!used.Ok)
        {
            ShowStorageActionError(used.Error);
            return;
        }

        await CheckAsync();
    }

    // Beginning again leaves the old history behind, so it asks first and names the folder that is given up.
    private async Task StartWithEmptyStorageAsync(StorageStatus storage)
    {
        ClearStorageActionError();
        var confirmed = await _dialogs!.ConfirmAsync(new ConfirmRequest(
            _text.Message("startup.storage.confirm.title"),
            _text.Message("startup.storage.confirm.message", ("path", storage.Root)),
            _text.Message("startup.storage.startEmpty"),
            Destructive: true));
        if (!confirmed)
        {
            return;
        }

        var started = await _status!.StartWithEmptyStorageAsync();
        if (!started.Ok)
        {
            ShowStorageActionError(started.Error);
            return;
        }

        await CheckAsync();
    }
}
