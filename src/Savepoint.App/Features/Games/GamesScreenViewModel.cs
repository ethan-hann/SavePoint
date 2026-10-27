using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Games;

/// <summary>
/// The Games screen: the selected game's header, its profile table, whatever the launch flow shows (its banner and running
/// card at the bottom of the pane, a failure card below the table), and the storage footer. It follows the shell's selected game, reloads when the library is refreshed, and turns
/// Play into the launch flow, Edit game into the editor pane and New profile into the wizard.
/// </summary>
public sealed partial class GamesScreenViewModel : ObservableObject, IScreen, IProfileRowActions, IDisposable
{
    private readonly IShellContext _shell;
    private readonly GamesDependencies _deps;

    private GameDefinition? _game;
    private string? _shownGameId;
    private bool _loaded;
    private bool _disposed;

    // Until the view reports a width there is nothing to squeeze, so the header keeps its words.
    private double _headerWidth = double.PositiveInfinity;
    private bool _cardsScroll;
    private bool _flowActive;
    private IReadOnlyList<RepoRole> _folderRoles = [];
    private long _generation;
    private CancellationTokenSource? _loadCts;

    /// <summary>The selected game's name; empty without a game.</summary>
    [ObservableProperty]
    private string _gameName = string.Empty;

    /// <summary>The selected game's artwork for the header tile; null without a game.</summary>
    [ObservableProperty]
    private GameArt? _art;

    /// <summary>The selected game's install folder, shown in monospace under its name.</summary>
    [ObservableProperty]
    private string _installPath = string.Empty;

    /// <summary>"Skyrim Special Edition — profiles", the library layout's header.</summary>
    [ObservableProperty]
    private string _libraryTitle = string.Empty;

    /// <summary>True while the profile list or the row stats are being read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    private bool _isLoading;

    /// <summary>The card shown instead of the table when the profile list could not be read; null otherwise.</summary>
    [ObservableProperty]
    private ErrorCardViewModel? _loadError;

    /// <summary>"This game is using 41.2 GB of your 210 GB storage folder"; empty when the use could not be read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStorageFooter))]
    [NotifyPropertyChangedFor(nameof(ShowStorageFooter))]
    private string _storageFooterText = string.Empty;

    /// <summary>What the launch flow shows for the selected game: its banner, checking, saving or running card, or a failure card; null when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInlineContent))]
    [NotifyPropertyChangedFor(nameof(PinnedContent))]
    [NotifyPropertyChangedFor(nameof(HasPinnedContent))]
    [NotifyPropertyChangedFor(nameof(ScrollingContent))]
    [NotifyPropertyChangedFor(nameof(HasScrollingContent))]
    private object? _inlineContent;

    /// <param name="shell">What the screen sees of the shell.</param>
    /// <param name="dependencies">What the screen is built from.</param>
    public GamesScreenViewModel(IShellContext shell, GamesDependencies dependencies)
    {
        _shell = shell;
        _deps = dependencies;
        LayoutToggle = shell.LayoutToggle;
        FreeUp = new FreeUpSpaceViewModel(dependencies.Games, dependencies.Jobs, dependencies.Dialogs, dependencies.Text, dependencies.Dispatcher, ShowError, RefreshStorageUsageAsync, dependencies.Clock);
        InitializeProfileSearch();

        _shell.PropertyChanged += OnShellChanged;
        _shell.LibraryRefreshed += OnLibraryRefreshed;
        _shell.AddGameRequested += OnAddGameRequested;
        _shell.LaunchFlow.PropertyChanged += OnFlowChanged;
        _shell.LaunchFlow.InlineContentChanged += OnInlineContentChanged;
        _shell.LaunchFlow.EditGameRequested += OnEditGameRequested;
        _deps.Jobs.JobChanged += OnJobChanged;

        ShowGame(_shell.SelectedGame);
    }

    /// <summary>The shortest pane that keeps a launch card pinned; the toast stack and the pill can take a third of a shorter one.</summary>
    public const double PinnedCardMinHeight = 300;

    /// <inheritdoc />
    public NavDestination Destination => NavDestination.Games;

    /// <summary>The profiles of the selected game, in catalog order.</summary>
    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = [];

    /// <summary>Which optional table columns fit.</summary>
    public ProfileTableColumns Columns { get; } = new();

    /// <summary>Whether the header shows its buttons' words or only their icons.</summary>
    public GameHeaderLayout Header { get; } = new();

    /// <summary>The Sidebar / Library toggle shown in the sidebar layout's header.</summary>
    public LayoutToggleViewModel LayoutToggle { get; }

    /// <summary>True while a game is selected; without one the shell shows its first-run state instead.</summary>
    public bool HasGame => _game is not null;

    /// <summary>True while the shell shows the sidebar layout.</summary>
    public bool IsSidebarLayout => _shell.Layout == ShellLayout.Sidebar;

    /// <summary>True while the shell shows the library layout, whose header is a title rather than the game's art and path.</summary>
    public bool IsLibraryLayout => _shell.Layout == ShellLayout.Library;

    /// <summary>True when the profile table has rows to show.</summary>
    public bool ShowTable => HasGame && LoadError is null && Profiles.Count > 0;

    /// <summary>True while the first read of a game's profiles has nothing to show yet, which is when the screen says it is loading.</summary>
    public bool ShowLoading => HasGame && IsLoading && Profiles.Count == 0 && LoadError is null;

    /// <summary>True when the game has no profiles yet and the screen prompts for the first one.</summary>
    public bool ShowEmptyState => HasGame && _loaded && LoadError is null && Profiles.Count == 0;

    /// <summary>True when the storage footer has something to say.</summary>
    public bool HasStorageFooter => StorageFooterText.Length > 0;

    /// <summary>True when the storage footer is drawn: it has something to say and the layout has room for it (the library shelf shows each game's size itself).</summary>
    public bool ShowStorageFooter => HasStorageFooter && IsSidebarLayout;

    /// <summary>The accessible name of the header's overflow button ("More actions for Skyrim Special Edition").</summary>
    public string GameMoreActionsName => _game is { } game ? _deps.Text.Message("games.profileTable.moreActions", ("name", game.Name)) : string.Empty;

    /// <summary>True while the launch flow has anything to show for the selected game.</summary>
    public bool HasInlineContent => InlineContent is not null;

    /// <summary>The launch flow's banner, checking, saving or running card, which is short enough to stay pinned above the storage footer while the pane is tall enough; null when there is none or the card scrolls with the profiles.</summary>
    public object? PinnedContent => InlineContent is null || ScrollsWithProfiles ? null : InlineContent;

    /// <summary>True while <see cref="PinnedContent"/> has a card to show.</summary>
    public bool HasPinnedContent => PinnedContent is not null;

    /// <summary>A failure or partial-result card, or any card in a pane too short to pin it, shown below the table where it can use the pane's height; pinned, it would squeeze the table to nothing. Null when there is none.</summary>
    public object? ScrollingContent => InlineContent is not null && ScrollsWithProfiles ? InlineContent : null;

    /// <summary>True while <see cref="ScrollingContent"/> has a card to show.</summary>
    public bool HasScrollingContent => ScrollingContent is not null;

    private bool ScrollsWithProfiles => InlineContent is ErrorCardViewModel || _cardsScroll;

    /// <summary>Switches to the History destination for the selected game.</summary>
    [RelayCommand]
    private void ShowHistory() => _shell.Navigate(NavDestination.History);

    /// <summary>Opens Activity on the selected game, from the Activity chip.</summary>
    [RelayCommand]
    private void ShowActivity() => _shell.ShowActivity(_shell.SelectedGame?.Id);

    /// <summary>The storage footer's Free up space for the selected game.</summary>
    public FreeUpSpaceViewModel FreeUp { get; }

    /// <summary>True when the tab row offers a snapshot: a profile is loaded in the game's folders, or exactly one profile has no snapshot yet.</summary>
    public bool HasLoadedProfile => LoadedProfile is not null;

    /// <summary>The Take a snapshot button's label.</summary>
    public string TakeSnapshotText => _deps.Text.Message("games.header.takeSnapshot");

    /// <summary>
    /// Takes a snapshot for <see cref="LoadedProfile"/> through that row's own command, so it is off, and asks first, exactly when
    /// the row's menu entry is and does: off while the game has a session or a snapshot is being taken, and a confirmation for a
    /// profile whose first snapshot this would be.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTakeSnapshot))]
    private Task TakeSnapshotAsync() => LoadedProfile?.TakeSnapshotCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    private bool CanTakeSnapshot => LoadedProfile?.TakeSnapshotCommand.CanExecute(null) == true;

    // The loaded profile, or else the one profile that has no snapshot yet (its first one is whatever the folders hold, which the
    // row confirms). With several such profiles there is no telling which the player means, so none is offered.
    private ProfileRowViewModel? LoadedProfile =>
        Profiles.FirstOrDefault(row => row.SnapshotNow == SnapshotNowAvailability.Available)
        ?? (Profiles.Count(row => row.SnapshotNow == SnapshotNowAvailability.FirstSnapshot) == 1
            ? Profiles.Single(row => row.SnapshotNow == SnapshotNowAvailability.FirstSnapshot)
            : null);

    /// <inheritdoc />
    public void OnShown()
    {
        FreeUp.ClearResult();
        _ = ReloadAsync();
    }

    /// <summary>
    /// Reads the selected game's profiles, row stats and storage use again. A read that finishes after the selection
    /// moved on, or after a newer reload started, is dropped, so one game's rows never land under another's header.
    /// </summary>
    public async Task ReloadAsync()
    {
        if (_disposed)
        {
            return;
        }

        var generation = ++_generation;
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();

        var game = _shell.SelectedGame;
        var switched = game?.Id != _shownGameId;
        ShowGame(game);
        if (game is null)
        {
            ClearRows();
            IsLoading = false;
            return;
        }

        if (switched)
        {
            ClearProfileSearch();
            ClearRows();
            StorageFooterText = string.Empty;
            LoadError = null;
            _folderRoles = [];
            _loaded = false;
        }

        IsLoading = true;
        RaiseTableState();

        var listTask = _deps.Profiles.ListAsync(game.Id, cts.Token);
        var statsTask = _deps.Profiles.GetRowStatsAsync(game.Id, cts.Token);
        var usageTask = _deps.Games.GetStorageUsageAsync(game.Id, cts.Token);
        var foldersTask = _deps.Games.GetFolderStatusAsync(game.Id, cts.Token);
        var list = await listTask;
        if (generation != _generation || _disposed)
        {
            return;
        }

        // The catalog answers at once and the status scan of both folders can take seconds, so a game's first rows show from the
        // list alone and fill in their status, version and mod files when the stats land. A reload keeps the rows it has meanwhile.
        if (list.Ok && Profiles.Count == 0 && list.Value.Count > 0)
        {
            _loaded = true;
            LoadError = null;
            ApplyRows(list.Value, []);
            RaiseTableState();
        }

        var stats = await statsTask;
        var usage = await usageTask;
        var folders = await foldersTask;

        if (generation != _generation || _disposed)
        {
            return;
        }

        // A failed read keeps the folders the rows already offer; the menu entries are not worth an error toast of their own.
        if (folders.Ok)
        {
            _folderRoles = [.. folders.Value.Select(folder => folder.Role)];
        }

        IsLoading = false;
        _loaded = true;
        StorageFooterText = usage.Ok ? StorageFooter(usage.Value) : string.Empty;

        if (!list.Ok)
        {
            ClearRows();
            LoadError = LoadFailure(list.Error);
            RaiseTableState();
            return;
        }

        // Without stats the rows still list, so a profile can be edited or deleted even when its git state cannot be read.
        if (!stats.Ok)
        {
            ShowError(stats.Error);
        }

        LoadError = null;
        ApplyRows(list.Value, stats.Ok ? stats.Value : []);
        RefreshPlayAvailability();
        RaiseTableState();
    }

    /// <summary>Tells the screen how wide its profile table is, so it can drop columns that do not fit.</summary>
    /// <param name="width">The table's width in device-independent pixels.</param>
    public void SetTableWidth(double width) => Columns.Update(width);

    /// <summary>Tells the screen how wide its header is, so the game's name keeps its room.</summary>
    /// <param name="width">The header's width in device-independent pixels.</param>
    public void SetHeaderWidth(double width)
    {
        _headerWidth = width;
        Header.Update(width, IsSidebarLayout);
    }

    /// <summary>
    /// Tells the screen how tall its pane is. Below <see cref="PinnedCardMinHeight"/> the launch flow's card scrolls with the profiles
    /// instead of being pinned, because the toasts and the job pill take the room it would stand in.
    /// </summary>
    /// <param name="height">The pane's height in device-independent pixels.</param>
    public void SetPaneHeight(double height)
    {
        var scrolls = height > 0 && height < PinnedCardMinHeight;
        if (scrolls == _cardsScroll)
        {
            return;
        }

        _cardsScroll = scrolls;
        OnPropertyChanged(nameof(PinnedContent));
        OnPropertyChanged(nameof(HasPinnedContent));
        OnPropertyChanged(nameof(ScrollingContent));
        OnPropertyChanged(nameof(HasScrollingContent));
    }

    /// <summary>Stops following the shell and the launch flow.</summary>
    public void Dispose()
    {
        _disposed = true;
        _loadCts?.Cancel();
        ProfileSearch.Dispose();
        FreeUp.Dispose();
        _shell.PropertyChanged -= OnShellChanged;
        _shell.LibraryRefreshed -= OnLibraryRefreshed;
        _shell.AddGameRequested -= OnAddGameRequested;
        _shell.LaunchFlow.PropertyChanged -= OnFlowChanged;
        _shell.LaunchFlow.InlineContentChanged -= OnInlineContentChanged;
        _shell.LaunchFlow.EditGameRequested -= OnEditGameRequested;
        _deps.Jobs.JobChanged -= OnJobChanged;
    }

    // Jobs raise their events from any thread. Only a snapshot of this game matters here: while one runs, no second one starts.
    private void OnJobChanged(object? sender, JobSnapshot job) =>
        _deps.Dispatcher.Post(() =>
        {
            if (!_disposed && job.Kind == JobKind.Capture && job.GameId == _game?.Id)
            {
                RefreshPlayAvailability();
            }
        });

    private bool SnapshotRunning(GameDefinition? game) =>
        game is not null && _deps.Jobs.List().Any(job => job.Kind == JobKind.Capture && job.GameId == game.Id && job.Status == JobStatus.Running);

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IShellContext.SelectedGame):
                _ = ReloadAsync();
                break;
            case nameof(IShellContext.Layout):
                Header.Update(_headerWidth, IsSidebarLayout);
                OnPropertyChanged(nameof(IsSidebarLayout));
                OnPropertyChanged(nameof(IsLibraryLayout));
                OnPropertyChanged(nameof(ShowStorageFooter));
                break;
        }
    }

    private string StorageFooter(GameStorageUsage usage) =>
        _deps.Text.Message("games.storageFooter.usage", ("used", _deps.Text.FileSize(usage.BytesUsed)), ("total", _deps.Text.FileSize(usage.VolumeTotalBytes)));

    // After a free-up run the footer shows what the game uses now; the rest of the screen has not changed.
    private async Task RefreshStorageUsageAsync()
    {
        if (_game is not { } game)
        {
            return;
        }

        var usage = await _deps.Games.GetStorageUsageAsync(game.Id);
        if (usage.Ok && !_disposed && game.Id == _game?.Id)
        {
            StorageFooterText = StorageFooter(usage.Value);
        }
    }

    private void OnLibraryRefreshed(object? sender, EventArgs e) => _ = ReloadAsync();

    private void OnAddGameRequested(object? sender, AddGameRequest request) =>
        _shell.ShowPane(_deps.Editor.CreateForNew(_shell, request.Prefill));

    // The flow raises its events from any thread, so each one is handed to the dispatcher.
    private void OnFlowChanged(object? sender, PropertyChangedEventArgs e) =>
        _deps.Dispatcher.Post(() =>
        {
            RefreshPlayAvailability();

            // A session that starts or ends with no job and no new inline content (a skipped save) still changes which profile is running.
            var active = FlowOwns(_game);
            if (!_disposed && active != _flowActive)
            {
                _flowActive = active;
                _ = ReloadAsync();
            }
        });

    private void OnInlineContentChanged(object? sender, string gameId) =>
        _deps.Dispatcher.Post(() =>
        {
            if (_disposed || gameId != _game?.Id)
            {
                return;
            }

            RefreshInlineContent();
            RefreshPlayAvailability();

            // A session starting or ending changes which profile is active and when it was played.
            _ = ReloadAsync();
        });

    /// <summary>Opens the game editor pane for the selected game.</summary>
    [RelayCommand]
    private void EditGame()
    {
        if (_game is { } game)
        {
            _shell.ShowPane(_deps.Editor.CreateForExisting(_shell, game.Id));
        }
    }

    // A failure the editor fixes can come from any screen (a launch started from History), so the editor opens here, on the game it is about.
    private void OnEditGameRequested(object? sender, string gameId) =>
        _deps.Dispatcher.Post(() =>
        {
            if (_disposed || _shell.Games.All(game => game.Id != gameId))
            {
                return;
            }

            _shell.SelectGame(gameId);
            _shell.Navigate(NavDestination.Games);
            _shell.ShowPane(_deps.Editor.CreateForExisting(_shell, gameId));
        });

    private bool FlowOwns(GameDefinition? game) => game is not null && _shell.LaunchFlow.IsActive(game.Id);

    private void ShowGame(GameDefinition? game)
    {
        _game = game;
        _flowActive = FlowOwns(game);
        _shownGameId = game?.Id;
        FreeUp.SetGame(game?.Id, game?.Name);
        GameName = game?.Name ?? string.Empty;
        Art = game is null ? null : _shell.Artwork.For(game.Id);
        InstallPath = game?.Install.Path ?? string.Empty;
        LibraryTitle = game is null ? string.Empty : _deps.Text.Message("games.header.libraryTitle", ("name", game.Name));
        RefreshInlineContent();
        OnPropertyChanged(nameof(HasGame));
        OnPropertyChanged(nameof(GameMoreActionsName));
        RaiseTableState();
    }

    private void RefreshInlineContent() =>
        InlineContent = _game is { } game ? _shell.LaunchFlow.InlineContentFor(game.Id) : null;

    private void RaiseTableState()
    {
        OnPropertyChanged(nameof(ShowTable));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowLoading));
        RaiseSearchState();
    }

    private void ClearRows()
    {
        Profiles.Clear();
        RefilterProfiles();
        RaiseTableState();
        RefreshSnapshotHeader();
    }

    // The header button follows whichever row is loaded and that row's own availability.
    private void RefreshSnapshotHeader()
    {
        OnPropertyChanged(nameof(HasLoadedProfile));
        TakeSnapshotCommand.NotifyCanExecuteChanged();
    }

    // Rows are matched by profile id and updated in place; the collection only changes when the profiles themselves do.
    private void ApplyRows(IReadOnlyList<Profile> profiles, IReadOnlyList<ProfileRowStats> stats)
    {
        var statsById = new Dictionary<string, ProfileRowStats>();
        foreach (var entry in stats)
        {
            statsById[entry.ProfileId] = entry;
        }

        var existing = Profiles.ToDictionary(row => row.Id);
        var rows = new List<ProfileRowViewModel>(profiles.Count);
        foreach (var profile in profiles)
        {
            var current = statsById.TryGetValue(profile.Id, out var found)
                ? found
                : new ProfileRowStats(profile.Id, false, [], null, null, null, profile.VersionOverride, profile.LastLaunchedAt, false);
            var known = statsById.ContainsKey(profile.Id);
            if (existing.TryGetValue(profile.Id, out var row))
            {
                row.Apply(profile, current, known);
            }
            else
            {
                row = new ProfileRowViewModel(profile, current, _deps.Text, Columns, this, known);

                // The header button gates on the loaded row's command, which also turns itself off while its own run is under way.
                row.TakeSnapshotCommand.CanExecuteChanged += OnRowSnapshotChanged;
            }

            row.SetFolders(_folderRoles);
            rows.Add(row);
        }

        if (!Profiles.SequenceEqual(rows))
        {
            Profiles.Clear();
            foreach (var row in rows)
            {
                Profiles.Add(row);
            }
        }

        // A profile that just arrived (made, duplicated, brought back) must show, and a search that hides it would make it look lost.
        if (ProfileSearch.HasText && rows.Any(row => !existing.ContainsKey(row.Id)))
        {
            ClearProfileSearch();
        }

        RefilterProfiles();
        RefreshSnapshotHeader();
    }

    private void OnRowSnapshotChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            TakeSnapshotCommand.NotifyCanExecuteChanged();
        }
    }

    // Play needs no session for the game and no profile already running.
    private void RefreshPlayAvailability()
    {
        var gameRunning = Profiles.Any(row => row.IsRunning);
        var canPlay = _game is { } game
            && !_shell.LaunchFlow.IsActive(game.Id)
            && !gameRunning;
        var snapshotRunning = SnapshotRunning(_game);
        foreach (var row in Profiles)
        {
            row.SetCanPlay(canPlay, gameRunning);
            row.SetSnapshotBusy(snapshotRunning);
        }

        RefreshSnapshotHeader();
    }

    private ErrorCardViewModel LoadFailure(ErrorInfo error) =>
        new(error, _deps.Text, [new ErrorCardAction(_deps.Text.Message("games.loadError.tryAgain"), new AsyncRelayCommand(ReloadAsync), IsPrimary: true)]);

    private void ShowError(ErrorInfo error, ErrorOperation operation = ErrorOperation.Unspecified) =>
        _deps.Toasts.Show(new ToastMessage(_deps.Text.Message(error, operation), ToastKind.Error));
}
