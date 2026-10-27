using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Activity;

/// <summary>
/// The Activity screen: the running and recent jobs of the selected game or of every game, newest first, and the details of the one
/// the reader chose. It follows the jobs as they change, keeps each row and the reader's selection in place while they do, and
/// says each change of state to a screen reader.
/// </summary>
public sealed partial class ActivityScreenViewModel : ObservableObject, IScreen, IDisposable
{
    private readonly IShellContext _shell;
    private readonly ActivityDependencies _deps;
    private readonly ActivityText _words;
    private readonly Dictionary<Guid, JobRowViewModel> _known = [];
    private readonly Dictionary<Guid, long> _order = [];
    private long _sequence;
    private JobRowViewModel? _selected;
    private JobDetailsViewModel? _details;
    private bool _disposed;

    /// <param name="shell">What the screen sees of the shell.</param>
    /// <param name="dependencies">What the screen is built from.</param>
    public ActivityScreenViewModel(IShellContext shell, ActivityDependencies dependencies)
    {
        _shell = shell;
        _deps = dependencies;
        _words = new ActivityText(dependencies.Text, id => _shell.Games.FirstOrDefault(game => game.Id == id)?.Name, dependencies.Clock);
        var text = dependencies.Text;
        ScopeOptions =
        [
            new ScopeOption(ActivityScope.ThisGame, text.Message("activity.scope.thisGame")),
            new ScopeOption(ActivityScope.AllGames, text.Message("activity.scope.allGames")),
        ];
        _selectedScope = ScopeOptions[_shell.SelectedGame is null ? 1 : 0];

        // Subscribed before the list is read, so a change in between is not lost.
        _shell.PropertyChanged += OnShellChanged;
        _shell.ActivityRequested += OnActivityRequested;
        _shell.LibraryRefreshed += OnLibraryRefreshed;
        _deps.Jobs.JobChanged += OnJobChanged;
        foreach (var job in _deps.Jobs.List())
        {
            Take(job, announce: false);
        }

        RefreshRows();
    }

    /// <summary>Raised with each thing a screen reader should be told, so a message identical to the last is still spoken.</summary>
    public event EventHandler<string>? Announced;

    /// <inheritdoc />
    public NavDestination Destination => NavDestination.Activity;

    /// <summary>The screen's title.</summary>
    public string Title => _deps.Text.Message("activity.title");

    /// <summary>The line under the title: which jobs are listed and how many.</summary>
    public string Subtitle => _shell.SelectedGame is { } game && !IsAllGames
        ? _deps.Text.Message("activity.subtitle.thisGame", ("game", game.Name), ("count", Rows.Count))
        : _deps.Text.Message("activity.subtitle.allGames", ("count", Rows.Count));

    /// <summary>True while every game's jobs are listed.</summary>
    public bool IsAllGames => SelectedScope.Scope == ActivityScope.AllGames;

    /// <summary>The selected game's name, which tints the tile beside the title; empty without a game.</summary>
    public string GameName => _shell.SelectedGame?.Name ?? string.Empty;

    /// <summary>The selected game's artwork, shown beside the title while the screen lists one game; null otherwise.</summary>
    public GameArt? Art => ShowArt && _shell.SelectedGame is { } game ? _shell.Artwork.For(game.Id) : null;

    /// <summary>True while the artwork shows.</summary>
    public bool ShowArt => !IsAllGames && _shell.SelectedGame is not null;

    /// <summary>The choices of the scope control: this game, and all games.</summary>
    public IReadOnlyList<ScopeOption> ScopeOptions { get; }

    /// <summary>The scope control's accessible name.</summary>
    public string ScopeLabel => _deps.Text.Message("activity.scope.label");

    /// <summary>True when the scope control shows: there is a selected game to choose between.</summary>
    public bool ShowScope => _shell.SelectedGame is not null;

    /// <summary>The jobs listed, newest started first.</summary>
    public ObservableCollection<JobRowViewModel> Rows { get; } = [];

    /// <summary>The row the reader chose; setting it shows that job's details.</summary>
    public JobRowViewModel? SelectedRow
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
            {
                return;
            }

            var left = _selected;
            _selected = value;
            _details = value is null ? null : new JobDetailsViewModel(value, _words, _deps.Jobs, _deps.Dialogs, _deps.Text, Announce);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Details));
            OnPropertyChanged(nameof(ShowDetails));

            // A job that was only kept for the reader to finish reading goes now that another is chosen. The list is still committing this
            // choice and reads its rows again when it finishes, so the row leaves once the list is idle, not while it is counting them.
            if (left is not null && value is not null && Forgotten(left))
            {
                Forget();
                _deps.Dispatcher.PostWhenIdle(() =>
                {
                    if (!_disposed)
                    {
                        RefreshRows();
                    }
                });
            }
        }
    }

    /// <summary>The details of the chosen job; null while none is chosen.</summary>
    public JobDetailsViewModel? Details => _details;

    /// <summary>True when the list has rows.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>True when there is nothing to list at all.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>True when the selected game has no jobs though other games do.</summary>
    public bool IsFilteredEmpty => IsEmpty && !IsAllGames && _known.Count > 0;

    /// <summary>True when the details pane shows a job.</summary>
    public bool ShowDetails => _details is not null;

    /// <summary>What a screen reader was last told when a job started, changed state or ended.</summary>
    [ObservableProperty]
    private string _announcement = string.Empty;

    /// <summary>The note under the list: how many finished jobs SavePoint keeps.</summary>
    public string FooterText => _deps.Text.Message("activity.footer");

    /// <summary>Which jobs are listed.</summary>
    [ObservableProperty]
    private ScopeOption _selectedScope;

    /// <summary>Lists every game's jobs.</summary>
    [RelayCommand]
    private void ShowAllGames() => SelectedScope = ScopeOptions[1];

    /// <inheritdoc />
    public void OnShown()
    {
        // Names and times may have moved on since the rows were last drawn, and a job may have changed while the screen was away.
        foreach (var job in _deps.Jobs.List())
        {
            Take(job, announce: false);
        }

        Forget();
        foreach (var row in _known.Values)
        {
            row.Apply(row.Job);
        }

        RefreshRows();
    }

    /// <summary>Stops following the shell and the jobs.</summary>
    public void Dispose()
    {
        _disposed = true;
        _shell.PropertyChanged -= OnShellChanged;
        _shell.ActivityRequested -= OnActivityRequested;
        _shell.LibraryRefreshed -= OnLibraryRefreshed;
        _deps.Jobs.JobChanged -= OnJobChanged;
    }

    partial void OnSelectedScopeChanged(ScopeOption value)
    {
        OnPropertyChanged(nameof(IsAllGames));
        OnPropertyChanged(nameof(ShowArt));
        OnPropertyChanged(nameof(Art));
        RefreshRows();
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IShellContext.SelectedGame))
        {
            return;
        }

        // Another game chosen in the rail is the game whose jobs the reader wants, whatever was listed before.
        SelectedScope = ScopeOptions[_shell.SelectedGame is null ? 1 : 0];
        OnPropertyChanged(nameof(GameName));
        OnPropertyChanged(nameof(ShowScope));
        OnPropertyChanged(nameof(ShowArt));
        OnPropertyChanged(nameof(Art));
        RefreshRows();
    }

    private void OnActivityRequested(object? sender, ActivityRequest request)
    {
        SelectedScope = ScopeOptions[request.GameId is null || _shell.SelectedGame is null ? 1 : 0];
        RefreshRows();
    }

    private void OnLibraryRefreshed(object? sender, EventArgs e)
    {
        foreach (var row in _known.Values)
        {
            row.Apply(row.Job);
        }

        OnPropertyChanged(nameof(ShowScope));
        RefreshRows();
    }

    private void OnJobChanged(object? sender, JobSnapshot job) =>
        _deps.Dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            Take(job, announce: true);

            // The list is rebuilt when the screen is shown again, so a screen that is not showing does not sort it for every progress report.
            if (_shell.Destination == NavDestination.Activity)
            {
                RefreshRows();
            }
        });

    // Keeps the newest snapshot of each job; one that is not newer than the one held is dropped.
    private void Take(JobSnapshot job, bool announce)
    {
        if (_known.TryGetValue(job.JobId, out var row))
        {
            if (job.Revision <= row.Job.Revision)
            {
                return;
            }

            var before = row.StatusText;
            row.Apply(job);
            if (ReferenceEquals(_details?.Row, row))
            {
                _details.Apply(job);
            }

            if (announce && row.StatusText != before)
            {
                Announce(Said(row));
            }

            // A job that ends is what makes the registry let go of an older one.
            if (job.Status != JobStatus.Running)
            {
                Forget();
            }

            return;
        }

        row = new JobRowViewModel(job, _words);
        _known[job.JobId] = row;
        _order[job.JobId] = ++_sequence;
        if (announce)
        {
            Announce(Said(row));
        }

        Forget();
    }

    private string Said(JobRowViewModel row) =>
        _deps.Text.Message("activity.announce", ("title", row.Title), ("status", row.StatusText.TrimEnd('…')));

    private void Announce(string message)
    {
        Announcement = message;
        Announced?.Invoke(this, message);
    }

    // The jobs shown are the registry's to keep: a finished job it has let go of (five per game) is forgotten here too, so a long
    // session never shows more than the footer promises. A job still running stays whatever the registry says, and so does the one the
    // reader has open, with a note that it is no longer kept, until another is chosen: the details they are reading never change under them.
    private void Forget()
    {
        var listed = _deps.Jobs.List().Select(job => job.JobId).ToHashSet();
        foreach (var old in _known.Values.Where(row => !row.IsRunning && !listed.Contains(row.JobId)).ToList())
        {
            if (ReferenceEquals(old, _selected))
            {
                _details?.MarkNoLongerKept();
                continue;
            }

            _known.Remove(old.JobId);
            _order.Remove(old.JobId);
        }
    }

    private bool Forgotten(JobRowViewModel row) =>
        !row.IsRunning && _deps.Jobs.List().All(job => job.JobId != row.JobId);

    private bool Matches(JobRowViewModel row) =>
        IsAllGames || (_shell.SelectedGame is { } game && row.Job.GameId == game.Id);

    // Rows are matched by job id and kept in place, so a refresh never replaces the row a reader has focused.
    private void RefreshRows()
    {
        var wanted = _known.Values
            .Where(Matches)
            .OrderByDescending(row => row.Job.StartedAt)
            .ThenByDescending(row => _order[row.JobId])
            .ToList();

        for (var index = Rows.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(Rows[index]))
            {
                Rows.RemoveAt(index);
            }
        }

        for (var index = 0; index < wanted.Count; index++)
        {
            if (index < Rows.Count && ReferenceEquals(Rows[index], wanted[index]))
            {
                continue;
            }

            var at = Rows.IndexOf(wanted[index]);
            if (at >= 0)
            {
                Rows.Move(at, index);
            }
            else
            {
                Rows.Insert(index, wanted[index]);
            }
        }

        if (_selected is null || !Rows.Contains(_selected))
        {
            SelectedRow = Rows.FirstOrDefault(row => row.IsRunning) ?? Rows.FirstOrDefault();
        }

        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsFilteredEmpty));
    }
}
