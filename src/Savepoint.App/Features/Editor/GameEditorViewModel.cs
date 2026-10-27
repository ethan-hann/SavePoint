using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Editor;

/// <summary>
/// The game editor: a full pane, opened by the Games screen, that creates a game or edits an existing one.
/// It builds a <see cref="GameDefinition"/> from the form, asks the user to confirm each new folder, saves through
/// <see cref="IGamesApi"/>, and closes itself after refreshing the library. Game notes are never shown and always kept (D13.22).
/// </summary>
public sealed partial class GameEditorViewModel : ObservableObject, IDisposable
{
    private readonly IGamesApi _games;
    private readonly IDialogs _dialogs;
    private readonly UiFormatter _text;
    private readonly ToastHostViewModel _toasts;
    private readonly IShellContext _shell;
    private readonly IArtworkApi _artwork;

    // The shell's artwork for the game being edited, which the preview follows while the editor is open.
    private GameArt? _followedArt;

    // The library's definition when editing; null when adding.
    private GameDefinition? _saved;

    // What the form was opened from: carries the fields the form never shows (schema version, notes).
    private GameDefinition _source;

    // The game-wide rules a template or definition file gave the form; any other rule in it was typed by the player.
    private IReadOnlyList<string> _suppliedRules;

    // What the form built right after opening, to tell whether anything changed.
    private GameDefinition _baseline;

    // One new game keeps one id across failed attempts.
    private string? _pendingId;

    private Task? _load;

    // Each folder's confirmation and whether it still exists, as the Api last reported it.
    private IReadOnlyList<FolderStatus> _statuses = [];

    /// <summary>The game's name, as typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    /// <summary>Where the game is installed, as typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _installPath = string.Empty;

    /// <summary>Where the saves live, as typed; it may start with a shortcut such as {documents}. Empty means the game has no saves folder.</summary>
    [ObservableProperty]
    private string _savesPath = string.Empty;

    /// <summary>Where the mods live when they are outside the game folder, as typed; it may start with a shortcut such as {appData}. Empty means the game has no mods folder.</summary>
    [ObservableProperty]
    private string _modsPath = string.Empty;

    /// <summary>Which way the game starts: 0 runs a file, 1 opens a launch link (Steam).</summary>
    [ObservableProperty]
    private int _launchChoice;

    /// <summary>The file that starts the game, relative to the install folder.</summary>
    [ObservableProperty]
    private string _exePath = string.Empty;

    /// <summary>The launch link that starts the game, such as steam://run/123456.</summary>
    [ObservableProperty]
    private string _uri = string.Empty;

    /// <summary>The program name SavePoint waits for to know the game has stopped; empty uses the start file.</summary>
    [ObservableProperty]
    private string _processName = string.Empty;

    /// <summary>What is wrong with the name, or null.</summary>
    [ObservableProperty]
    private string? _nameError;

    /// <summary>What is wrong with the install folder, or null.</summary>
    [ObservableProperty]
    private string? _installError;

    /// <summary>What is wrong with the saves folder, or null.</summary>
    [ObservableProperty]
    private string? _savesError;

    /// <summary>What is wrong with the mods folder, or null.</summary>
    [ObservableProperty]
    private string? _modsError;

    /// <summary>What is wrong with the way the game starts, or null.</summary>
    [ObservableProperty]
    private string? _launchError;

    /// <summary>What is wrong with the start file, or null.</summary>
    [ObservableProperty]
    private string? _exePathError;

    /// <summary>What is wrong with the launch link, or null.</summary>
    [ObservableProperty]
    private string? _uriError;

    /// <summary>An error card about the install folder, drawn under its field, or null.</summary>
    [ObservableProperty]
    private ErrorCardViewModel? _installCard;

    /// <summary>An error card about the saves folder, drawn under its field, or null.</summary>
    [ObservableProperty]
    private ErrorCardViewModel? _savesCard;

    /// <summary>An error card about the mods folder, drawn under its field, or null.</summary>
    [ObservableProperty]
    private ErrorCardViewModel? _modsCard;

    /// <summary>An error card about the form as a whole, drawn above the columns, or null.</summary>
    [ObservableProperty]
    private ErrorCardViewModel? _formError;

    /// <summary>True while a save is running; Save is unavailable until it ends.</summary>
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
        GameDefinition? prefill,
        IArtworkApi artwork)
    {
        _games = games;
        _dialogs = dialogs;
        _text = text;
        _toasts = toasts;
        _shell = shell;
        _artwork = artwork;
        _saved = saved;
        _source = saved ?? prefill ?? new GameDefinition { InstallIgnorePaths = RunTimeOutput.DefaultRules };
        _suppliedRules = saved is null && prefill is not null ? prefill.IgnorePaths : [];
        _notesFromTemplate = saved is null && prefill is not null;
        IgnoreRules = new ChipListViewModel(
            text,
            text.Message("gameEditor.ignorePaths.label"),
            text.Message("gameEditor.addRule"),
            text.Message("gameEditor.ignorePaths.placeholder"),
            text.Message("gameEditor.ignorePaths.addAutomation"),
            text.Message("gameEditor.ignorePaths.helper"));
        InstallIgnoreRules = new ChipListViewModel(
            text,
            text.Message("gameEditor.installIgnorePaths.label"),
            text.Message("gameEditor.addRule"),
            text.Message("gameEditor.installIgnorePaths.placeholder"),
            text.Message("gameEditor.installIgnorePaths.addAutomation"),
            text.Message("gameEditor.installIgnorePaths.helper"));
        LaunchArgs = new ChipListViewModel(text, text.Message("gameEditor.launchArgs.label"), text.Message("gameEditor.addOption"), text.Message("gameEditor.launchArgs.placeholder"));
        ModFolders = new ChipListViewModel(
            text,
            text.Message("gameEditor.modPathHints.label"),
            text.Message("gameEditor.addFolder"),
            text.Message("gameEditor.modPathHints.placeholder"),
            actions:
            [
                new ChipListAction(text.Message("gameEditor.modFiles.chooseFiles"), text.Message("gameEditor.modFiles.chooseFilesAutomation"), ChooseModFilesCommand),
                new ChipListAction(text.Message("gameEditor.modFiles.chooseFolders"), text.Message("gameEditor.modFiles.chooseFoldersAutomation"), ChooseModFoldersCommand),
            ]);
        Title = saved is null ? text.Message("gameEditor.title.create") : text.Message("gameEditor.title.edit", ("name", saved.Name));
        LocationChoices = BuildLocationChoices(token => SavesPath = WithLocation(SavesPath, token), "gameEditor.external.locationAutomation");
        ModsLocationChoices = BuildLocationChoices(token => ModsPath = WithLocation(ModsPath, token), "gameEditor.mods.locationAutomation");
        ArtworkPreview = shell.Artwork.Preview(saved?.Id ?? string.Empty);
        LoadForm(_source, rulesAreDefaults: saved is null && prefill is null);
        NotifyModFilesPick();
        InitializeArtwork(shell, saved);
        InitializeGuidance();
        InitializeCloudSaves();
        _baseline = BuildDefinition(_source.Id);
    }

    /// <summary>The header: "Add a game", or "Edit" and the game's name.</summary>
    public string Title { get; }

    /// <summary>True when an existing game is being edited.</summary>
    public bool IsEditing => _saved is not null;

    /// <summary>True when a new game is being added.</summary>
    public bool IsCreating => _saved is null;

    /// <summary>The files to ignore in both folders, the saves folder included.</summary>
    public ChipListViewModel IgnoreRules { get; }

    /// <summary>The files to ignore in the game's own folder only; a rule here never reaches the saves folder.</summary>
    public ChipListViewModel InstallIgnoreRules { get; }

    /// <summary>Why a blank new game already leaves logs and crash reports out; empty for an edited game, a template or a loaded definition file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDefaultRulesNote))]
    private string _defaultRulesNote = string.Empty;

    /// <summary>True when <see cref="DefaultRulesNote"/> has text.</summary>
    public bool HasDefaultRulesNote => DefaultRulesNote.Length > 0;

    /// <summary>The extra command-line options the game starts with.</summary>
    public ChipListViewModel LaunchArgs { get; }

    /// <summary>The folders that hold mods.</summary>
    public ChipListViewModel ModFolders { get; }

    /// <summary>Whether Save is available: a name and an install folder are filled in and no save is running.</summary>
    public bool CanSave => !IsSaving && Name.Trim().Length > 0 && InstallPath.Trim().Length > 0;

    /// <summary>True when the form differs from what it was opened with.</summary>
    public bool IsDirty => BuildDefinition(_baseline.Id) != _baseline || HasPendingArtwork;

    /// <summary>Opens the editor on an existing game.</summary>
    public static GameEditorViewModel ForExisting(IGamesApi games, IDialogs dialogs, UiFormatter text, ToastHostViewModel toasts, IShellContext shell, GameDefinition game, IArtworkApi artwork) =>
        new(games, dialogs, text, toasts, shell, game, null, artwork);

    /// <summary>Opens the editor on a new game, optionally starting from a template or an imported definition.</summary>
    public static GameEditorViewModel ForNew(IGamesApi games, IDialogs dialogs, UiFormatter text, ToastHostViewModel toasts, IShellContext shell, GameDefinition? prefill, IArtworkApi artwork) =>
        new(games, dialogs, text, toasts, shell, null, prefill, artwork);

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

        if (_saved is null && _source.Mods is not null)
        {
            // Leaving the missing folder out is not a change the player made.
            var clean = !IsDirty;
            await LeaveOutMissingTemplateModsAsync();
            if (clean)
            {
                _baseline = BuildDefinition(_baseline.Id);
            }
        }

        // The store check reads files off the UI thread; it runs beside the artwork and suggestion reads.
        var cloud = LoadCloudSavesAsync();
        await LoadArtworkAsync();
        await RefreshSuggestionsAsync();
        await cloud;
    }

    partial void OnNameChanged(string value)
    {
        NameError = null;
        SearchTemplates(value);
    }

    partial void OnUriChanged(string value)
    {
        UriError = null;
        ScheduleCloudCheck();
    }

    private static string? NullIfBlank(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    // The note about default rules is only true while the rules are the ones SavePoint started the form with; a definition file's rules, a
    // template's and an edited game's are someone else's list.
    private void LoadForm(GameDefinition game, bool rulesAreDefaults = false)
    {
        DefaultRulesNote = rulesAreDefaults ? _text.Message("gameEditor.ignorePaths.defaultNote") : string.Empty;
        _fillingForm = true;
        Name = game.Name;
        _fillingForm = false;
        InstallPath = game.Install.Path;
        ModsPath = game.Mods?.Path ?? string.Empty;
        SavesPath = game.External?.Path ?? string.Empty;
        ExePath = game.Launch.ExePath ?? string.Empty;
        Uri = game.Launch.Uri ?? string.Empty;
        ProcessName = game.Launch.ProcessNameForExitDetection ?? string.Empty;
        LaunchChoice = game.Launch.Method == LaunchMethod.Uri ? 1 : 0;
        SteamAppIdText = game.SteamAppId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        LaunchArgs.SetValues(game.Launch.Args);
        IgnoreRules.SetValues(game.IgnorePaths);
        InstallIgnoreRules.SetValues(game.InstallIgnorePaths);
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
            Mods = NullIfBlank(ModsPath) is { } mods ? new ModsLocation(mods) : null,
            External = NullIfBlank(SavesPath) is { } saves ? new ExternalLocation(saves, ExternalRole.Saves) : null,
            Launch = new LaunchConfig(
                exe ? LaunchMethod.Exe : LaunchMethod.Uri,
                exe ? NullIfBlank(ExePath) : null,
                exe ? null : NullIfBlank(Uri),
                LaunchArgs.Values,
                NullIfBlank(ProcessName)),
            IgnorePaths = IgnoreRules.Values,
            InstallIgnorePaths = InstallIgnoreRules.Values,
            ModPathHints = ModFolders.Values,
            SteamAppId = ParseSteamAppId(),
        };
    }
}
