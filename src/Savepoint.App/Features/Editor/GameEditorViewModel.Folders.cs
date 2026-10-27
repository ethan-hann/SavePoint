using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Platform;

namespace Savepoint.App.Features.Editor;

public sealed partial class GameEditorViewModel
{
    // Bumped on every change to the saves field, so a slow answer for an earlier value never replaces a later one.
    private int _resolveVersion;
    private int _modsResolveVersion;

    /// <summary>The line saying when the saved install folder was confirmed; null when there is nothing to say.</summary>
    [ObservableProperty]
    private string? _installConfirmation;

    /// <summary>The line saying when the saved saves folder was confirmed; null when there is nothing to say.</summary>
    [ObservableProperty]
    private string? _savesConfirmation;

    /// <summary>The line saying which folder a shortcut in the saves field points to; null when the field has no shortcut.</summary>
    [ObservableProperty]
    private string? _savesResolves;

    /// <summary>The line saying when the saved mods folder was confirmed; null when there is nothing to say.</summary>
    [ObservableProperty]
    private string? _modsConfirmation;

    /// <summary>The line saying which folder a shortcut in the mods field points to; null when the field has no shortcut.</summary>
    [ObservableProperty]
    private string? _modsResolves;

    /// <summary>Why a template's mods folder was left out of a new game, because it does not exist yet; empty when nothing was left out.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModsLeftOutNote))]
    private string _modsLeftOutNote = string.Empty;

    /// <summary>True when <see cref="ModsLeftOutNote"/> has text.</summary>
    public bool HasModsLeftOutNote => ModsLeftOutNote.Length > 0;

    /// <summary>The path shortcuts offered under the mods field; the same ones as for the saves field.</summary>
    public IReadOnlyList<SuggestionItem> ModsLocationChoices { get; private set; } = [];

    /// <summary>True while the saves field is empty, which is when the editor explains what a game without a saves folder is missing.</summary>
    public bool HasNoSavesFolder => string.IsNullOrWhiteSpace(SavesPath);

    /// <summary>The path shortcuts offered under the saves field, such as <c>{documents}</c>; which ones exist depends on the operating system.</summary>
    public IReadOnlyList<SuggestionItem> LocationChoices { get; private set; } = [];

    // Each field offers its own buttons: a shortcut changes only the field its row sits under.
    private IReadOnlyList<SuggestionItem> BuildLocationChoices(Action<string> apply, string automationKey)
    {
        // Saved Games is a Windows folder; every other system has the home folder as a starting point instead.
        string[] tokens = _text.OsKind == OsKind.Windows
            ? ["{documents}", "{savedGames}", "{appData}", "{localAppData}"]
            : ["{documents}", "{userHome}", "{appData}", "{localAppData}"];
        return [.. tokens.Select(token => new SuggestionItem(token, new RelayCommand(() => apply(token)), LocationName(token), _text.Message(automationKey, ("location", LocationName(token)))))];
    }

    // What a person calls the place; the field itself keeps the shortcut, which the button's tooltip shows.
    private string LocationName(string token) => token switch
    {
        "{documents}" => _text.Message("gameEditor.external.location.documents"),
        "{savedGames}" => _text.Message("gameEditor.external.location.savedGames"),
        "{userHome}" => _text.Message("gameEditor.external.location.home"),
        "{appData}" => _text.Message("gameEditor.external.location.appData"),
        _ => _text.Message("gameEditor.external.location.localAppData"),
    };

    // The shortcut replaces any shortcut the field starts with and keeps the rest of the path.
    private static string WithLocation(string current, string token)
    {
        current = current.Trim();
        var tail = current.StartsWith('{') && current.IndexOf('}') is var end and > 0 ? current[(end + 1)..] : current;
        return tail.Length == 0 || tail[0] is '\\' or '/' ? token + tail : $@"{token}\{tail}";
    }

    partial void OnSavesPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasNoSavesFolder));
        SavesError = null;
        SavesCard = null;
        UpdateConfirmations();
        _ = UpdateSavesResolvesAsync();
    }

    // A template can name a mods folder the game has not made yet (a player with no mods never has). A new game cannot be added with a
    // folder that is not there, so it starts without it, as adding from the library does, and the note says the folder can be added later.
    private async Task LeaveOutMissingTemplateModsAsync()
    {
        var raw = ModsPath.Trim();
        if (raw.Length == 0 || await _games.FolderExistsAsync(raw) is not { Ok: true, Value: false })
        {
            return;
        }

        var resolved = await _games.ResolvePathAsync(raw);
        if (ModsPath.Trim() != raw)
        {
            return;
        }

        ModsPath = string.Empty;
        ModsLeftOutNote = _text.Message("gameEditor.mods.leftOutNote", ("path", resolved.Ok ? resolved.Value : raw));
    }

    partial void OnModsPathChanged(string value)
    {
        ModsLeftOutNote = string.Empty;
        ModsError = null;
        ModsCard = null;
        UpdateConfirmations();
        _ = UpdateModsResolvesAsync();
    }

    partial void OnInstallPathChanged(string value)
    {
        InstallError = null;
        InstallCard = null;
        NotifyModFilesPick();
        ClearProcessNameOffer();
        NotifySteamStartNote();
        UpdateConfirmations();
        ScheduleCloudCheck();
    }

    private async Task UpdateSavesResolvesAsync()
    {
        var version = ++_resolveVersion;
        var line = await ResolvesLineAsync(SavesPath);
        if (version == _resolveVersion)
        {
            SavesResolves = line;
        }
    }

    private async Task UpdateModsResolvesAsync()
    {
        var version = ++_modsResolveVersion;
        var line = await ResolvesLineAsync(ModsPath);
        if (version == _modsResolveVersion)
        {
            ModsResolves = line;
        }
    }

    // While a shortcut is still being typed it cannot resolve; the problem is reported when the user saves.
    private async Task<string?> ResolvesLineAsync(string field)
    {
        var raw = field.Trim();
        if (!raw.StartsWith('{'))
        {
            return null;
        }

        var resolved = await _games.ResolvePathAsync(raw);
        return resolved.Ok ? _text.Message("gameEditor.external.resolvesTo", ("path", resolved.Value)) : null;
    }

    // The "Confirmed for SavePoint" line belongs to the folder as it was saved; once the field points elsewhere there is nothing to confirm yet.
    private void UpdateConfirmations()
    {
        InstallConfirmation = ConfirmationFor(RepoRole.Install, InstallPath);
        ModsConfirmation = ConfirmationFor(RepoRole.Mods, ModsPath);
        SavesConfirmation = ConfirmationFor(RepoRole.External, SavesPath);
    }

    private string? ConfirmationFor(RepoRole role, string current)
    {
        if (_saved is null || OriginalPath(role) != current.Trim())
        {
            return null;
        }

        // A folder that is gone gets its own card, so it is not also called confirmed.
        if (_statuses.FirstOrDefault(status => status.Role == role) is not { Exists: true, Consent: { Confirmed: true } consent })
        {
            return null;
        }

        return consent.ConfirmedAt is { } at
            ? _text.Message("gameEditor.folder.confirmed", ("date", _text.Date(at)))
            : _text.Message("gameEditor.folder.confirmedNoDate");
    }

    private string OriginalPath(RepoRole role) => PathOf(_saved, role);

    private static string PathOf(GameDefinition? game, RepoRole role) => (role switch
    {
        RepoRole.Install => game?.Install.Path,
        RepoRole.Mods => game?.Mods?.Path,
        _ => game?.External?.Path,
    })?.Trim() ?? string.Empty;

    // A folder that is gone gets the "moved or is gone" card with a way to choose the new place; picking one re-runs the confirmation on save.
    private void ShowMissingFolders()
    {
        foreach (var status in _statuses.Where(status => !status.Exists && OriginalPath(status.Role) != string.Empty))
        {
            var role = status.Role;
            var error = new ErrorInfo(
                "error.path.missing",
                new Dictionary<string, object?> { ["path"] = status.ResolvedPath ?? status.Path, ["role"] = FolderRoles.Name(role) },
                string.Empty);
            ShowCard(role, new ErrorCardViewModel(error, _text, [ChooseFolderAction(role)]));
        }
    }

    // A card about a folder is drawn under that folder's field.
    private void ShowCard(RepoRole role, ErrorCardViewModel card)
    {
        switch (role)
        {
            case RepoRole.Install:
                InstallCard = card;
                break;
            case RepoRole.Mods:
                ModsCard = card;
                break;
            default:
                SavesCard = card;
                break;
        }
    }

    private ErrorCardAction ChooseFolderAction(RepoRole role) =>
        new(_text.Message("gameEditor.chooseFolder"), new AsyncRelayCommand(() => BrowseFolderAsync(role)), IsPrimary: true);

    [RelayCommand]
    private Task BrowseInstallAsync() => BrowseFolderAsync(RepoRole.Install);

    [RelayCommand]
    private Task BrowseSavesAsync() => BrowseFolderAsync(RepoRole.External);

    [RelayCommand]
    private Task BrowseModsAsync() => BrowseFolderAsync(RepoRole.Mods);

    private async Task BrowseFolderAsync(RepoRole role)
    {
        var start = await StartFolderAsync(role switch
        {
            RepoRole.Install => InstallPath,
            RepoRole.Mods => ModsPath,
            _ => SavesPath,
        });
        var title = _text.Message(role switch
        {
            RepoRole.Install => "gameEditor.install.pickTitle",
            RepoRole.Mods => "gameEditor.mods.pickTitle",
            _ => "gameEditor.external.pickTitle",
        });
        if (await _dialogs.PickFolderAsync(new FolderPickRequest(title, start)) is not { } picked)
        {
            return;
        }

        switch (role)
        {
            case RepoRole.Install:
                InstallPath = picked;
                await RefreshSuggestionsAsync();
                break;
            case RepoRole.Mods:
                ModsPath = picked;
                break;
            default:
                SavesPath = picked;
                break;
        }
    }

    // The picker opens where the field already points; a shortcut is turned into the folder it names first.
    private async Task<string?> StartFolderAsync(string current)
    {
        var raw = current.Trim();
        if (raw.Length == 0)
        {
            return null;
        }

        if (!raw.StartsWith('{'))
        {
            return raw;
        }

        var resolved = await _games.ResolvePathAsync(raw);
        return resolved.Ok ? resolved.Value : null;
    }
}
