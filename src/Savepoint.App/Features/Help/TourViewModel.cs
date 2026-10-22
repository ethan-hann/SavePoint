using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Features.Help;

/// <summary>
/// The first-run tour: four steps that each point at a real control (the profile row, Play, History, New profile) with a small card
/// beside it. It does not block the screen, can be skipped at every step, and says nothing about whether it has run before: the shell
/// decides when to start it and remembers that it did.
/// </summary>
public sealed partial class TourViewModel : ObservableObject
{
    private static readonly TourStep[] _steps = [TourStep.Profile, TourStep.Play, TourStep.History, TourStep.NewProfile];

    private readonly UiFormatter _text;
    private string _gameName = string.Empty;
    private int _index;

    /// <summary>True while the tour is on a step.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <param name="text">Formats the tour's copy.</param>
    public TourViewModel(UiFormatter text)
    {
        _text = text;
        Current = StepAt(0);
        NextCommand = new RelayCommand(Next);
        BackCommand = new RelayCommand(Back, () => IsActive && _index > 0);
        SkipCommand = new RelayCommand(End);
    }

    /// <summary>Raised when the tour ends, by Done, Skip tour or Escape; focus goes back to the Play button.</summary>
    public event EventHandler? Ended;

    /// <summary>Raised each time a step is shown, so the window can announce it.</summary>
    public event EventHandler? StepShown;

    /// <summary>What the card says now.</summary>
    public TourStepViewModel Current { get; private set; }

    /// <summary>Moves to the next step, or ends the tour on the last one.</summary>
    public IRelayCommand NextCommand { get; }

    /// <summary>Moves to the step before; off on the first.</summary>
    public IRelayCommand BackCommand { get; }

    /// <summary>Ends the tour from any step.</summary>
    public IRelayCommand SkipCommand { get; }

    /// <summary>Starts the tour at its first step; a tour that is already running begins again.</summary>
    /// <param name="gameName">The game the first step names.</param>
    public void Start(string gameName)
    {
        // The step is built before the tour is marked active, so whatever shows it for the first time already finds its words.
        _gameName = gameName;
        _index = 0;
        Show();
        IsActive = true;
    }

    private void Next()
    {
        if (!IsActive)
        {
            return;
        }

        if (_index == _steps.Length - 1)
        {
            End();
            return;
        }

        _index++;
        Show();
    }

    private void Back()
    {
        if (IsActive && _index > 0)
        {
            _index--;
            Show();
        }
    }

    private void End()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        BackCommand.NotifyCanExecuteChanged();
        Ended?.Invoke(this, EventArgs.Empty);
    }

    private void Show()
    {
        Current = StepAt(_index);
        OnPropertyChanged(nameof(Current));
        BackCommand.NotifyCanExecuteChanged();
        StepShown?.Invoke(this, EventArgs.Empty);
    }

    private TourStepViewModel StepAt(int index)
    {
        var step = _steps[index];
        var (title, body) = step switch
        {
            TourStep.Profile => (_text.Message("help.tour.profile.title"), _text.Message("help.tour.profile.body", ("gameName", _gameName))),
            TourStep.Play => (_text.Message("help.tour.play.title"), _text.Message("help.tour.play.body")),
            TourStep.History => (_text.Message("help.tour.history.title"), _text.Message("help.tour.history.body")),
            _ => (_text.Message("help.tour.newProfile.title"), _text.Message("help.tour.newProfile.body")),
        };
        var number = index + 1;
        var last = index == _steps.Length - 1;
        return new TourStepViewModel(
            step,
            title,
            body,
            _text.Message("help.tour.step", ("number", number), ("count", _steps.Length)),
            _text.Message("help.tour.automationName", ("number", number), ("count", _steps.Length), ("title", title)),
            index > 0,
            _text.Message(last ? "help.tour.done" : "help.tour.next"),
            last ? _text.Message("help.tour.findAgain") : string.Empty);
    }
}
