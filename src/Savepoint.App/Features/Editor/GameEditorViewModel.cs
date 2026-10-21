using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Editor;

/// <summary>
/// The game editor (mockup 2g): a full pane, opened by the Games screen, that creates a game or edits an existing one.
/// It builds a <see cref="GameDefinition"/> from the form, asks the user to confirm each new folder, saves through
/// <see cref="IGamesApi"/>, and closes itself after refreshing the library. Game notes are never shown and always kept (D13.22).
/// </summary>
public sealed partial class GameEditorViewModel : ObservableObject
{
    private readonly IGamesApi _games;
    private readonly IDialogs _dialogs;
    private readonly UiFormatter _text;
    private readonly ToastHostViewModel _toasts;
    private readonly IShellContext _shell;

    // The library's definition when editing; null when adding.
    private readonly GameDefinition? _saved;

    // What the form was opened from: carries the fields the form never shows (schema version, notes).
    private GameDefinition _source;

    // What the form built right after opening, to tell whether anything changed.
    private GameDefinition _baseline;

    // One new game keeps one id across failed attempts.
    private string? _pendingId;

    private Task? _load;

    // Each folder's confirmation and whether it still exists, as the Api last reported it.
    private IReadOnlyList<FolderStatus> _statuses = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _installPath = string.Empty;

    [ObservableProperty]
    private string _savesPath = string.Empty;

    [ObservableProperty]
    private int _launchChoice;

    [ObservableProperty]
    private string _exePath = string.Empty;

    [ObservableProperty]
    private string _uri = string.Empty;

    [ObservableProperty]
    private string _processName = string.Empty;

    [ObservableProperty]
    private string? _nameError;

    [ObservableProperty]
    private string? _installError;

    [ObservableProperty]
    private string? _savesError;

    [ObservableProperty]
    private string? _launchError;

    [ObservableProperty]
    private string? _exePathError;

    [ObservableProperty]
    private string? _uriError;

    [ObservableProperty]
    private ErrorCardViewModel? _installCard;

    [ObservableProperty]
    private ErrorCardViewModel? _savesCard;

    [ObservableProperty]
    private ErrorCardViewModel? _formError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    private GameEditorViewModel(
        IGamesApi games,
        IDialogs dialogs,
        UiFormatter text,
        ToastHostViewModel toasts,
        IShellContext shell,
        GameDefinition? saved,
        GameDefinition? prefill)
    {
        _games = games;
        _dialogs = dialogs;
        _text = text;
        _toasts = toasts;
        _shell = shell;
        _saved = saved;
        _source = saved ?? prefill ?? new GameDefinition();
        IgnoreRules = new ChipListViewModel(text, text.Message("gameEditor.ignorePaths.label"), text.Message("gameEditor.addRule"), text.Message("gameEditor.ignorePaths.placeholder"));
        LaunchArgs = new ChipListViewModel(text, text.Message("gameEditor.launchArgs.label"), text.Message("gameEditor.addOption"), text.Message("gameEditor.launchArgs.placeholder"));
        ModFolders = new ChipListViewModel(text, text.Message("gameEditor.modPathHints.label"), text.Message("gameEditor.addFolder"), text.Message("gameEditor.modPathHints.placeholder"));
        Title = saved is null ? text.Message("gameEditor.title.create") : text.Message("gameEditor.title.edit", ("name", saved.Name));
        LocationChoices = BuildLocationChoices();
        LoadForm(_source);
        _baseline = BuildDefinition(_source.Id);
    }

    /// <summary>The header: "Add a game", or "Edit" and the game's name.</summary>
    public string Title { get; }

    /// <summary>True when an existing game is being edited.</summary>
    public bool IsEditing => _saved is not null;

    /// <summary>True when a new game is being added.</summary>
    public bool IsCreating => _saved is null;

    /// <summary>The files to ignore.</summary>
    public ChipListViewModel IgnoreRules { get; }

    /// <summary>The extra command-line options the game starts with.</summary>
    public ChipListViewModel LaunchArgs { get; }

    /// <summary>The folders that hold mods.</summary>
    public ChipListViewModel ModFolders { get; }

    /// <summary>Whether Save is available: a name and an install folder are filled in and no save is running.</summary>
    public bool CanSave => !IsSaving && Name.Trim().Length > 0 && InstallPath.Trim().Length > 0;

    /// <summary>True when the form differs from what it was opened with.</summary>
    public bool IsDirty => BuildDefinition(_baseline.Id) != _baseline;

    /// <summary>Opens the editor on an existing game.</summary>
    public static GameEditorViewModel ForExisting(IGamesApi games, IDialogs dialogs, UiFormatter text, ToastHostViewModel toasts, IShellContext shell, GameDefinition game) =>
        new(games, dialogs, text, toasts, shell, game, null);

    /// <summary>Opens the editor on a new game, optionally starting from a template or an imported definition.</summary>
    public static GameEditorViewModel ForNew(IGamesApi games, IDialogs dialogs, UiFormatter text, ToastHostViewModel toasts, IShellContext shell, GameDefinition? prefill) =>
        new(games, dialogs, text, toasts, shell, null, prefill);

    /// <summary>Reads what needs the Api (each folder's confirmation and whether it still exists). Safe to call more than once.</summary>
    public Task LoadAsync() => _load ??= LoadCoreAsync();

    private async Task LoadCoreAsync()
    {
        if (_saved is not null)
        {
            var statuses = await _games.GetFolderStatusAsync(_saved.Id);
            if (statuses.Ok)
            {
                _statuses = statuses.Value;
                UpdateConfirmations();
                ShowMissingFolders();
            }
            else
            {
                _toasts.Show(new ToastMessage(_text.Message(statuses.Error), ToastKind.Error));
            }
        }

        await RefreshSuggestionsAsync();
    }

    partial void OnNameChanged(string value) => NameError = null;

    partial void OnUriChanged(string value) => UriError = null;

    private static string? NullIfBlank(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private void LoadForm(GameDefinition game)
    {
        Name = game.Name;
        InstallPath = game.Install.Path;
        SavesPath = game.External?.Path ?? string.Empty;
        LaunchChoice = game.Launch.Method == LaunchMethod.Uri ? 1 : 0;
        ExePath = game.Launch.ExePath ?? string.Empty;
        Uri = game.Launch.Uri ?? string.Empty;
        ProcessName = game.Launch.ProcessNameForExitDetection ?? string.Empty;
        LaunchArgs.SetValues(game.Launch.Args);
        IgnoreRules.SetValues(game.IgnorePaths);
        ModFolders.SetValues(game.ModPathHints);
    }

    // Only the chosen start method's field goes in; the other one is kept in the form so switching back loses nothing.
    private GameDefinition BuildDefinition(string id)
    {
        var exe = LaunchChoice == 0;
        return _source with
        {
            Id = id,
            Name = Name.Trim(),
            Install = new InstallLocation(InstallPath.Trim()),
            External = NullIfBlank(SavesPath) is { } saves ? new ExternalLocation(saves, ExternalRole.Saves) : null,
            Launch = new LaunchConfig(
                exe ? LaunchMethod.Exe : LaunchMethod.Uri,
                exe ? NullIfBlank(ExePath) : null,
                exe ? null : NullIfBlank(Uri),
                LaunchArgs.Values,
                NullIfBlank(ProcessName)),
            IgnorePaths = IgnoreRules.Values,
            ModPathHints = ModFolders.Values,
        };
    }
}
