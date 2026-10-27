using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Help;
using Savepoint.App.Features.Library;
using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using RunState = Savepoint.Core.Api.JobStatus;

namespace Savepoint.App.Features.Shell;

/// <summary>
/// The app shell: navigation between Games, History and Settings, the persisted layout, the game list with profile
/// counts and sizes, the selected game, and the job status that is visible in both layouts. Screens plug in through
/// <see cref="IScreenFactory"/> and talk to the shell through <see cref="IShellContext"/>.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IShellContext, IDisposable
{
    /// <summary>Below this window width the sidebar collapses to an icon rail.</summary>
    public const double CompactBelowWidth = 900;

    /// <summary>A window shorter than this, in device-independent pixels, compacts the library shelf.</summary>
    public const double ShortBelowHeight = 560;

    /// <summary>Width of the expanded rail.</summary>
    public const double ExpandedRailWidth = 236;

    /// <summary>Width of the icon rail.</summary>
    public const double CompactRailWidth = 64;

    private readonly ShellDependencies _deps;
    private readonly Dictionary<NavDestination, IScreen> _screens = [];

    private AppSettings? _settings;
    private NavDestination _destination = NavDestination.Games;
    private ShellLayout _layout = ShellLayout.Sidebar;
    private GameRowViewModel? _selectedRow;
    private NavItemViewModel _selectedNav;
    private object? _pane;
    private NavDestination _paneDestination;
    private (object Pane, NavDestination Destination)? _underHelp;
    private readonly TourViewModel _tour;
    private bool _tourOffered;
    private bool _isLoaded;
    private bool _libraryKnown;
    private LibraryErrorViewModel? _libraryError;
    private bool _isCompact;
    private bool _isShort;
    private bool _initialized;
    private bool _disposed;
    private bool _refreshRequested;
    private Task? _refreshLoop;
    private IReadOnlyDictionary<string, int>? _profileCounts;
    private IReadOnlyDictionary<string, long>? _bytes;

    /// <param name="dependencies">What the shell is built from.</param>
    public ShellViewModel(ShellDependencies dependencies)
    {
        _deps = dependencies;
        var text = dependencies.Text;
        Nav =
        [
            new NavItemViewModel(NavDestination.Games, text.Message("shell.nav.games"), Icons.StadiaController),
            new NavItemViewModel(NavDestination.History, text.Message("shell.nav.history"), Icons.History),
            new NavItemViewModel(NavDestination.Activity, text.Message("shell.nav.activity"), Icons.Sync),
            new NavItemViewModel(NavDestination.Settings, text.Message("shell.nav.settings"), Icons.Settings),
        ];
        _selectedNav = Nav[0];
        NavLabel = text.Message("shell.nav.label");
        _tour = new TourViewModel(text);
        _tour.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TourViewModel.IsActive))
            {
                OnPropertyChanged(nameof(ShowTour));
            }
        };
        FirstRun = new FirstRunViewModel(text, AddGameCommand, new AsyncRelayCommand(StartFromTemplateAsync), AddFromLibrariesCommand, new RelayCommand(ShowHelp));
        LayoutToggle = new LayoutToggleViewModel(text, SetLayoutAsync, _layout);
        JobStatus = new JobStatusViewModel(text, dependencies.Toasts, id => Games.FirstOrDefault(row => row.Id == id)?.Name, jobId => dependencies.LaunchFlow.ShowsOutcomeOf(jobId), ShowActivity);

        InitializeGameSearch();

        dependencies.LaunchFlow.SettingsSaved += OnFlowSettingsSaved;
        dependencies.Theme.LargestFittingScaleChanged += OnLargestFittingScaleChanged;
    }

    private void OnLargestFittingScaleChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(LargestFittingUiScale));

    /// <inheritdoc />
    public event EventHandler? LibraryRefreshed;

    /// <inheritdoc />
    public event EventHandler<AddGameRequest>? AddGameRequested;

    /// <inheritdoc />
    public event EventHandler<ActivityRequest>? ActivityRequested;

    /// <summary>The three destinations, in order.</summary>
    public IReadOnlyList<NavItemViewModel> Nav { get; }

    /// <summary>The navigation's accessible name ("Main").</summary>
    public string NavLabel { get; }

    /// <summary>The selected destination's item; setting it navigates.</summary>
    public NavItemViewModel? SelectedNav
    {
        get => _selectedNav;
        set
        {
            // A list box clears its selection while it rebuilds; telling it the value again puts the selection back.
            if (value is null)
            {
                OnPropertyChanged();
                return;
            }

            Navigate(value.Destination);
        }
    }

    /// <summary>The game rows, in list order.</summary>
    public ObservableCollection<GameRowViewModel> Games { get; } = [];

    /// <summary>The selected game's row; setting it selects that game without changing the screen, so arrowing through the list never leaves a half-filled page. Opening the game is <see cref="SelectGame"/>.</summary>
    public GameRowViewModel? SelectedGameRow
    {
        get => _selectedRow;
        set
        {
            if (value is null)
            {
                OnPropertyChanged();
                return;
            }

            Select(value);
            StartTourIfDue();
        }
    }

    /// <summary>The first-run empty state.</summary>
    public FirstRunViewModel FirstRun { get; }

    /// <summary>The job pill and its toasts.</summary>
    public JobStatusViewModel JobStatus { get; }

    /// <inheritdoc />
    public LayoutToggleViewModel LayoutToggle { get; }

    /// <summary>True once settings and the game list have been read (or the attempt has failed).</summary>
    public bool IsLoaded
    {
        get => _isLoaded;
        private set => SetProperty(ref _isLoaded, value);
    }

    /// <summary>True while the first-run empty state replaces the Games screen: the library was read and has no games.</summary>
    public bool ShowFirstRun => _libraryKnown && Games.Count == 0 && Destination == NavDestination.Games && !PaneVisible;

    /// <summary>The error that fills the content region while the library has never been read; null otherwise.</summary>
    public LibraryErrorViewModel? LibraryError => _libraryError;

    /// <summary>What fills the content region: a full pane, the library error, the first-run state or the current screen.</summary>
    public object? Content => PaneVisible ? _pane : _libraryError is not null ? _libraryError : ShowFirstRun ? FirstRun : CurrentScreen;

    /// <summary>The screen of the current destination; null until the shell has started.</summary>
    public IScreen? CurrentScreen => _screens.GetValueOrDefault(Destination);

    /// <summary>True for the sidebar layout.</summary>
    public bool IsSidebarLayout => Layout == ShellLayout.Sidebar;

    /// <summary>True for the library layout.</summary>
    public bool IsLibraryLayout => Layout == ShellLayout.Library;

    /// <summary>True while the library shelf shows: library layout, Games destination, at least one game, no pane.</summary>
    public bool ShowShelf => IsLibraryLayout && Destination == NavDestination.Games && Games.Count > 0 && !PaneVisible;

    /// <summary>True while the window is narrower than <see cref="CompactBelowWidth"/>.</summary>
    public bool IsCompact => _isCompact;

    /// <summary>True while the window is shorter than <see cref="ShortBelowHeight"/>; the library shelf drops its art to leave room for the page.</summary>
    public bool IsShort => _isShort;

    /// <summary>The rail's width for the current state.</summary>
    public double RailWidth => IsCompact ? CompactRailWidth : ExpandedRailWidth;

    /// <inheritdoc />
    public GameDefinition? SelectedGame => _selectedRow?.Game;

    /// <inheritdoc />
    IReadOnlyList<GameDefinition> IShellContext.Games => [.. Games.Select(row => row.Game)];

    /// <inheritdoc />
    public ShellLayout Layout => _layout;

    /// <inheritdoc />
    public int LargestFittingUiScale => _deps.Theme.LargestFittingScale;

    /// <inheritdoc />
    public NavDestination Destination => _destination;

    /// <inheritdoc />
    public ILaunchFlowHost LaunchFlow => _deps.LaunchFlow;

    /// <inheritdoc />
    public IArtworkProvider Artwork => _deps.Artwork;

    /// <summary>Opens the game editor in create mode.</summary>
    [RelayCommand]
    private void AddGame() => RequestAddGame();

    private bool PaneVisible => _pane is not null && _paneDestination == Destination;

    /// <summary>Reads settings and the library, builds the screens, shows the first one and starts following jobs. Calling it again does nothing.</summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        // Subscribe before reading the snapshot, so a job that changes in between is not missed.
        _deps.Jobs.JobChanged += OnJobChanged;

        var settings = await _deps.Settings.GetAsync();
        if (settings.Ok)
        {
            ApplySettings(settings.Value);
        }
        else
        {
            ShowError(settings.Error);
        }

        await RefreshLibraryAsync();

        // Seeded after the games are known, so a running job's pill already names its game.
        JobStatus.Seed(_deps.Jobs.List());

        foreach (var destination in Enum.GetValues<NavDestination>())
        {
            var factory = _deps.Screens.FirstOrDefault(candidate => candidate.Destination == destination);
            _screens[destination] = factory?.Create(this) ?? throw new InvalidOperationException($"No screen is registered for the {destination} destination.");
        }

        IsLoaded = true;
        RaiseShellState();
        CurrentScreen?.OnShown();
        StartTourIfDue();
    }

    /// <summary>Tells the shell how wide the window is; the sidebar collapses below <see cref="CompactBelowWidth"/>.</summary>
    public void SetWindowWidth(double width)
    {
        var compact = width < CompactBelowWidth;
        if (compact == _isCompact)
        {
            return;
        }

        _isCompact = compact;
        foreach (var item in Nav)
        {
            item.ShowLabel = !compact;
        }

        foreach (var row in Games)
        {
            row.ShowLabels = !compact;
        }

        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(RailWidth));
        OnPropertyChanged(nameof(RailAddHelpText));
        RefilterGames();
    }

    /// <summary>Tells the shell how tall the window is; the library shelf compacts below <see cref="ShortBelowHeight"/>.</summary>
    public void SetWindowHeight(double height)
    {
        var isShort = height < ShortBelowHeight;
        if (isShort == _isShort)
        {
            return;
        }

        _isShort = isShort;
        OnPropertyChanged(nameof(IsShort));
    }

    /// <inheritdoc />
    public void Navigate(NavDestination destination)
    {
        if (destination == _destination)
        {
            return;
        }

        CurrentScreen?.OnHidden();
        _destination = destination;
        _selectedNav = Nav.First(item => item.Destination == destination);
        OnPropertyChanged(nameof(SelectedNav));
        OnPropertyChanged(nameof(Destination));
        RaiseShellState();
        CurrentScreen?.OnShown();
        StartTourIfDue();
    }

    /// <inheritdoc />
    public void SelectGame(string gameId)
    {
        var row = Games.FirstOrDefault(candidate => candidate.Id == gameId);
        if (row is null)
        {
            return;
        }

        RevealGame(row);
        Select(row);

        // Choosing a game on purpose puts the player back on its screen, so the help page goes first.
        if (_pane is HowItWorksViewModel)
        {
            ClosePane();
        }

        if (Destination == NavDestination.Settings)
        {
            Navigate(NavDestination.Games);
        }

        StartTourIfDue();
    }

    /// <inheritdoc />
    public void ShowActivity(string? gameId)
    {
        // The game is selected and the screen told before it is shown, so it opens on the right jobs the first time.
        var row = gameId is null ? null : Games.FirstOrDefault(candidate => candidate.Id == gameId);
        if (row is not null)
        {
            RevealGame(row);
            Select(row);
        }

        ActivityRequested?.Invoke(this, new ActivityRequest(row?.Id));
        Navigate(NavDestination.Activity);
    }

    /// <inheritdoc />
    public Task RefreshLibraryAsync()
    {
        _refreshRequested = true;
        return _refreshLoop is { IsCompleted: false } running ? running : _refreshLoop = RunRefreshLoopAsync();
    }

    /// <inheritdoc />
    public async Task SetLayoutAsync(ShellLayout layout)
    {
        if (layout == _layout)
        {
            return;
        }

        _layout = layout;
        LayoutToggle.Sync(layout);
        OnLayoutChanged();

        var saved = await _deps.SettingsWriter.UpdateAsync(settings => settings with { Layout = layout }, OnLayoutSaved);
        if (saved.Ok)
        {
            return;
        }

        _layout = _settings?.Layout ?? ShellLayout.Sidebar;
        LayoutToggle.Sync(_layout);
        OnLayoutChanged();
        ShowError(saved.Error);
    }

    /// <inheritdoc />
    public void SettingsChanged(AppSettings settings) => ApplySettings(settings);

    /// <inheritdoc />
    public void RequestAddGame(GameDefinition? prefill = null)
    {
        Navigate(NavDestination.Games);
        AddGameRequested?.Invoke(this, new AddGameRequest(prefill));
    }

    /// <inheritdoc />
    public void ShowPane(object paneViewModel)
    {
        EndPane(except: paneViewModel);
        _pane = paneViewModel;
        _paneDestination = Destination;
        RaiseShellState();
    }

    /// <inheritdoc />
    public void ClosePane()
    {
        // Closing the help page gives back the pane it covered, unharmed; any other pane is simply closed.
        if (_pane is HowItWorksViewModel && _underHelp is { } covered)
        {
            _pane = covered.Pane;
            _paneDestination = covered.Destination;
            _underHelp = null;
            RaiseShellState();
            StartTourIfDue();
            return;
        }

        EndPane(except: null);
        RaiseShellState();
        StartTourIfDue();
    }

    /// <inheritdoc />
    public void ShowHelp() => OpenHelp(null);

    /// <inheritdoc />
    public void ShowHelp(HowItWorksSection section) => OpenHelp(section);

    private void OpenHelp(HowItWorksSection? section)
    {
        if (_pane is HowItWorksViewModel open)
        {
            if (section is { } requested)
            {
                open.ShowSection(requested);
            }

            return;
        }

        // A pane that is open (the game editor with a form half filled in) is kept, not replaced: ShowPane would end it.
        _underHelp = _pane is null ? null : (_pane, _paneDestination);
        _pane = new HowItWorksViewModel(_deps.Text, ClosePane, StartTour, () => CanShowTour, section);
        _paneDestination = Destination;
        RaiseShellState();
    }

    /// <summary>The first-run tour.</summary>
    public TourViewModel Tour => _tour;

    /// <summary>True while the tour's card and outline should show: the tour is on a step, the Games screen is in view and nothing covers it.</summary>
    public bool ShowTour => _tour.IsActive && Destination == NavDestination.Games && !PaneVisible;

    /// <inheritdoc />
    public bool CanShowTour => SelectedGameHasProfile;

    /// <inheritdoc />
    public void StartTour()
    {
        if (!CanShowTour)
        {
            return;
        }

        // The page that offered the tour closes, giving back any pane it covered; that pane keeps the tour waiting until it is closed.
        if (_pane is HowItWorksViewModel)
        {
            ClosePane();
        }

        _tourOffered = true;
        Navigate(NavDestination.Games);
        _tour.Start(_selectedRow!.Name);
    }

    private bool SelectedGameHasProfile => _selectedRow is { } row && _profileCounts?.GetValueOrDefault(row.Id) > 0;

    // The tour starts by itself once per run, when the Games screen is in view with a profile in it and the player has not seen it.
    // Whether it has run is written to the settings the moment it starts, so a player who closes the window mid-tour is not shown it again.
    private void StartTourIfDue()
    {
        if (_tourOffered || !IsLoaded || _tour.IsActive || _settings is not { TourSeen: false } || Destination != NavDestination.Games || PaneVisible || !SelectedGameHasProfile)
        {
            return;
        }

        _tourOffered = true;
        _tour.Start(_selectedRow!.Name);
        _ = _deps.SettingsWriter.UpdateAsync(settings => settings with { TourSeen = true }, saved => _settings = saved);
    }

    // A pane that listens for changes lets go of them when it is closed or replaced, and so does the one the help page covers.
    private void EndPane(object? except)
    {
        var ended = _pane;
        var covered = _underHelp?.Pane;
        _pane = null;
        _underHelp = null;
        if (!ReferenceEquals(ended, except))
        {
            (ended as IDisposable)?.Dispose();
        }

        if (!ReferenceEquals(covered, except))
        {
            (covered as IDisposable)?.Dispose();
        }
    }

    /// <summary>Stops following the Api's events and lets go of the screens it built, which stops their timers and reads.</summary>
    public void Dispose()
    {
        _disposed = true;
        GameSearch.Dispose();
        _deps.Jobs.JobChanged -= OnJobChanged;
        EndPane(except: null);
        foreach (var screen in _screens.Values)
        {
            (screen as IDisposable)?.Dispose();
        }

        (_deps.Artwork as IDisposable)?.Dispose();
        _deps.LaunchFlow.SettingsSaved -= OnFlowSettingsSaved;
        _deps.Theme.LargestFittingScaleChanged -= OnLargestFittingScaleChanged;
    }

    // Runs as each save lands, in the order they were stored, so a layout another change briefly undid is put back.
    private void OnLayoutSaved(AppSettings settings)
    {
        _settings = settings;
        if (settings.Layout != _layout)
        {
            _layout = settings.Layout;
            LayoutToggle.Sync(_layout);
            OnLayoutChanged();
        }
    }

    private void OnFlowSettingsSaved(object? sender, AppSettings settings) =>
        _deps.Dispatcher.Post(() =>
        {
            if (!_disposed)
            {
                ApplySettings(settings);
            }
        });

    private void OnJobChanged(object? sender, JobSnapshot job) =>
        _deps.Dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            JobStatus.Handle(job);
            if (job.Status != RunState.Running && job.Kind != JobKind.Launch)
            {
                _ = RefreshLibraryAsync();
            }
        });

    private void ApplySettings(AppSettings settings)
    {
        // Settings that say the tour has not run, after ones that said it had, are SavePoint's data having been removed: it may start again.
        if (_settings is { TourSeen: true } && !settings.TourSeen)
        {
            _tourOffered = false;
        }

        _settings = settings;
        _layout = settings.Layout;
        LayoutToggle.Sync(_layout);
        OnLayoutChanged();
        FirstRun.SetStorageRoot(settings.StorageRoot);
        FirstRun.SetArtworkLookUp(settings.LookUpArtworkOnline);
        _deps.Theme.Apply(settings.Theme, settings.AccentColorId);
        _deps.Theme.ApplyScale(settings.UiScalePercent);
        StartTourIfDue();
    }

    private void OnLayoutChanged()
    {
        OnPropertyChanged(nameof(Layout));
        OnPropertyChanged(nameof(IsSidebarLayout));
        OnPropertyChanged(nameof(IsLibraryLayout));
        OnPropertyChanged(nameof(ShowShelf));

        // Whether the games' search box shows depends on the layout and the width.
        RefilterGames();
    }

    private void RaiseShellState()
    {
        OnPropertyChanged(nameof(CurrentScreen));
        OnPropertyChanged(nameof(ShowFirstRun));
        OnPropertyChanged(nameof(RailAddHelpText));
        OnPropertyChanged(nameof(ShowShelf));
        OnPropertyChanged(nameof(LibraryError));
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(ShowTour));
    }

    // Overlapping requests collapse: while one reload runs, further requests only mark that another is due.
    private async Task RunRefreshLoopAsync()
    {
        while (_refreshRequested)
        {
            _refreshRequested = false;
            await ReloadLibraryAsync();
        }
    }

    private async Task ReloadLibraryAsync()
    {
        var gamesTask = _deps.Games.ListAsync();
        var profilesTask = _deps.Profiles.ListAsync();
        var summaryTask = _deps.Settings.GetStorageSummaryAsync();
        await Task.WhenAll(gamesTask, profilesTask, summaryTask);

        var games = await gamesTask;
        if (!games.Ok)
        {
            // A library that was never read leaves nothing to show, so the page says why and offers a retry; one already on screen stays.
            if (_libraryKnown)
            {
                ShowError(games.Error);
            }
            else
            {
                var tryAgain = new ErrorCardAction(_deps.Text.Message("shell.libraryError.tryAgain"), new AsyncRelayCommand(RefreshLibraryAsync), IsPrimary: true);
                _libraryError = new LibraryErrorViewModel(new ErrorCardViewModel(games.Error, _deps.Text, [tryAgain]));
                RaiseShellState();
            }

            return;
        }

        var profiles = await profilesTask;
        if (profiles.Ok)
        {
            _profileCounts = profiles.Value.GroupBy(profile => profile.GameId).ToDictionary(group => group.Key, group => group.Count());
        }
        else
        {
            ShowError(profiles.Error);
        }

        // A size is a nicety; when it cannot be read the card shows the count alone and nothing else breaks.
        var summary = await summaryTask;
        if (summary.Ok)
        {
            _bytes = summary.Value.Games.ToDictionary(game => game.GameId, game => game.Bytes);
        }

        _libraryKnown = true;
        _libraryError = null;
        RaiseShellState();
        Rebuild(games.Value);
        LibraryRefreshed?.Invoke(this, EventArgs.Empty);
        StartTourIfDue();
    }

    private void Rebuild(IReadOnlyList<GameDefinition> games)
    {
        var previous = _selectedRow;
        var previousGame = previous?.Game;

        // Rows are matched by game id and updated in place, so a refresh never replaces the row a person has focused in the rail or shelf.
        var existing = Games.ToDictionary(row => row.Id);
        var rows = new List<GameRowViewModel>(games.Count);
        foreach (var game in games)
        {
            var profileCount = _profileCounts?.GetValueOrDefault(game.Id);
            long? size = _bytes is not null && _bytes.TryGetValue(game.Id, out var bytes) ? bytes : null;
            if (existing.TryGetValue(game.Id, out var row))
            {
                row.Apply(game, profileCount, size, _deps.Text);
            }
            else
            {
                row = new GameRowViewModel(game, profileCount, size, _deps.Text, Artwork.For(game.Id)) { ShowLabels = !_isCompact };
            }

            rows.Add(row);
        }

        for (var index = Games.Count - 1; index >= 0; index--)
        {
            if (!rows.Contains(Games[index]))
            {
                Games.RemoveAt(index);
            }
        }

        for (var index = 0; index < rows.Count; index++)
        {
            if (index < Games.Count && ReferenceEquals(Games[index], rows[index]))
            {
                continue;
            }

            var at = Games.IndexOf(rows[index]);
            if (at >= 0)
            {
                Games.Move(at, index);
            }
            else
            {
                Games.Insert(index, rows[index]);
            }
        }

        // History is about one game's points, so it has nothing to show in an empty library.
        Nav.First(item => item.Destination == NavDestination.History).IsEnabled = rows.Count > 0;

        // When the selected game is gone, the next one is the first the search lists, so the list shows what is selected.
        var selected = rows.FirstOrDefault(row => row.Id == previous?.Id) ?? ListedBySearch(rows).FirstOrDefault() ?? rows.FirstOrDefault();
        Select(selected, force: !Equals(previousGame, selected?.Game));
        RefilterGames();
        RaiseShellState();
        JobStatus.Refresh();
    }

    private void Select(GameRowViewModel? row, bool force = false)
    {
        foreach (var candidate in Games)
        {
            candidate.IsSelected = candidate == row;
        }

        if (row == _selectedRow && !force)
        {
            return;
        }

        _selectedRow = row;
        OnPropertyChanged(nameof(SelectedGameRow));
        OnPropertyChanged(nameof(SelectedGame));
    }

    /// <summary>Looks through the Steam and GOG Galaxy libraries and adds the games the player checks, after one confirmation.</summary>
    [RelayCommand]
    private async Task AddFromLibrariesAsync()
    {
        var flow = new LibraryImportFlow(_deps.Games, _deps.Dialogs, _deps.Text, _deps.LibraryRuns);
        var summary = await flow.RunAsync();
        if (flow.TemplateListRequested)
        {
            await StartFromTemplateAsync();
            return;
        }

        if (summary?.Added.FirstOrDefault()?.GameId is not { } firstAdded)
        {
            return;
        }

        await RefreshLibraryAsync();
        Navigate(NavDestination.Games);
        SelectGame(firstAdded);
    }

    private async Task StartFromTemplateAsync()
    {
        var choice = await _deps.Dialogs.ShowAsync(new TemplatePickerViewModel(_deps.Games.Templates, _deps.Text));
        switch (choice)
        {
            case TemplateChoice.Template template:
                RequestAddGame(template.Chosen.ToDraft());
                break;
            case TemplateChoice.File:
                await ImportDefinitionFileAsync();
                break;
        }
    }

    private async Task ImportDefinitionFileAsync()
    {
        var filters = new[] { new FileTypeFilter(_deps.Text.Message("templatePicker.fileType"), ["*.json"]) };
        var path = await _deps.Dialogs.PickOpenFileAsync(new OpenFilePickRequest(_deps.Text.Message("templatePicker.title"), filters));
        if (path is null)
        {
            return;
        }

        var imported = await _deps.Games.ImportDefinitionAsync(path);
        if (imported.Ok)
        {
            RequestAddGame(imported.Value);
        }
        else
        {
            ShowError(imported.Error);
        }
    }

    private void ShowError(ErrorInfo error) =>
        _deps.Toasts.Show(new ToastMessage(_deps.Text.Message(error), ToastKind.Error));
}
