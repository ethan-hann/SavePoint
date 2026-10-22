using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Help;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.History;

/// <summary>What the History screen is showing.</summary>
public enum HistoryState
{
    /// <summary>The library is empty, so there is no game to show history for.</summary>
    NoGame,

    /// <summary>The first read for this game has not come back.</summary>
    Loading,

    /// <summary>The read failed; <see cref="HistoryScreenViewModel.ErrorCard"/> says why.</summary>
    Failed,

    /// <summary>The game (or the chosen profile) has no points yet.</summary>
    Empty,

    /// <summary>There are points to show.</summary>
    Loaded,
}

/// <summary>What the History screen is built from.</summary>
/// <param name="History">The history Api.</param>
/// <param name="Profiles">Supplies the profile filter's entries.</param>
/// <param name="Games">Supplies the space kept for the game.</param>
/// <param name="Jobs">Tells the screen when a capture or launch finished, so the markers stay current.</param>
/// <param name="Dialogs">Asks for the name of a profile a recently removed snapshot comes back as.</param>
/// <param name="Text">Formats the copy.</param>
/// <param name="Dispatcher">Brings Api events to the UI thread.</param>
/// <param name="Clock">What "this week" is measured from.</param>
public sealed record HistoryDependencies(
    IHistoryApi History,
    IProfilesApi Profiles,
    IGamesApi Games,
    IJobsApi Jobs,
    IDialogs Dialogs,
    UiFormatter Text,
    IUiDispatcher Dispatcher,
    TimeProvider Clock);

/// <summary>
/// The History screen (mockups 2h and 2j): the points the selected game can go back to, as a timeline and a list of
/// saves or as cards, with a profile filter and a Cards / List choice that lasts for the session. "Go back to this" and
/// "Load" hand a target to the shell's launch flow. The screen follows the selected game, library refreshes and
/// finished jobs while it is shown, and reads again each time it comes into view.
/// </summary>
public sealed partial class HistoryScreenViewModel : ObservableObject, IScreen
{
    private static readonly TimeSpan _thisWeek = TimeSpan.FromDays(7);

    private readonly IShellContext _shell;
    private readonly HistoryDependencies _deps;
    private readonly HistoryText _words;
    private readonly List<FlowCommand> _flowCommands = [];

    private CancellationTokenSource? _reading;
    private int _generation;
    private string? _loadedGameId;
    private ProfileFilter? _currentFilter;
    private ViewModeOption? _currentViewOption;
    private HistoryViewMode _viewMode = HistoryViewMode.Cards;
    private bool _restoring;

    /// <summary>What the screen is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsFilteredEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowFilter))]
    [NotifyPropertyChangedFor(nameof(HasPoints))]
    [NotifyPropertyChangedFor(nameof(HasNoGame))]
    [NotifyPropertyChangedFor(nameof(ShowRemoved))]
    private HistoryState _state = HistoryState.Loading;

    /// <summary>The selected game's name; null when the library is empty.</summary>
    [ObservableProperty]
    private string? _gameName;

    /// <summary>The selected game's artwork for the header tile; null when the library is empty.</summary>
    [ObservableProperty]
    private GameArt? _art;

    /// <summary>The line under the title: the game and how many points are kept ("Skyrim Special Edition · 8 points kept").</summary>
    [ObservableProperty]
    private string _subtitle = string.Empty;

    /// <summary>Why the read failed, with a Try again button; null unless <see cref="State"/> is <see cref="HistoryState.Failed"/>.</summary>
    [ObservableProperty]
    private ErrorCardViewModel? _errorCard;

    /// <summary>The "Versions &amp; mods" column, newest first.</summary>
    [ObservableProperty]
    private IReadOnlyList<TimelineEntryViewModel> _timeline = [];

    /// <summary>The "Saves" column, newest first.</summary>
    [ObservableProperty]
    private IReadOnlyList<SaveRowViewModel> _saves = [];

    /// <summary>The card view's groups, "This week" first; a group with no cards is left out.</summary>
    [ObservableProperty]
    private IReadOnlyList<PointGroupViewModel> _groups = [];

    /// <summary>The filter menu: "All profiles", then each profile of the game.</summary>
    [ObservableProperty]
    private IReadOnlyList<ProfileFilter> _filters = [];

    /// <summary>The profile the points are narrowed to; choosing one reads again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterLabel))]
    [NotifyPropertyChangedFor(nameof(FilterAutomationName))]
    [NotifyPropertyChangedFor(nameof(IsFilteredEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowFilter))]
    private ProfileFilter? _selectedFilter;

    /// <summary>The Cards / List segment chosen; the choice lasts for the session.</summary>
    [ObservableProperty]
    private ViewModeOption? _selectedViewOption;

    /// <summary>The storage line, or null when the size could not be read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFooter))]
    private string? _footerText;

    /// <summary>The selected game's recently removed snapshots, the most recently removed first; empty when there are none or they could not be read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemoved))]
    [NotifyPropertyChangedFor(nameof(ShowRemoved))]
    [NotifyPropertyChangedFor(nameof(RemovedHeading))]
    private IReadOnlyList<RemovedSnapshotViewModel> _removed = [];

    /// <summary>Why the recently removed snapshots could not be read; null when they were.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemoved))]
    [NotifyPropertyChangedFor(nameof(ShowRemoved))]
    [NotifyPropertyChangedFor(nameof(HasRemovedError))]
    private string? _removedError;

    /// <summary>True while the "Recently removed" section is open. It starts closed and stays as the player left it while the screen follows the same game.</summary>
    [ObservableProperty]
    private bool _isRemovedOpen;

    /// <param name="shell">What the screen follows: the selected game, library refreshes and the launch flow.</param>
    /// <param name="dependencies">The Api and helpers.</param>
    public HistoryScreenViewModel(IShellContext shell, HistoryDependencies dependencies)
    {
        _shell = shell;
        _deps = dependencies;
        _words = new HistoryText(dependencies.Text);

        var text = dependencies.Text;
        ViewOptions =
        [
            new ViewModeOption(HistoryViewMode.Cards, text.Message("history.view.cards")),
            new ViewModeOption(HistoryViewMode.List, text.Message("history.view.list")),
        ];
        ViewLabel = text.Message("history.view.label");
        _currentViewOption = ViewOptions[0];
        _selectedViewOption = _currentViewOption;
        RetryCommand = new AsyncRelayCommand(ReloadAsync);
        ShowAllProfilesCommand = new RelayCommand(ShowAllProfiles);
        SnapshotHelp = ContextualHelp.Snapshot(text, shell.ShowHelp);

        shell.PropertyChanged += OnShellPropertyChanged;
        shell.LibraryRefreshed += (_, _) => ReloadIfShown();
        dependencies.Jobs.JobChanged += (_, job) => dependencies.Dispatcher.Post(() => OnJobChanged(job));
        shell.LaunchFlow.PropertyChanged += (_, _) => dependencies.Dispatcher.Post(RefreshFlowCommands);
        shell.LaunchFlow.InlineContentChanged += (_, _) => dependencies.Dispatcher.Post(RefreshFlowCommands);
    }

    /// <inheritdoc />
    public NavDestination Destination => NavDestination.History;

    /// <summary>"What's a snapshot?", beside the title.</summary>
    public ContextualHelpViewModel SnapshotHelp { get; }

    /// <summary>The Cards and List segments, Cards first.</summary>
    public IReadOnlyList<ViewModeOption> ViewOptions { get; }

    /// <summary>The Cards / List toggle's accessible name ("View").</summary>
    public string ViewLabel { get; }

    /// <summary>The profile filter button's accessible name, which includes the profile chosen ("Filter by profile: All profiles").</summary>
    public string FilterAutomationName => _deps.Text.Message("history.filter.automationName", ("profile", FilterLabel));

    /// <summary>The view showing the points; Cards until the user chooses, then whatever they chose for the rest of the session.</summary>
    public HistoryViewMode ViewMode => _viewMode;

    /// <summary>True while the cards show.</summary>
    public bool IsCardsView => _viewMode == HistoryViewMode.Cards;

    /// <summary>True while the two-column list shows.</summary>
    public bool IsListView => _viewMode == HistoryViewMode.List;

    /// <summary>The filter button's text: "All profiles" or the chosen profile's name.</summary>
    public string FilterLabel => SelectedFilter?.Label ?? string.Empty;

    /// <summary>True while the first read for the game has not come back.</summary>
    public bool IsLoading => State == HistoryState.Loading;

    /// <summary>True when the read failed and <see cref="ErrorCard"/> shows.</summary>
    public bool HasError => State == HistoryState.Failed;

    /// <summary>True when there are no points to show, with or without a profile filter.</summary>
    public bool IsEmpty => State == HistoryState.Empty;

    /// <summary>True when the empty state is down to the profile filter: the chosen profile has no points though others may.</summary>
    public bool IsFilteredEmpty => State == HistoryState.Empty && SelectedFilter?.ProfileId is not null;

    /// <summary>True when the profile filter shows: there are points to narrow down, or the chosen profile has none and another has to be picked.</summary>
    public bool ShowFilter => HasPoints || IsFilteredEmpty;

    /// <summary>True when there are points to show.</summary>
    public bool HasPoints => State == HistoryState.Loaded;

    /// <summary>True when the library is empty.</summary>
    public bool HasNoGame => State == HistoryState.NoGame;

    /// <summary>True when the storage line shows.</summary>
    public bool HasFooter => FooterText is not null;

    /// <summary>True when there is something to say about recently removed snapshots: some are kept, or they could not be read.</summary>
    public bool HasRemoved => Removed.Count > 0 || RemovedError is not null;

    /// <summary>True when the "Recently removed" section shows: it has something to say and the screen has read the game's snapshots.</summary>
    public bool ShowRemoved => HasRemoved && State is HistoryState.Loaded or HistoryState.Empty;

    /// <summary>True when <see cref="RemovedError"/> shows instead of the list.</summary>
    public bool HasRemovedError => RemovedError is not null;

    /// <summary>The section's heading, with how many snapshots it holds ("Recently removed (3)").</summary>
    public string RemovedHeading => Removed.Count > 0
        ? _deps.Text.Message("history.removed.heading", ("count", Removed.Count))
        : _deps.Text.Message("history.removed.title");

    /// <summary>What the section says about the snapshots: they belong to no profile, and are deleted for good the next time space is freed up.</summary>
    public string RemovedNote => _deps.Text.Message("history.removed.note");

    /// <summary>Reads the history again after a failure.</summary>
    public IAsyncRelayCommand RetryCommand { get; }

    /// <summary>Clears the profile filter; offered on the empty state of a profile with no points.</summary>
    public IRelayCommand ShowAllProfilesCommand { get; }

    /// <inheritdoc />
    public void OnShown() => _ = ReloadAsync();

    /// <summary>
    /// Reads the selected game's points, the profile list and the space kept for it. The first read for a game shows the
    /// loading state; a later read keeps what is on screen until the new answer lands. An answer to a read that a newer
    /// one has replaced is dropped.
    /// </summary>
    public async Task ReloadAsync()
    {
        // A newer read supersedes the one in flight; its token is cancelled and its answer is dropped by generation.
        var previous = _reading;
        previous?.Cancel();
        previous?.Dispose();
        var generation = ++_generation;
        if (_shell.SelectedGame is not { } game)
        {
            _loadedGameId = null;
            Clear();
            GameName = null;
            Art = null;
            Subtitle = string.Empty;
            State = HistoryState.NoGame;
            return;
        }

        if (game.Id != _loadedGameId)
        {
            StartGame(game);
        }
        else if (State is HistoryState.Failed or HistoryState.NoGame)
        {
            State = HistoryState.Loading;
        }

        var source = new CancellationTokenSource();
        _reading = source;
        var historyTask = _deps.History.GetAsync(game.Id, _currentFilter?.ProfileId, source.Token);
        var profilesTask = _deps.Profiles.ListAsync(game.Id, source.Token);
        var usageTask = _deps.Games.GetStorageUsageAsync(game.Id, source.Token);
        var removedTask = _deps.History.GetRecentlyRemovedAsync(game.Id, source.Token);
        await Task.WhenAll(historyTask, profilesTask, usageTask, removedTask);
        if (generation != _generation)
        {
            return;
        }

        var history = await historyTask;
        if (!history.Ok)
        {
            ShowFailure(game, history.Error);
            return;
        }

        var profiles = await profilesTask;
        if (profiles.Ok && !UpdateFilters(profiles.Value))
        {
            // The chosen profile is gone: start over with every profile.
            await ReloadAsync();
            return;
        }

        var usage = await usageTask;
        FooterText = usage.Ok
            ? _deps.Text.Message("history.footer", ("size", _deps.Text.FileSize(usage.Value.BytesUsed)))
            : null;

        // A list that cannot be read is said so in its own section, and never takes the history down with it.
        var removed = await removedTask;
        RemovedError = removed.Ok ? null : _deps.Text.Message(removed.Error);
        Removed = removed.Ok ? BuildRemoved(game, removed.Value.Snapshots) : [];
        Show(game, history.Value.Points);
    }

    partial void OnSelectedFilterChanged(ProfileFilter? value)
    {
        if (_restoring)
        {
            return;
        }

        // A list box clears its selection while it rebuilds; only a real choice counts.
        if (value is null)
        {
            Restore(() => SelectedFilter = _currentFilter);
            return;
        }

        _currentFilter = value;
        _ = ReloadAsync();
    }

    partial void OnSelectedViewOptionChanged(ViewModeOption? value)
    {
        if (_restoring)
        {
            return;
        }

        if (value is null)
        {
            Restore(() => SelectedViewOption = _currentViewOption);
            return;
        }

        _currentViewOption = value;
        _viewMode = value.Mode;
        OnPropertyChanged(nameof(ViewMode));
        OnPropertyChanged(nameof(IsCardsView));
        OnPropertyChanged(nameof(IsListView));
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The shell announces the selection again whenever it rebuilds its rows; only a different game is a new read.
        if (e.PropertyName == nameof(IShellContext.SelectedGame) && _shell.SelectedGame?.Id != _loadedGameId)
        {
            ReloadIfShown();
        }
    }

    private void OnJobChanged(JobSnapshot job)
    {
        RefreshFlowCommands();
        if (job.Status != JobStatus.Running && (job.GameId is null || job.GameId == _loadedGameId))
        {
            ReloadIfShown();
        }
    }

    private void ReloadIfShown()
    {
        if (_shell.Destination == NavDestination.History)
        {
            _ = ReloadAsync();
        }
    }

    private void ShowAllProfiles()
    {
        if (Filters.Count > 0)
        {
            SelectedFilter = Filters[0];
        }
    }

    // A new game starts with every profile and nothing on screen but the loading state.
    private void StartGame(GameDefinition game)
    {
        _loadedGameId = game.Id;
        GameName = game.Name;
        Art = _shell.Artwork.For(game.Id);
        Subtitle = game.Name;
        ErrorCard = null;
        FooterText = null;
        IsRemovedOpen = false;
        Clear();
        var all = new ProfileFilter(null, _deps.Text.Message("history.filter.all"));
        Restore(() =>
        {
            Filters = [all];
            _currentFilter = all;
            SelectedFilter = all;
        });
        State = HistoryState.Loading;
    }

    private void Clear()
    {
        Timeline = [];
        Saves = [];
        Groups = [];
        Removed = [];
        RemovedError = null;
        _flowCommands.Clear();
    }

    // False when the profile being filtered on no longer exists; the filter is then back on every profile.
    private bool UpdateFilters(IReadOnlyList<Profile> profiles)
    {
        var all = new ProfileFilter(null, _deps.Text.Message("history.filter.all"));
        List<ProfileFilter> filters = [all, .. profiles.Select(profile => new ProfileFilter(profile.Id, profile.Name))];

        // The same profiles as before keep the same list and items, so a list that is open keeps its focus while a choice reloads the points.
        var unchanged = Filters.Count == filters.Count && Filters.Zip(filters).All(pair => pair.First == pair.Second);
        if (unchanged)
        {
            filters = [.. Filters];
        }

        var chosen = filters.FirstOrDefault(filter => filter.ProfileId == _currentFilter?.ProfileId);
        Restore(() =>
        {
            if (!unchanged)
            {
                Filters = filters;
            }

            _currentFilter = chosen ?? all;
            SelectedFilter = _currentFilter;
        });
        return chosen is not null;
    }

    private void ShowFailure(GameDefinition game, ErrorInfo error)
    {
        Clear();
        FooterText = null;
        Subtitle = game.Name;
        ErrorCard = new ErrorCardViewModel(error, _deps.Text, [new ErrorCardAction(_deps.Text.Message("history.retry"), RetryCommand, IsPrimary: true)]);
        State = HistoryState.Failed;
    }

    private void Show(GameDefinition game, IReadOnlyList<HistoryPoint> read)
    {
        var points = read.OrderByDescending(point => point.CapturedAt).ToList();
        ErrorCard = null;
        Subtitle = _deps.Text.Message("history.subtitle", ("game", game.Name), ("count", points.Count));
        _flowCommands.Clear();
        Timeline = BuildTimeline(game, points);
        Saves = BuildSaves(game, points);
        Groups = BuildGroups(game, points);
        State = points.Count == 0 ? HistoryState.Empty : HistoryState.Loaded;
    }

    private List<TimelineEntryViewModel> BuildTimeline(GameDefinition game, List<HistoryPoint> points)
    {
        var onTimeline = points.Where(point => point.Kind != HistoryPointKind.SaveOnly).ToList();
        return
        [
            .. onTimeline.Select((point, index) =>
            {
                var (headline, namesVersion) = _words.Headline(point);
                var detail = _words.TimelineDetail(point, namesVersion);
                var stamp = _words.Stamp(point.CapturedAt, point.InUse);
                return new TimelineEntryViewModel(
                    headline,
                    detail,
                    stamp,
                    point.InUse,
                    index == onTimeline.Count - 1,
                    _words.ItemName(headline, detail, stamp),
                    _deps.Text.Message("history.card.goBackAutomation", ("name", point.Name)),
                    BeginCommand(game.Id, new LaunchTarget.Point(point.Id)));
            }),
        ];
    }

    private List<SaveRowViewModel> BuildSaves(GameDefinition game, List<HistoryPoint> points) =>
        [
            .. points
                .Where(point => point.Kind != HistoryPointKind.VersionAndMods)
                .Select(point => new SaveRowViewModel(
                    point,
                    _words.SaveDetail(point),
                    _deps.Text.Message("history.save.loadAutomation", ("name", point.Name)),
                    _words.ItemName(point.Name, _words.SaveDetail(point), point.LoadedNow ? _deps.Text.Message("history.save.loaded") : string.Empty),
                    BeginCommand(game.Id, new LaunchTarget.Save(point.ProfileId, point.Id)))),
        ];

    private List<PointGroupViewModel> BuildGroups(GameDefinition game, List<HistoryPoint> points)
    {
        var now = _deps.Clock.GetUtcNow();
        var cards = points.Select(point => (Point: point, Card: new PointCardViewModel(
            point,
            HistoryText.Icon(point.Kind),
            _words.Contains(point),
            _words.Date(point.CapturedAt),
            _deps.Text.Message("history.card.goBackAutomation", ("name", point.Name)),
            _words.ItemName(point.Name, _words.Contains(point), _words.Date(point.CapturedAt), point.LoadedNow ? _deps.Text.Message("history.card.loadedNow") : string.Empty),
            BeginCommand(game.Id, new LaunchTarget.Point(point.Id))))).ToList();

        var thisWeek = cards.Where(item => now - item.Point.CapturedAt < _thisWeek).Select(item => item.Card).ToList();
        var earlier = cards.Where(item => now - item.Point.CapturedAt >= _thisWeek).Select(item => item.Card).ToList();
        List<PointGroupViewModel> groups = [];
        if (thisWeek.Count > 0)
        {
            groups.Add(new PointGroupViewModel(_deps.Text.Message("history.group.thisWeek"), thisWeek));
        }

        if (earlier.Count > 0)
        {
            groups.Add(new PointGroupViewModel(_deps.Text.Message("history.group.earlier"), earlier));
        }

        return groups;
    }

    private List<RemovedSnapshotViewModel> BuildRemoved(GameDefinition game, IReadOnlyList<RemovedSnapshot> removed) =>
        [
            .. removed.OrderByDescending(snapshot => snapshot.RemovedAt).Select(snapshot =>
            {
                var detail = _words.Contains(snapshot.Kind, snapshot.Install);
                var origin = _words.RemovedOrigin(snapshot);
                var when = _deps.Text.Message("history.removed.when", ("when", _deps.Text.ShortDateTime(snapshot.RemovedAt)));
                return new RemovedSnapshotViewModel(
                    snapshot,
                    detail,
                    origin,
                    when,
                    _deps.Text.Message("history.removed.bringBackAutomation", ("name", snapshot.Name)),
                    _words.ItemName(snapshot.Name, detail, origin, when),
                    new AsyncRelayCommand(() => BringBackAsync(game.Id, snapshot)));
            }),
        ];

    // Asks for the new profile's name; the player's answer decides, and the Api's refusal stays in the dialog until they give up or fix it.
    private async Task BringBackAsync(string gameId, RemovedSnapshot snapshot)
    {
        var dialog = new BringBackDialogViewModel(snapshot.Name, name => _deps.Profiles.BringBackAsync(gameId, snapshot.Id, name), _deps.Text);
        if (!await _deps.Dialogs.ShowAsync(dialog))
        {
            return;
        }

        // A profile exists now, so the shell's rail and the other screens read again too.
        await _shell.RefreshLibraryAsync();
        await ReloadAsync();
    }

    private ICommand BeginCommand(string gameId, LaunchTarget target)
    {
        var command = new FlowCommand(() => BeginAsync(gameId, target), () => CanBegin(gameId));
        _flowCommands.Add(command);
        return command.Command;
    }

    private bool CanBegin(string gameId) => !_shell.LaunchFlow.IsActive(gameId);

    // The banner, the running card and a failure's card are shown on the Games screen, so a launch that got going (or failed to) takes the player there.
    private async Task BeginAsync(string gameId, LaunchTarget target)
    {
        var flow = _shell.LaunchFlow;
        await flow.BeginAsync(gameId, target);
        if (_shell.Destination == NavDestination.History && (flow.IsActive(gameId) || flow.InlineContentFor(gameId) is not null))
        {
            _shell.Navigate(NavDestination.Games);
        }
    }

    private void RefreshFlowCommands()
    {
        foreach (var command in _flowCommands)
        {
            command.Refresh();
        }
    }

    // Sets properties the screen itself owns without that counting as a choice by the user.
    private void Restore(Action change)
    {
        _restoring = true;
        try
        {
            change();
        }
        finally
        {
            _restoring = false;
        }
    }
}
