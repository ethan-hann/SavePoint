using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
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

    /// <summary>The check itself failed.</summary>
    Failed,

    /// <summary>No services are connected to this build.</summary>
    Unavailable,
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
    private readonly IAppStatusApi? _status;
    private readonly UiFormatter _text;
    private readonly bool _canTryAgain;

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

    /// <param name="status">The startup Api; null when no services are connected, which shows the unavailable screen.</param>
    /// <param name="text">Formats the screens' copy.</param>
    /// <param name="canTryAgain">False when a failed check cannot succeed on a second try, as when the backend never started.</param>
    public StartupViewModel(IAppStatusApi? status, UiFormatter text, bool canTryAgain = true)
    {
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

    /// <summary>Runs the checks again.</summary>
    public IAsyncRelayCommand CheckAgainCommand => field ??= new AsyncRelayCommand(CheckAsync);

    /// <summary>Runs the checks and moves to the matching phase.</summary>
    public async Task CheckAsync()
    {
        CorruptFiles.Clear();
        Card = null;

        if (_status is null)
        {
            Phase = StartupPhase.Unavailable;
            Card = ErrorCardViewModel.Custom(
                _text,
                Icons.Info,
                ErrorTone.Neutral,
                _text.Message("startup.unavailable.title"),
                _text.Message("startup.unavailable.body"));
            return;
        }

        Phase = StartupPhase.Checking;
        var checkedStatus = await _status.CheckAsync();
        if (!checkedStatus.Ok)
        {
            Phase = StartupPhase.Failed;
            Card = _canTryAgain
                ? new ErrorCardViewModel(checkedStatus.Error, _text, [TryAgain()])
                : ErrorCardViewModel.Custom(
                    _text,
                    Icons.Warning,
                    ErrorTone.Warning,
                    _text.Message("startup.restartNeeded.title"),
                    _text.Message("startup.restartNeeded.body", ("reason", _text.Message(checkedStatus.Error))));
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

        Phase = StartupPhase.Ready;
        Ready?.Invoke(this, EventArgs.Empty);
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

    private ErrorCardViewModel CardFor(CorruptCatalogFile file)
    {
        var withPath = file.Error.Args.ContainsKey("path")
            ? file.Error
            : file.Error with { Args = new Dictionary<string, object?>(file.Error.Args) { ["path"] = file.Path } };
        var restore = new AsyncRelayCommand(() => RecoverAsync(file, CatalogRecovery.RestoreBackup), () => file.BackupPath is not null);
        var startEmpty = new AsyncRelayCommand(() => RecoverAsync(file, CatalogRecovery.StartEmpty));
        var backupNote = file.BackupPath is { } backup
            ? _text.Message("startup.corrupt.backupAt", ("path", backup))
            : _text.Message("startup.corrupt.noBackup");

        return new ErrorCardViewModel(
            withPath,
            _text,
            [
                new ErrorCardAction(_text.Message("startup.corrupt.restoreBackup"), restore, IsPrimary: true),
                new ErrorCardAction(_text.Message("startup.corrupt.startEmpty"), startEmpty),
            ],
            supplement: backupNote);
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
}
