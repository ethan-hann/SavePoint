using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Games;

/// <summary>
/// The Games screen: the selected game's header, its profile table, whatever the launch flow shows above the table,
/// and the storage footer. It follows the shell's selected game, reloads when the library is refreshed, and turns
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
    private long _generation;
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    private string _gameName = string.Empty;

    [ObservableProperty]
    private string _installPath = string.Empty;

    [ObservableProperty]
    private string _libraryTitle = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ErrorCardViewModel? _loadError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStorageFooter))]
    private string _storageFooterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInlineContent))]
    private object? _inlineContent;

    /// <param name="shell">What the screen sees of the shell.</param>
    /// <param name="dependencies">What the screen is built from.</param>
    public GamesScreenViewModel(IShellContext shell, GamesDependencies dependencies)
    {
        _shell = shell;
        _deps = dependencies;
        LayoutToggle = shell.LayoutToggle;
        ShowHistoryCommand = new RelayCommand(() => _shell.Navigate(NavDestination.History));
        EditGameCommand = new RelayCommand(OpenEditor, () => CanEditGame);
        NewProfileCommand = new AsyncRelayCommand(() => Task.CompletedTask);
        DeleteGameCommand = new AsyncRelayCommand(DeleteGameAsync);

        _shell.PropertyChanged += OnShellChanged;
        _shell.LibraryRefreshed += OnLibraryRefreshed;
        _shell.AddGameRequested += OnAddGameRequested;
        if (_shell.LaunchFlow is { } flow)
        {
            flow.PropertyChanged += OnFlowChanged;
            flow.InlineContentChanged += OnInlineContentChanged;
        }

        ShowGame(_shell.SelectedGame);
    }

    /// <inheritdoc />
    public NavDestination Destination => NavDestination.Games;

    /// <summary>The profiles of the selected game, in catalog order.</summary>
    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = [];

    /// <summary>Which optional table columns fit.</summary>
    public ProfileTableColumns Columns { get; } = new();

    /// <summary>The Sidebar / Library toggle shown in the sidebar layout's header.</summary>
    public LayoutToggleViewModel LayoutToggle { get; }

    /// <summary>True while a game is selected; without one the shell shows its first-run state instead.</summary>
    public bool HasGame => _game is not null;

    /// <summary>True while the shell shows the sidebar layout.</summary>
    public bool IsSidebarLayout => _shell.Layout == ShellLayout.Sidebar;

    /// <summary>True while the shell shows the library layout, whose header is a title rather than the game's art and path.</summary>
    public bool IsLibraryLayout => _shell.Layout == ShellLayout.Library;

    /// <summary>True when there is a game editor to open, which is what shows Edit game.</summary>
    public bool CanEditGame => _deps.Editor is not null;

    /// <summary>True when the profile table has rows to show.</summary>
    public bool ShowTable => HasGame && LoadError is null && Profiles.Count > 0;

    /// <summary>True when the game has no profiles yet and the screen prompts for the first one.</summary>
    public bool ShowEmptyState => HasGame && _loaded && LoadError is null && Profiles.Count == 0;

    /// <summary>True when the storage footer has something to say.</summary>
    public bool HasStorageFooter => StorageFooterText.Length > 0;

    /// <summary>True while the launch flow has a banner or running card to show above the table.</summary>
    public bool HasInlineContent => InlineContent is not null;

    /// <summary>Switches to the History destination for the selected game.</summary>
    public IRelayCommand ShowHistoryCommand { get; }

    /// <summary>Opens the game editor pane for the selected game.</summary>
    public IRelayCommand EditGameCommand { get; }

    /// <summary>Opens the New profile wizard.</summary>
    public IAsyncRelayCommand NewProfileCommand { get; }

    /// <summary>Deletes the selected game after two confirmations.</summary>
    public IAsyncRelayCommand DeleteGameCommand { get; }

    /// <inheritdoc />
    public void OnShown() => _ = ReloadAsync();

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
        var stats = await statsTask;
        var usage = await usageTask;

        if (generation != _generation || _disposed)
        {
            return;
        }

        IsLoading = false;
        _loaded = true;
        StorageFooterText = usage.Ok
            ? _deps.Text.Message("games.storageFooter.usage", ("used", _deps.Text.FileSize(usage.Value.BytesUsed)), ("total", _deps.Text.FileSize(usage.Value.VolumeTotalBytes)))
            : string.Empty;

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

    /// <summary>Stops following the shell and the launch flow.</summary>
    public void Dispose()
    {
        _disposed = true;
        _loadCts?.Cancel();
        _shell.PropertyChanged -= OnShellChanged;
        _shell.LibraryRefreshed -= OnLibraryRefreshed;
        _shell.AddGameRequested -= OnAddGameRequested;
        if (_shell.LaunchFlow is { } flow)
        {
            flow.PropertyChanged -= OnFlowChanged;
            flow.InlineContentChanged -= OnInlineContentChanged;
        }
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IShellContext.SelectedGame):
                _ = ReloadAsync();
                break;
            case nameof(IShellContext.Layout):
                OnPropertyChanged(nameof(IsSidebarLayout));
                OnPropertyChanged(nameof(IsLibraryLayout));
                break;
        }
    }

    private void OnLibraryRefreshed(object? sender, EventArgs e) => _ = ReloadAsync();

    private void OnAddGameRequested(object? sender, AddGameRequest request)
    {
        if (_deps.Editor is { } editor)
        {
            _shell.ShowPane(editor.CreateForNew(_shell, request.Prefill));
        }
    }

    // The flow raises its events from any thread, so each one is handed to the dispatcher.
    private void OnFlowChanged(object? sender, PropertyChangedEventArgs e) => _deps.Dispatcher.Post(RefreshPlayAvailability);

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
        if (_deps.Editor is { } editor && _game is { } game)
        {
            _shell.ShowPane(editor.CreateForExisting(_shell, game.Id));
        }
    }

    private void ShowGame(GameDefinition? game)
    {
        _game = game;
        _shownGameId = game?.Id;
        GameName = game?.Name ?? string.Empty;
        InstallPath = game?.Install.Path ?? string.Empty;
        LibraryTitle = game is null ? string.Empty : _deps.Text.Message("games.header.libraryTitle", ("name", game.Name));
        RefreshInlineContent();
        OnPropertyChanged(nameof(HasGame));
        RaiseTableState();
    }

    private void RefreshInlineContent() =>
        InlineContent = _game is { } game ? _shell.LaunchFlow?.InlineContentFor(game.Id) : null;

    private void RaiseTableState()
    {
        OnPropertyChanged(nameof(ShowTable));
        OnPropertyChanged(nameof(ShowEmptyState));
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

    // Play needs a launch flow, no session for the game, and no profile already running.
    private void RefreshPlayAvailability()
    {
        var canPlay = _game is { } game
            && _shell.LaunchFlow is { } flow
            && !flow.IsActive(game.Id)
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
