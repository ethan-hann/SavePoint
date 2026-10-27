using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Preferences;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Help;
using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Launch;

namespace Savepoint.App.Features.Launch;

/// <summary>
/// The dialog that opens when the game exits: how long the session was, how many new files there are, and
/// radio cards to keep it as a new point, update the latest one, or leave it. A session that started from an older
/// point offers a new profile, continuing the profile from there, or leaving it instead (D14). It answers with the
/// completion to apply; Escape and the "Not now" card answer with <see cref="SessionCompletion.Skip"/>.
/// </summary>
public sealed partial class PostPlayDialogViewModel : DialogViewModel<SessionCompletion>
{
    private readonly UiFormatter _text;
    private readonly IDialogs _dialogs;
    private readonly string _profileName;
    private readonly PostPlayOptions _options;
    private readonly string _defaultPointName;
    private readonly SettingsWriter _settingsWriter;
    private readonly Action? _editGame;
    private readonly IGamesApi _games;
    private readonly ToastHostViewModel _toasts;
    private readonly string? _gameId;
    private readonly IReadOnlyList<string>? _leaveOutRules;
    private bool _explainReplace;
    private bool _leaveOutDone;

    /// <summary>What happened when the player asked to leave the files out, or why it did not work; empty before that.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLeaveOutResult))]
    private string _leaveOutResult = string.Empty;

    /// <summary>The card the player has selected; the main button acts on it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryLabel))]
    private PostPlayChoiceViewModel _selectedChoice;

    /// <param name="exited">The session's exit: its length, new files and the choices it allows.</param>
    /// <param name="profileName">The profile the session was played from.</param>
    /// <param name="deps">The Api and formatting the dialog uses.</param>
    /// <param name="explainOlderChoice">True the first time a player meets the older-snapshot choice: "Which should I pick?" opens by itself, and the player's settings remember it was shown.</param>
    /// <param name="explainReplace">True the first time a player has not yet seen the Replace card's explanation: it opens by itself when the card is first selected, and the player's settings remember it was shown.</param>
    /// <param name="editGame">Opens Edit game for the session's game; the hint about a program that was never seen offers it. Null when the host has no editor to open.</param>
    /// <param name="gameId">The session's game; the offer to leave run-time files out needs it. Null offers nothing.</param>
    public PostPlayDialogViewModel(SessionState.Exited exited, string profileName, LaunchDependencies deps, bool explainOlderChoice = false, bool explainReplace = false, Action? editGame = null, string? gameId = null)
        : base(TitleFor(exited, deps.Text))
    {
        _text = deps.Text;
        _editGame = editGame;
        NotSeenHint = exited.EndKind switch
        {
            SessionEndKind.NamedProcessNeverAppeared => _text.Message("launch.postPlay.notSeen.hint"),
            SessionEndKind.NamedProcessNeverMatched => _text.Message("launch.postPlay.neverMatched.hint"),
            SessionEndKind.ProcessListUnreadable => _text.Message("launch.postPlay.unreadable.hint"),
            SessionEndKind.ProgramFailedAtStart => _text.Message("launch.postPlay.failedAtStart.hint", ("code", ExitCodeText(exited))),
            _ => string.Empty,
        };

        // An unreadable process list is not the name's fault, so there is nothing in Edit game to point at.
        EditGameLabel = exited.EndKind is SessionEndKind.NamedProcessNeverAppeared or SessionEndKind.NamedProcessNeverMatched or SessionEndKind.ProgramFailedAtStart
            ? _text.Message("launch.postPlay.notSeen.editGame")
            : string.Empty;

        _dialogs = deps.Dialogs;
        _profileName = profileName;
        _options = exited.Options;
        var today = _text.NameDate(deps.Clock.GetUtcNow());
        _defaultPointName = _text.Message("launch.postPlay.defaultName", ("date", today));

        Body = _text.Message("launch.postPlay.body", ("count", exited.NewFileCount));
        _games = deps.Games;
        _toasts = deps.Toasts;
        _gameId = gameId;
        _leaveOutRules = exited.LeaveOutRules;
        InstallNote = exited.ChangedInstallFileCount switch
        {
            <= 0 => string.Empty,
            var count when _leaveOutRules is not null => _text.Message("launch.postPlay.runTimeOnly", ("count", count)),
            var count => _text.Message("launch.postPlay.installChanged", ("count", count)),
        };
        ModsNote = exited.ChangedModsFileCount > 0 ? _text.Message("launch.postPlay.modsChanged", ("count", exited.ChangedModsFileCount)) : string.Empty;
        var older = _options.Choices.Contains(CompletionChoice.ForkNewProfile) || _options.Choices.Contains(CompletionChoice.MoveForward);
        OlderNote = older ? _text.Message("launch.postPlay.olderNote", ("profileName", profileName)) : string.Empty;
        OlderChoiceHelp = older ? ContextualHelp.OlderChoice(_text, profileName) : null;
        _settingsWriter = deps.SettingsWriter;
        _explainReplace = explainReplace;

        // The first time a player meets this choice, the explanation is already open: they learn it before they pick, then it stays out of the way.
        if (explainOlderChoice && OlderChoiceHelp is not null)
        {
            OlderChoiceHelp.IsOpen = true;
            Remember(settings => settings with { OlderChoiceHelpSeen = true });
        }

        Choices = BuildChoices(today);
        _selectedChoice = Choices.FirstOrDefault(choice => choice.Kind == _options.Default) ?? Choices[0];
        _selectedChoice.IsSelected = true;
    }

    /// <summary>"3 files in your saves folder are new or changed since you started. What should SavePoint do with them?"</summary>
    public string Body { get; }

    /// <summary>A line about the game's own files having changed while it ran; empty when they did not.</summary>
    public string InstallNote { get; }

    /// <summary>A line about files in the mods folder having changed while the game ran; empty when they did not or the game has no mods folder.</summary>
    public string ModsNote { get; }

    /// <summary>True when <see cref="ModsNote"/> has text.</summary>
    public bool HasModsNote => ModsNote.Length > 0;

    /// <summary>Why the end of the session is uncertain (the game was not seen, its name never matched, or the programs could not be checked); empty when it was seen running.</summary>
    public string NotSeenHint { get; }

    /// <summary>True when <see cref="NotSeenHint"/> has text.</summary>
    public bool HasNotSeenHint => NotSeenHint.Length > 0;

    /// <summary>The label of the button that opens Edit game.</summary>
    public string EditGameLabel { get; }

    /// <summary>True when the button that opens Edit game shows.</summary>
    public bool HasEditGame => EditGameLabel.Length > 0;

    // The saves are left for later (as Not now does) so the player can fix the name first; the editor opens once the dialog is gone.
    [RelayCommand]
    private void EditGame()
    {
        Close(new SessionCompletion.Skip());
        _editGame?.Invoke();
    }

    // A game that was never seen running, or whose process list could not be read, has no play time to report; the wait is not time played.
    private static string TitleFor(SessionState.Exited exited, UiFormatter text) => exited.EndKind switch
    {
        SessionEndKind.NamedProcessNeverAppeared => text.Message("launch.postPlay.notSeen.title"),
        SessionEndKind.ProcessListUnreadable => text.Message("launch.postPlay.unreadable.title"),
        SessionEndKind.ProgramFailedAtStart => text.Message("launch.postPlay.failedAtStart.title"),
        _ => text.Message("launch.postPlay.title", ("duration", text.Duration(exited.PlayDuration))),
    };

    /// <summary>The exit code as the hint and toast print it, with no digit grouping.</summary>
    /// <param name="exited">The session's exit.</param>
    internal static string ExitCodeText(SessionState.Exited exited) =>
        (exited.ProgramExitCode ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>True when <see cref="InstallNote"/> has text.</summary>
    public bool HasInstallNote => InstallNote.Length > 0;

    /// <summary>The label of the button that leaves the changed logs out from now on; empty when the dialog offers none.</summary>
    public string LeaveOutLabel => _leaveOutRules is null || _gameId is null ? string.Empty : _text.Message("launch.postPlay.leaveOut");

    /// <summary>True while the button that leaves the changed logs out shows: the files were all logs or crash dumps and nothing has been done about them yet.</summary>
    public bool HasLeaveOut => LeaveOutLabel.Length > 0 && !_leaveOutDone;

    /// <summary>True when <see cref="LeaveOutResult"/> has text.</summary>
    public bool HasLeaveOutResult => LeaveOutResult.Length > 0;

    // Adds the rules to the game as Edit game would. Only the game's rules change: the dialog stays open and the session's own choice is untouched.
    [RelayCommand]
    private async Task LeaveOutAsync()
    {
        if (_leaveOutRules is null || _gameId is null || _leaveOutDone)
        {
            return;
        }

        var listed = await _games.ListAsync();
        if (!listed.Ok)
        {
            Fail(listed.Error);
            return;
        }

        var game = listed.Value.FirstOrDefault(candidate => candidate.Id == _gameId);
        if (game is null)
        {
            Fail(ErrorInfo.From(new GameNotFoundException(_gameId)));
            return;
        }

        // The rules go on the game's own folder only, so they can never leave out a save. One the game already has for both folders says the same.
        var added = RunTimeOutput.Merge(game.InstallIgnorePaths, _leaveOutRules.Where(rule => !game.IgnorePaths.Contains(rule)));
        if (added.Count != game.InstallIgnorePaths.Count)
        {
            var updated = await _games.UpdateAsync(game with { InstallIgnorePaths = added });
            if (!updated.Ok)
            {
                Fail(updated.Error);
                return;
            }
        }

        _leaveOutDone = true;
        OnPropertyChanged(nameof(HasLeaveOut));
        LeaveOutResult = _text.Message("launch.postPlay.leaveOutDone");
    }

    // The dialog may be gone by the time this ends; a failure then is a message, not a line nobody will read.
    private void Fail(ErrorInfo error)
    {
        var reason = _text.Message(error);
        if (IsClosed)
        {
            _toasts.Show(new ToastMessage(_text.Message("launch.postPlay.leaveOutFailed", ("reason", reason)), ToastKind.Error));
        }
        else
        {
            LeaveOutResult = reason;
        }
    }

    /// <summary>For a session that started from an older point, why the choices differ; empty otherwise.</summary>
    public string OlderNote { get; }

    /// <summary>True when <see cref="OlderNote"/> has text.</summary>
    public bool HasOlderNote => OlderNote.Length > 0;

    /// <summary>"Which should I pick?", under the older-snapshot note; null when the session did not start from an older snapshot.</summary>
    public ContextualHelpViewModel? OlderChoiceHelp { get; }

    /// <summary>True when <see cref="OlderChoiceHelp"/> shows.</summary>
    public bool HasOlderChoiceHelp => OlderChoiceHelp is not null;

    /// <summary>The cards, in order.</summary>
    public IReadOnlyList<PostPlayChoiceViewModel> Choices { get; }

    /// <summary>The main button's text, which names the selected choice.</summary>
    public string PrimaryLabel => SelectedChoice.Kind switch
    {
        CompletionChoice.UpdateExisting => _text.Message("launch.postPlay.updateIt"),
        CompletionChoice.MoveForward => _text.Message("launch.postPlay.continueIt"),
        CompletionChoice.Skip => _text.Message("launch.postPlay.notNow"),
        _ => _text.Message("launch.postPlay.saveIt"),
    };

    /// <inheritdoc />
    public override string Description => string.Join(' ', new[] { Body, HasNotSeenHint ? NotSeenHint : null, HasOlderNote ? OlderNote : null }.OfType<string>());

    /// <inheritdoc />
    public override double DialogWidth => 580;

    /// <inheritdoc />
    public override bool PinsFooter => true;

    /// <inheritdoc />
    protected override SessionCompletion DismissResult => new SessionCompletion.Skip();

    partial void OnSelectedChoiceChanged(PostPlayChoiceViewModel? oldValue, PostPlayChoiceViewModel newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        newValue.IsSelected = true;

        // The first time a player picks Replace, the card says where the old snapshot goes before they confirm.
        if (_explainReplace && newValue.Kind == CompletionChoice.UpdateExisting && newValue.Help is { } help)
        {
            _explainReplace = false;
            help.IsOpen = true;
            Remember(settings => settings with { ReplaceHelpSeen = true });
        }
    }

    // Best effort: a player whose settings cannot be written is shown the explanation again next time, which does no harm.
    private void Remember(Func<AppSettings, AppSettings> change) => _ = _settingsWriter.UpdateAsync(change);

    [RelayCommand]
    private async Task SaveAsync()
    {
        var choice = SelectedChoice;
        switch (choice.Kind)
        {
            case CompletionChoice.CaptureNew:
                Close(new SessionCompletion.CaptureNew(choice.EffectiveName));
                break;

            case CompletionChoice.UpdateExisting:
                var point = _options.UpdateTarget?.Name ?? _defaultPointName;
                var replace = await _dialogs.ConfirmAsync(new ConfirmRequest(
                    _text.Message("launch.postPlay.updateConfirm.title"),
                    _text.Message("launch.postPlay.updateConfirm.message", ("pointName", point)),
                    _text.Message("launch.postPlay.updateConfirm.confirm"),
                    Destructive: true));
                if (replace)
                {
                    Close(new SessionCompletion.UpdateExisting(point));
                }

                break;

            case CompletionChoice.ForkNewProfile:
                Close(new SessionCompletion.ForkNewProfile(choice.EffectiveName, choice.EffectiveName));
                break;

            case CompletionChoice.MoveForward:
                if (_options.NewerPointCount > 0 && !await ConfirmMoveForwardAsync())
                {
                    break;
                }

                Close(new SessionCompletion.MoveForward(_defaultPointName));
                break;

            default:
                Close(new SessionCompletion.Skip());
                break;
        }
    }

    private Task<bool> ConfirmMoveForwardAsync() =>
        _dialogs.ConfirmAsync(new ConfirmRequest(
            _text.Message("launch.postPlay.moveConfirm.title", ("profileName", _profileName)),
            _text.Message("launch.postPlay.moveConfirm.message", ("count", _options.NewerPointCount), ("profileName", _profileName)),
            _text.Message("launch.postPlay.moveConfirm.confirm"),
            Destructive: true));

    private List<PostPlayChoiceViewModel> BuildChoices(string today)
    {
        var offered = _options.Choices;

        // The service suggests Not now only when neither folder changed, so keeping is no longer the recommendation.
        var nothingChanged = _options.Default == CompletionChoice.Skip;
        var cards = new List<PostPlayChoiceViewModel>();

        if (offered.Contains(CompletionChoice.CaptureNew))
        {
            cards.Add(new PostPlayChoiceViewModel(
                CompletionChoice.CaptureNew,
                Icons.AddCircle,
                _text.Message("launch.postPlay.new.title"),
                _text.Message(nothingChanged ? "launch.postPlay.new.subOptional" : "launch.postPlay.new.sub"),
                _text.Message("launch.postPlay.new.nameLabel"),
                _defaultPointName));
        }

        if (offered.Contains(CompletionChoice.UpdateExisting) && _options.UpdateTarget is { } target)
        {
            cards.Add(new PostPlayChoiceViewModel(
                CompletionChoice.UpdateExisting,
                Icons.Edit,
                _text.Message("launch.postPlay.update.title", ("pointName", target.Name)),
                _text.Message("launch.postPlay.update.sub", ("pointName", target.Name)),
                help: ContextualHelp.Replace(_text, target.Name)));
        }

        if (offered.Contains(CompletionChoice.ForkNewProfile))
        {
            cards.Add(new PostPlayChoiceViewModel(
                CompletionChoice.ForkNewProfile,
                Icons.AddCircle,
                _text.Message("launch.postPlay.fork.title"),
                _text.Message("launch.postPlay.fork.sub", ("profileName", _profileName)),
                _text.Message("launch.postPlay.fork.nameLabel"),
                _text.Message("launch.postPlay.fork.defaultName", ("date", today))));
        }

        if (offered.Contains(CompletionChoice.MoveForward))
        {
            cards.Add(new PostPlayChoiceViewModel(
                CompletionChoice.MoveForward,
                Icons.History,
                _text.Message("launch.postPlay.move.title", ("profileName", _profileName)),
                _text.Message("launch.postPlay.move.sub", ("count", _options.NewerPointCount), ("profileName", _profileName))));
        }

        cards.Add(new PostPlayChoiceViewModel(
            CompletionChoice.Skip,
            Icons.Schedule,
            _text.Message("launch.postPlay.skip.title"),
            _text.Message(nothingChanged ? "launch.postPlay.skip.subNothingChanged" : "launch.postPlay.skip.sub")));
        return cards;
    }
}
