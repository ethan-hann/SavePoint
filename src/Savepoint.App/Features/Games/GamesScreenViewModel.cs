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
    private bool _flowActive;
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
    [NotifyPropertyChangedFor(nameof(FailureContent))]
    [NotifyPropertyChangedFor(nameof(HasFailureContent))]
    private object? _inlineContent;

    /// <param name="shell">What the screen sees of the shell.</param>
    /// <param name="dependencies">What the screen is built from.</param>
    public GamesScreenViewModel(IShellContext shell, GamesDependencies dependencies)
    {
        _shell = shell;
        _deps = dependencies;
        LayoutToggle = shell.LayoutToggle;
        ShowHistoryCommand = new RelayCommand(() => _shell.Navigate(NavDestination.History));
        ShowActivityCommand = new RelayCommand(() => _shell.ShowActivity(_shell.SelectedGame?.Id));
        FreeUp = new FreeUpSpaceViewModel(dependencies.Games, dependencies.Jobs, dependencies.Dialogs, dependencies.Text, dependencies.Dispatcher, ShowError, RefreshStorageUsageAsync);
        EditGameCommand = new RelayCommand(OpenEditor);
        NewProfileCommand = new AsyncRelayCommand(NewProfileAsync);
        DeleteGameCommand = new AsyncRelayCommand(DeleteGameAsync);

        _shell.PropertyChanged += OnShellChanged;
        _shell.LibraryRefreshed += OnLibraryRefreshed;
        _shell.AddGameRequested += OnAddGameRequested;
        _shell.LaunchFlow.PropertyChanged += OnFlowChanged;
        _shell.LaunchFlow.InlineContentChanged += OnInlineContentChanged;
        _shell.LaunchFlow.EditGameRequested += OnEditGameRequested;

        ShowGame(_shell.SelectedGame);
    }

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

    /// <summary>The launch flow's banner, checking, saving or running card, which is short enough to stay pinned above the storage footer; null when there is none.</summary>
    public object? PinnedContent => InlineContent is ErrorCardViewModel ? null : InlineContent;

    /// <summary>True while <see cref="PinnedContent"/> has a card to show.</summary>
    public bool HasPinnedContent => PinnedContent is not null;

    /// <summary>A failure or partial-result card, shown below the table where it can use the pane's height; pinned, it would squeeze the table to nothing in a short window. Null when there is none.</summary>
    public ErrorCardViewModel? FailureContent => InlineContent as ErrorCardViewModel;

    /// <summary>True while <see cref="FailureContent"/> has a card to show.</summary>
    public bool HasFailureContent => FailureContent is not null;

    /// <summary>Switches to the History destination for the selected game.</summary>
    public IRelayCommand ShowHistoryCommand { get; }

    /// <summary>Opens Activity on the selected game, from the Activity chip.</summary>
    public IRelayCommand ShowActivityCommand { get; }

    /// <summary>The storage footer's Free up space for the selected game.</summary>
    public FreeUpSpaceViewModel FreeUp { get; }

    /// <summary>Opens the game editor pane for the selected game.</summary>
    public IRelayCommand EditGameCommand { get; }

    /// <summary>Opens the New profile wizard.</summary>
    public IAsyncRelayCommand NewProfileCommand { get; }

    /// <summary>Deletes the selected game once its name has been typed to confirm.</summary>
    public IAsyncRelayCommand DeleteGameCommand { get; }

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
            ClearRows();
            StorageFooterText = string.Empty;
            LoadError = null;
            _loaded = false;
        }

        IsLoading = true;
        RaiseTableState();

        var listTask = _deps.Profiles.ListAsync(game.Id, cts.Token);
        var statsTask = _deps.Profiles.GetRowStatsAsync(game.Id, cts.Token);
        var usageTask = _deps.Games.GetStorageUsageAsync(game.Id, cts.Token);
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

        if (generation != _generation || _disposed)
        {
            return;
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

    /// <summary>Stops following the shell and the launch flow.</summary>
    public void Dispose()
    {
        _disposed = true;
        _loadCts?.Cancel();
        FreeUp.Dispose();
        _shell.PropertyChanged -= OnShellChanged;
        _shell.LibraryRefreshed -= OnLibraryRefreshed;
        _shell.AddGameRequested -= OnAddGameRequested;
        _shell.LaunchFlow.PropertyChanged -= OnFlowChanged;
        _shell.LaunchFlow.InlineContentChanged -= OnInlineContentChanged;
        _shell.LaunchFlow.EditGameRequested -= OnEditGameRequested;
    }

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

    private void OpenEditor()
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
    }

    private void ClearRows()
    {
        Profiles.Clear();
        RaiseTableState();
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
            if (existing.TryGetValue(profile.Id, out var row))
            {
                row.Apply(profile, current);
            }
            else
            {
                row = new ProfileRowViewModel(profile, current, _deps.Text, Columns, this);
            }

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
    }

    // Play needs no session for the game and no profile already running.
    private void RefreshPlayAvailability()
    {
        var canPlay = _game is { } game
            && !_shell.LaunchFlow.IsActive(game.Id)
            && !Profiles.Any(row => row.IsRunning);
        foreach (var row in Profiles)
        {
            row.SetCanPlay(canPlay);
        }
    }

    private ErrorCardViewModel LoadFailure(ErrorInfo error) =>
        new(error, _deps.Text, [new ErrorCardAction(_deps.Text.Message("games.loadError.tryAgain"), new AsyncRelayCommand(ReloadAsync), IsPrimary: true)]);

    private void ShowError(ErrorInfo error) => _deps.Toasts.Show(new ToastMessage(_deps.Text.Message(error), ToastKind.Error));
}
