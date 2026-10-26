using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Editor;

public sealed partial class GameEditorViewModel
{
    // Bumped on every change to the saves field, so a slow answer for an earlier value never replaces a later one.
    private int _resolveVersion;

    /// <summary>The line saying when the saved install folder was confirmed; null when there is nothing to say.</summary>
    [ObservableProperty]
    private string? _installConfirmation;

    /// <summary>The line saying when the saved saves folder was confirmed; null when there is nothing to say.</summary>
    [ObservableProperty]
    private string? _savesConfirmation;

    /// <summary>The line saying which folder a shortcut in the saves field points to; null when the field has no shortcut.</summary>
    [ObservableProperty]
    private string? _savesResolves;

    /// <summary>True while the saves field is empty, which is when the editor explains what a game without a saves folder is missing.</summary>
    public bool HasNoSavesFolder => string.IsNullOrWhiteSpace(SavesPath);

    /// <summary>The path shortcuts offered under the saves field, such as <c>{documents}</c>; which ones exist depends on the operating system.</summary>
    public IReadOnlyList<SuggestionItem> LocationChoices { get; private set; } = [];

    private IReadOnlyList<SuggestionItem> BuildLocationChoices()
    {
        // Saved Games is a Windows folder; every other system has the home folder as a starting point instead.
        string[] tokens = _text.Os == "windows"
            ? ["{documents}", "{savedGames}", "{appData}", "{localAppData}"]
            : ["{documents}", "{userHome}", "{appData}", "{localAppData}"];
        return [.. tokens.Select(token => new SuggestionItem(token, new RelayCommand(() => ApplyLocation(token)), LocationName(token)))];
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
    private void ApplyLocation(string token)
    {
        var current = SavesPath.Trim();
        var tail = current.StartsWith('{') && current.IndexOf('}') is var end and > 0 ? current[(end + 1)..] : current;
        SavesPath = tail.Length == 0 || tail[0] is '\\' or '/' ? token + tail : $@"{token}\{tail}";
    }

    partial void OnSavesPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasNoSavesFolder));
        SavesError = null;
        SavesCard = null;
        UpdateConfirmations();
        _ = UpdateSavesResolvesAsync();
    }

    partial void OnInstallPathChanged(string value)
    {
        InstallError = null;
        InstallCard = null;
        ClearProcessNameOffer();
        NotifySteamStartNote();
        UpdateConfirmations();
    }

    private async Task UpdateSavesResolvesAsync()
    {
        var version = ++_resolveVersion;
        var raw = SavesPath.Trim();
        if (!raw.StartsWith('{'))
        {
            SavesResolves = null;
            return;
        }

        var resolved = await _games.ResolvePathAsync(raw);
        if (version == _resolveVersion)
        {
            // While the shortcut is still being typed it cannot resolve; the problem is reported when the user saves.
            SavesResolves = resolved.Ok ? _text.Message("gameEditor.external.resolvesTo", ("path", resolved.Value)) : null;
        }
    }

    // The "Confirmed for SavePoint" line belongs to the folder as it was saved; once the field points elsewhere there is nothing to confirm yet.
    private void UpdateConfirmations()
    {
        InstallConfirmation = ConfirmationFor(RepoRole.Install, InstallPath);
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

    private string OriginalPath(RepoRole role) =>
        (role == RepoRole.Install ? _saved?.Install.Path : _saved?.External?.Path)?.Trim() ?? string.Empty;

    // A folder that is gone gets the "moved or is gone" card with a way to choose the new place; picking one re-runs the confirmation on save.
    private void ShowMissingFolders()
    {
        foreach (var status in _statuses.Where(status => !status.Exists && OriginalPath(status.Role) != string.Empty))
        {
            var role = status.Role;
            var error = new ErrorInfo(
                "error.path.missing",
                new Dictionary<string, object?> { ["path"] = status.ResolvedPath ?? status.Path, ["role"] = role == RepoRole.Install ? "install" : "external" },
                string.Empty);
            var card = new ErrorCardViewModel(error, _text, [ChooseFolderAction(role)]);
            if (role == RepoRole.Install)
            {
                InstallCard = card;
            }
            else
            {
                SavesCard = card;
            }
        }
    }

    private ErrorCardAction ChooseFolderAction(RepoRole role) =>
        new(_text.Message("gameEditor.chooseFolder"), new AsyncRelayCommand(() => BrowseFolderAsync(role)), IsPrimary: true);

    [RelayCommand]
    private Task BrowseInstallAsync() => BrowseFolderAsync(RepoRole.Install);

    [RelayCommand]
    private Task BrowseSavesAsync() => BrowseFolderAsync(RepoRole.External);

    private async Task BrowseFolderAsync(RepoRole role)
    {
        var install = role == RepoRole.Install;
        var start = await StartFolderAsync(install ? InstallPath : SavesPath);
        var title = _text.Message(install ? "gameEditor.install.pickTitle" : "gameEditor.external.pickTitle");
        if (await _dialogs.PickFolderAsync(new FolderPickRequest(title, start)) is not { } picked)
        {
            return;
        }

        if (install)
        {
            InstallPath = picked;
            await RefreshSuggestionsAsync();
        }
        else
        {
            SavesPath = picked;
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
