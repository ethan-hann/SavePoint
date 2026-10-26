using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Features.Launch;

/// <summary>
/// The card that stands in for the progress dialog once the game runs: the game, the profile and how long
/// ago it started (refreshed every minute), and "I'm done playing" for the case where exit detection cannot tell.
/// The Games screen pins it at the bottom of the pane; it is not a dialog, so the rest of the app stays usable.
/// </summary>
public sealed partial class RunningCardViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan _refreshEvery = TimeSpan.FromMinutes(1);

    private readonly string _profileName;
    private readonly DateTimeOffset _startedAt;
    private readonly UiFormatter _text;
    private readonly TimeProvider _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<Task<bool>> _donePlaying;
    private readonly ITimer _timer;

    /// <summary>The profile and how long ago the game started, refreshed every minute.</summary>
    [ObservableProperty]
    private string _subtitle = string.Empty;

    /// <summary>True once "I'm done playing" has been pressed and the service has not answered yet.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DonePlayingCommand))]
    private bool _isFinishing;

    /// <param name="gameName">The running game.</param>
    /// <param name="profileName">The profile it was started from.</param>
    /// <param name="startedAt">When the game started.</param>
    /// <param name="text">Formats the card's lines.</param>
    /// <param name="clock">Says what time it is and times the minute refresh.</param>
    /// <param name="dispatcher">Brings the timer's callback to the UI thread.</param>
    /// <param name="donePlaying">Tells the service the player has finished; answers false when it could not.</param>
    public RunningCardViewModel(
        string gameName,
        string profileName,
        DateTimeOffset startedAt,
        UiFormatter text,
        TimeProvider clock,
        IUiDispatcher dispatcher,
        Func<Task<bool>> donePlaying)
    {
        _profileName = profileName;
        _startedAt = startedAt;
        _text = text;
        _clock = clock;
        _dispatcher = dispatcher;
        _donePlaying = donePlaying;
        Title = text.Message("launch.running.title", ("gameName", gameName));
        Refresh();

        // The first tick lands on the next whole minute since the start, so the text is never a minute behind.
        var elapsed = Elapsed();
        var untilNextMinute = _refreshEvery - TimeSpan.FromTicks(elapsed.Ticks % _refreshEvery.Ticks);
        _timer = clock.CreateTimer(_ => _dispatcher.Post(Refresh), null, untilNextMinute, _refreshEvery);
    }

    /// <summary>"Skyrim is running".</summary>
    public string Title { get; }

    /// <summary>What to tell the player about a program that closed right away while SavePoint keeps watching for the game; empty when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _notice = string.Empty;

    /// <summary>True when <see cref="Notice"/> has text.</summary>
    public bool HasNotice => Notice.Length > 0;

    /// <summary>Sets or clears the notice about the started program having closed with an error.</summary>
    /// <param name="exitCode">The program's nonzero exit code, or null when there is nothing to say.</param>
    public void SetProgramExit(int? exitCode) =>
        Notice = exitCode is { } code
            ? _text.Message("launch.running.programClosed", ("code", code.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            : string.Empty;

    /// <summary>Stops the minute refresh.</summary>
    public void Dispose() => _timer.Dispose();

    private TimeSpan Elapsed() => TimeSpan.FromTicks(Math.Max(0, (_clock.GetUtcNow() - _startedAt).Ticks));

    private void Refresh()
    {
        var elapsed = Elapsed();
        Subtitle = elapsed < _refreshEvery
            ? _text.Message("launch.running.startedJustNow", ("profileName", _profileName))
            : _text.Message("launch.running.startedAgo", ("profileName", _profileName), ("duration", _text.Duration(elapsed)));
    }

    [RelayCommand(CanExecute = nameof(CanFinish))]
    private async Task DonePlayingAsync()
    {
        IsFinishing = true;
        if (!await _donePlaying())
        {
            IsFinishing = false;
        }
    }

    private bool CanFinish() => !IsFinishing;
}
