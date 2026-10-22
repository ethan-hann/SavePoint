using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
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
    private readonly bool _canTryAgain;
    private readonly string? _logFolder;
    private readonly IFileRevealer? _revealer;
    private readonly IDialogs? _dialogs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCorruptFiles))]
    [NotifyPropertyChangedFor(nameof(IsChecking))]
    private StartupPhase _phase = StartupPhase.Checking;

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

    /// <param name="status">The startup Api the checks run against.</param>
    /// <param name="text">Formats the screens' copy.</param>
    /// <param name="canTryAgain">False when a failed check cannot succeed on a second try, as when the backend never started.</param>
    /// <param name="logFolder">The folder the diagnostic log is written to; the screen for a backend that never started shows it and offers to open it. Null when there is none.</param>
    /// <param name="revealer">Opens the log folder in the file manager.</param>
    /// <param name="dialogs">Asks before a recovery that begins again with nothing; without it that recovery goes ahead at once.</param>
    public StartupViewModel(
        IAppStatusApi status,
        UiFormatter text,
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
        _canTryAgain = canTryAgain;
    }

    /// <summary>Raised once, when the checks pass.</summary>
    public event EventHandler? Ready;

    /// <summary>The unreadable files, while <see cref="Phase"/> is <see cref="StartupPhase.CatalogCorrupt"/>.</summary>
    public ObservableCollection<CorruptFileViewModel> CorruptFiles { get; } = [];

    /// <summary>Where each recovered file went, so the user knows nothing was lost.</summary>
    public ObservableCollection<string> RecoveryNotes { get; } = [];

    /// <summary>True while the recovery list is the screen.</summary>
    public bool ShowCorruptFiles => Phase == StartupPhase.CatalogCorrupt;

    /// <summary>True while the checks run.</summary>
    public bool IsChecking => Phase == StartupPhase.Checking;

    /// <summary>True when <see cref="Card"/> is the screen.</summary>
    public bool HasCard => Card is not null;

    /// <summary>True when <see cref="RecoveryError"/> has something to say.</summary>
    public bool HasRecoveryError => RecoveryError is not null;

    /// <summary>True when <see cref="LogFolderError"/> has something to say.</summary>
    public bool HasLogFolderError => LogFolderError is not null;

    /// <summary>True when <see cref="StorageActionError"/> has something to say.</summary>
    public bool HasStorageActionError => StorageActionError is not null;

    /// <summary>Opens the log folder; says so in <see cref="LogFolderError"/> when the file manager cannot be started.</summary>
    public IRelayCommand OpenLogFolderCommand => field ??= new RelayCommand(OpenLogFolder);

    /// <summary>Runs the checks again.</summary>
    public IAsyncRelayCommand CheckAgainCommand => field ??= new AsyncRelayCommand(CheckAsync);

    /// <summary>Runs the checks and moves to the matching phase.</summary>
    public async Task CheckAsync()
    {
        CorruptFiles.Clear();
        Card = null;
        LogFolderError = null;
        StorageActionError = null;

        Phase = StartupPhase.Checking;
        var checkedStatus = await _status.CheckAsync();
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

        Phase = StartupPhase.Ready;
        Ready?.Invoke(this, EventArgs.Empty);
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
            detail: file.Error.Detail,
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

    private void OpenLogFolder() =>
        LogFolderError = _logFolder is { } folder && (_revealer?.Reveal(folder) ?? false) ? null : _text.Message("startup.logFolderFailed");

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
        StorageActionError = null;
        if (await _dialogs!.PickFolderAsync(new FolderPickRequest(_text.Message("startup.storage.pickTitle"), storage.Root)) is not { } folder)
        {
            return;
        }

        var used = await _status!.UseStorageFolderAsync(folder);
        if (!used.Ok)
        {
            StorageActionError = _text.Message(used.Error);
            return;
        }

        await CheckAsync();
    }

    // Beginning again leaves the old history behind, so it asks first and names the folder that is given up.
    private async Task StartWithEmptyStorageAsync(StorageStatus storage)
    {
        StorageActionError = null;
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
            StorageActionError = _text.Message(started.Error);
            return;
        }

        await CheckAsync();
    }
}
