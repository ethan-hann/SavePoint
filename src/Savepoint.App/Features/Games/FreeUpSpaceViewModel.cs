using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Games;

/// <summary>
/// The storage footer's Free up space for the selected game: starts the game's compaction, follows it while it runs, and says
/// how it ended. It also picks up a compaction that is already running for the game or for every game, started elsewhere.
/// </summary>
public sealed partial class FreeUpSpaceViewModel : ObservableObject, IDisposable
{
    private readonly IGamesApi _games;
    private readonly IJobsApi _jobs;
    private readonly UiFormatter _text;
    private readonly IUiDispatcher _dispatcher;
    private readonly Action<ErrorInfo> _showError;
    private readonly Func<Task> _afterRun;
    private readonly Dictionary<Guid, long> _revisions = [];

    private string? _gameId;
    private string _gameName = string.Empty;
    private Guid? _job;
    private bool _disposed;

    /// <summary>True while a compaction of the game, or of every game, runs.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FreeUpCommand))]
    private bool _isRunning;

    /// <summary>What the footer says about the run: that it is running, then how it ended; empty when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = string.Empty;

    /// <summary>What the live region says: each change of <see cref="StatusText"/>, including the same words twice.</summary>
    [ObservableProperty]
    private string _announcement = string.Empty;

    /// <param name="games">Starts the compaction.</param>
    /// <param name="jobs">Reports the compaction's progress and end.</param>
    /// <param name="text">Formats the words and sizes.</param>
    /// <param name="dispatcher">Brings job events back to the UI thread.</param>
    /// <param name="showError">Shows a failure to start the compaction.</param>
    /// <param name="afterRun">Runs after a compaction ends, so the footer can read the game's storage use again.</param>
    public FreeUpSpaceViewModel(IGamesApi games, IJobsApi jobs, UiFormatter text, IUiDispatcher dispatcher, Action<ErrorInfo> showError, Func<Task> afterRun)
    {
        _games = games;
        _jobs = jobs;
        _text = text;
        _dispatcher = dispatcher;
        _showError = showError;
        _afterRun = afterRun;
        FreeUpCommand = new AsyncRelayCommand(FreeUpAsync, () => !IsRunning && _gameId is not null);
        _jobs.JobChanged += OnJobChanged;
    }

    /// <summary>True when <see cref="StatusText"/> has something to say.</summary>
    public bool HasStatus => StatusText.Length > 0;

    /// <summary>The button's name for a screen reader ("Free up space for Skyrim Special Edition"); empty without a game.</summary>
    public string Name => _gameId is null ? string.Empty : _text.Message("games.storageFooter.freeUpName", ("name", _gameName));

    /// <summary>Starts the compaction for the game.</summary>
    public IAsyncRelayCommand FreeUpCommand { get; }

    /// <summary>Points the action at a game, or at none; a different game clears the status and follows that game's compaction if one runs.</summary>
    /// <param name="gameId">The game, or null.</param>
    /// <param name="gameName">The game's name, for the button's name.</param>
    public void SetGame(string? gameId, string? gameName)
    {
        _gameName = gameName ?? string.Empty;
        if (gameId == _gameId)
        {
            OnPropertyChanged(nameof(Name));
            return;
        }

        _gameId = gameId;
        _job = null;
        _revisions.Clear();
        IsRunning = false;
        Say(string.Empty, announce: false);
        OnPropertyChanged(nameof(Name));
        FreeUpCommand.NotifyCanExecuteChanged();

        // A compaction that is running already is news; one that ended before the game was picked is history.
        foreach (var job in _jobs.List())
        {
            Handle(job);
        }
    }

    /// <summary>Clears how the last run ended; a run that is still going keeps its status.</summary>
    public void ClearResult()
    {
        if (!IsRunning)
        {
            Say(string.Empty, announce: false);
        }
    }

    /// <summary>Stops following jobs.</summary>
    public void Dispose()
    {
        _disposed = true;
        _jobs.JobChanged -= OnJobChanged;
    }

    private async Task FreeUpAsync()
    {
        if (_gameId is not { } gameId)
        {
            return;
        }

        var started = await _games.OptimizeStorageAsync(gameId);
        if (!started.Ok)
        {
            _showError(started.Error);
            return;
        }

        // The job's own events normally get here first; this catches one that ended before the call returned.
        if (_jobs.Get(started.Value.JobId) is { } job)
        {
            Handle(job);
        }
    }

    // The jobs raise their events from any thread.
    private void OnJobChanged(object? sender, JobSnapshot job) => _dispatcher.Post(() => Handle(job));

    private void Handle(JobSnapshot job)
    {
        if (_disposed || job.Kind != JobKind.Compact || _gameId is null || (job.GameId is not null && job.GameId != _gameId))
        {
            return;
        }

        if (_revisions.TryGetValue(job.JobId, out var held) && job.Revision <= held)
        {
            return;
        }

        if (job.Status == JobStatus.Running)
        {
            _revisions[job.JobId] = job.Revision;
            if (_job != job.JobId)
            {
                _job = job.JobId;
                IsRunning = true;
                Say(_text.Message("games.storageFooter.freeUpRunning"), announce: true);
            }

            return;
        }

        // A job that ended before it was seen is history, not news.
        if (_job != job.JobId)
        {
            return;
        }

        _revisions[job.JobId] = job.Revision;
        _job = null;
        IsRunning = false;
        Say(Outcome(job), announce: true);
        _ = _afterRun();
    }

    private string Outcome(JobSnapshot job)
    {
        switch (job.Status)
        {
            case JobStatus.Succeeded:
                var freed = (job.Result as OptimizeResult)?.Games.FirstOrDefault(game => game.GameId == _gameId)?.BytesFreed;
                return freed is > 0
                    ? _text.Message("games.storageFooter.freed", ("size", _text.FileSize(freed.Value)))
                    : _text.Message("games.storageFooter.freedNothing");
            case JobStatus.Partial:
                return _text.Message("games.storageFooter.freedPartial");
            case JobStatus.Cancelled:
                return _text.Message("games.storageFooter.freeUpCanceled");
            default:
                return job.Error is { } error
                    ? _text.Message("games.storageFooter.freeUpFailedBecause", ("reason", _text.Message(error)))
                    : _text.Message("games.storageFooter.freeUpFailed");
        }
    }

    private void Say(string text, bool announce)
    {
        StatusText = text;
        if (announce)
        {
            Announcement = string.Empty;
            Announcement = text;
        }
    }
}
