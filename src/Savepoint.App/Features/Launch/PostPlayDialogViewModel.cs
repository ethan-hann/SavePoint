using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Theme;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Launch;

/// <summary>
/// The dialog that opens when the game exits (mockup 2d): how long the session was, how many new files there are, and
/// radio cards to keep it as a new point, update the latest one, or leave it. A session that started from an older
/// point offers a new profile, continuing the profile from there, or leaving it instead (D14). It answers with the
/// completion to apply; Escape and "Not now" answer with <see cref="SessionCompletion.Skip"/>.
/// </summary>
public sealed partial class PostPlayDialogViewModel : DialogViewModel<SessionCompletion>
{
    private readonly UiFormatter _text;
    private readonly IDialogs _dialogs;
    private readonly string _profileName;
    private readonly PostPlayOptions _options;
    private readonly string _defaultPointName;

    /// <summary>The card the player has selected; the main button acts on it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryLabel), nameof(ShowNotNowButton))]
    private PostPlayChoiceViewModel _selectedChoice;

    /// <param name="exited">The session's exit: its length, new files and the choices it allows.</param>
    /// <param name="profileName">The profile the session was played from.</param>
    /// <param name="deps">The Api and formatting the dialog uses.</param>
    public PostPlayDialogViewModel(SessionState.Exited exited, string profileName, LaunchDependencies deps)
        : base(deps.Text.Message("launch.postPlay.title", ("duration", deps.Text.Duration(exited.PlayDuration))))
    {
        _text = deps.Text;
        _dialogs = deps.Dialogs;
        _profileName = profileName;
        _options = exited.Options;
        var today = _text.NameDate(deps.Clock.GetUtcNow());
        _defaultPointName = _text.Message("launch.postPlay.defaultName", ("date", today));

        Body = _text.Message("launch.postPlay.body", ("count", exited.NewFileCount));
        InstallNote = exited.ChangedInstallFileCount > 0 ? _text.Message("launch.postPlay.installChanged", ("count", exited.ChangedInstallFileCount)) : string.Empty;
        var older = _options.Choices.Contains(CompletionChoice.ForkNewProfile) || _options.Choices.Contains(CompletionChoice.MoveForward);
        OlderNote = older ? _text.Message("launch.postPlay.olderNote", ("profileName", profileName)) : string.Empty;

        Choices = BuildChoices(today);
        _selectedChoice = Choices.FirstOrDefault(choice => choice.Kind == _options.Default) ?? Choices[0];
        _selectedChoice.IsSelected = true;
    }

    /// <summary>"Your saves folder has 3 new files since you started. What should SavePoint do with them?"</summary>
    public string Body { get; }

    /// <summary>A line about the game's own files having changed while it ran; empty when they did not.</summary>
    public string InstallNote { get; }

    /// <summary>True when <see cref="InstallNote"/> has text.</summary>
    public bool HasInstallNote => InstallNote.Length > 0;

    /// <summary>For a session that started from an older point, why the choices differ; empty otherwise.</summary>
    public string OlderNote { get; }

    /// <summary>True when <see cref="OlderNote"/> has text.</summary>
    public bool HasOlderNote => OlderNote.Length > 0;

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

    /// <summary>True when the separate "Not now" button shows: it would only repeat the main button when that already says so.</summary>
    public bool ShowNotNowButton => SelectedChoice.Kind != CompletionChoice.Skip;

    /// <inheritdoc />
    public override string Description => Body;

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
    }

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

    [RelayCommand]
    private void NotNow() => Close(new SessionCompletion.Skip());

    private Task<bool> ConfirmMoveForwardAsync() =>
        _dialogs.ConfirmAsync(new ConfirmRequest(
            _text.Message("launch.postPlay.moveConfirm.title", ("profileName", _profileName)),
            _text.Message("launch.postPlay.moveConfirm.message", ("count", _options.NewerPointCount), ("profileName", _profileName)),
            _text.Message("launch.postPlay.moveConfirm.confirm"),
            Destructive: true));

    private List<PostPlayChoiceViewModel> BuildChoices(string today)
    {
        var offered = _options.Choices;
        var cards = new List<PostPlayChoiceViewModel>();

        if (offered.Contains(CompletionChoice.CaptureNew))
        {
            cards.Add(new PostPlayChoiceViewModel(
                CompletionChoice.CaptureNew,
                Icons.AddCircle,
                _text.Message("launch.postPlay.new.title"),
                _text.Message("launch.postPlay.new.sub"),
                _text.Message("launch.postPlay.new.nameLabel"),
                _defaultPointName));
        }

        if (offered.Contains(CompletionChoice.UpdateExisting) && _options.UpdateTarget is { } target)
        {
            cards.Add(new PostPlayChoiceViewModel(
                CompletionChoice.UpdateExisting,
                Icons.Edit,
                _text.Message("launch.postPlay.update.title", ("pointName", target.Name)),
                _text.Message("launch.postPlay.update.sub")));
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
                _text.Message("launch.postPlay.move.sub", ("count", _options.NewerPointCount))));
        }

        cards.Add(new PostPlayChoiceViewModel(
            CompletionChoice.Skip,
            Icons.Schedule,
            _text.Message("launch.postPlay.skip.title"),
            _text.Message("launch.postPlay.skip.sub")));
        return cards;
    }
}
