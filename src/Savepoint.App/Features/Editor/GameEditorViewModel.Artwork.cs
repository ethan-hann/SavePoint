using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Editor;

public sealed partial class GameEditorViewModel
{
    private static readonly string[] _imagePatterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"];

    // What the Api last said the saved game has; a game that is not saved yet has none.
    private GameArtwork _knownArtwork = GameArtwork.None(string.Empty);

    // The image chosen in this editor and not copied in yet, and whether the game's own image is to be removed on save.
    private string? _pendingImage;
    private bool _removeImage;

    /// <summary>The Steam app id as typed; empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLookAgain))]
    [NotifyPropertyChangedFor(nameof(LookAgainWaitsForSave))]
    [NotifyCanExecuteChangedFor(nameof(LookAgainCommand))]
    private string _steamAppIdText = string.Empty;

    /// <summary>What is wrong with the Steam app id, or null.</summary>
    [ObservableProperty]
    private string? _steamAppIdError;

    /// <summary>True while a look again is running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLookAgain))]
    [NotifyCanExecuteChangedFor(nameof(LookAgainCommand))]
    private bool _isLookingAgain;

    /// <summary>An error card about the artwork, drawn in its section, or null.</summary>
    [ObservableProperty]
    private ErrorCardViewModel? _artworkError;

    /// <summary>What the Artwork section previews: the chosen image, or what the game has now.</summary>
    public GameArt ArtworkPreview { get; }

    /// <summary>Where the previewed art comes from: "From Steam", "Your image", or "No artwork found".</summary>
    public string ArtworkSourceText => EffectiveSource switch
    {
        ArtworkSource.Custom => _text.Message("gameEditor.artwork.source.custom"),
        ArtworkSource.Steam => _text.Message("gameEditor.artwork.source.steam"),
        ArtworkSource.Gog => _text.Message("gameEditor.artwork.source.gog"),
        _ => _text.Message("gameEditor.artwork.source.none"),
    };

    /// <summary>The Steam app id field's hint: the detected id when the field is empty and one was found, else an example.</summary>
    public string SteamAppIdHint =>
        _knownArtwork is { SteamAppId: { } id, SteamAppIdDetected: true }
            ? _text.Message("gameEditor.artwork.appId.detected", ("id", id.ToString(CultureInfo.InvariantCulture)))
            : _text.Message("gameEditor.artwork.appId.placeholder");

    /// <summary>True when "Remove your image" is offered: the game has an image of its own that is not already being removed, or one was just chosen.</summary>
    public bool CanRemoveImage => _pendingImage is not null || (_knownArtwork.Source == ArtworkSource.Custom && !_removeImage);

    /// <summary>
    /// True when "Look again" can run: the game is saved, and the app id field still holds the id the game has, because a look up
    /// works on the saved game.
    /// </summary>
    public bool CanLookAgain => _saved is not null && !IsLookingAgain && ParseSteamAppId() == _saved.SteamAppId;

    /// <summary>True when the app id was changed on a saved game, so Look again waits until the change is saved.</summary>
    public bool LookAgainWaitsForSave => _saved is not null && ParseSteamAppId() != _saved.SteamAppId;

    // True while a chosen image or a removal waits for Save; the form counts as changed.
    private bool HasPendingArtwork => _pendingImage is not null || _removeImage;

    private ArtworkSource EffectiveSource => _pendingImage is not null ? ArtworkSource.Custom : _removeImage ? ArtworkSource.None : _knownArtwork.Source;

    // Said while the id is typed, with the words Save uses, so a letter does not wait for Save to be refused.
    partial void OnSteamAppIdTextChanged(string value)
    {
        SteamAppIdError = ParseSteamAppId() == 0 ? _text.Message("validation.game.steamAppIdInvalid") : null;
        ScheduleCloudCheck();
    }

    // Blank is none; anything that is not a positive whole number is 0, which the catalog check refuses with one message.
    private int? ParseSteamAppId()
    {
        var text = SteamAppIdText.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : 0;
    }

    private void InitializeArtwork(IShellContext shell, GameDefinition? saved)
    {
        if (saved is not null)
        {
            var shared = shell.Artwork.For(saved.Id);
            _knownArtwork = KnownFrom(shared);
            _followedArt = shared;
            shared.PropertyChanged += OnFollowedArtChanged;
        }

        ShowPreview(force: false);
    }

    /// <summary>Stops following the game's artwork. The shell calls it when the editor's pane is closed or replaced.</summary>
    public void Dispose()
    {
        StopCloudChecks();
        if (_followedArt is { } followed)
        {
            followed.PropertyChanged -= OnFollowedArtChanged;
            _followedArt = null;
        }
    }

    private static GameArtwork KnownFrom(GameArt art) =>
        new(art.GameId, art.Source, art.WidePath, art.SquarePath, art.SteamAppId, art.SteamAppIdDetected);

    // A lookup that finishes while the editor is open changes the shared artwork; the revision is raised after every path is in place.
    private void OnFollowedArtChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameArt.Revision) && sender is GameArt art)
        {
            _knownArtwork = KnownFrom(art);
            ShowPreview(force: true);
        }
    }

    // The shell's copy may not have been read yet, so the editor asks once for itself; it never waits for the network.
    private async Task LoadArtworkAsync()
    {
        if (_saved is null)
        {
            return;
        }

        var read = await _artwork.GetAsync(_saved.Id);
        if (read.Ok)
        {
            _knownArtwork = read.Value;
            ShowPreview(force: false);
        }
    }

    private void ShowPreview(bool force)
    {
        var id = _saved?.Id ?? string.Empty;
        var shown = _pendingImage is { } path ? new GameArtwork(id, ArtworkSource.Custom, path, path, _knownArtwork.SteamAppId, _knownArtwork.SteamAppIdDetected)
            : _removeImage ? new GameArtwork(id, ArtworkSource.None, null, null, _knownArtwork.SteamAppId, _knownArtwork.SteamAppIdDetected)
            : _knownArtwork;
        ArtworkPreview.Show(shown, force);
        OnPropertyChanged(nameof(ArtworkSourceText));
        OnPropertyChanged(nameof(SteamAppIdHint));
        OnPropertyChanged(nameof(CanRemoveImage));
        RemoveImageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task ChooseImageAsync()
    {
        var filter = new FileTypeFilter(_text.Message("gameEditor.artwork.fileType"), _imagePatterns);
        if (await _dialogs.PickOpenFileAsync(new OpenFilePickRequest(_text.Message("gameEditor.artwork.pickTitle"), [filter])) is not { } path)
        {
            return;
        }

        _pendingImage = path;
        _removeImage = false;
        ArtworkError = null;
        ShowPreview(force: true);
    }

    // A chosen image that was never saved is simply forgotten; the game's saved image is removed when the game is saved.
    [RelayCommand(CanExecute = nameof(CanRemoveImage))]
    private void RemoveImage()
    {
        _pendingImage = null;
        _removeImage = _knownArtwork.Source == ArtworkSource.Custom;
        ArtworkError = null;
        ShowPreview(force: false);
    }

    [RelayCommand(CanExecute = nameof(CanLookAgain))]
    private async Task LookAgainAsync()
    {
        IsLookingAgain = true;
        ArtworkError = null;
        try
        {
            var result = await _artwork.LookAgainAsync(_saved!.Id);
            if (result.Ok)
            {
                _knownArtwork = result.Value;
                ShowPreview(force: false);
            }
            else
            {
                ArtworkError = ArtworkCard(result.Error);
            }
        }
        finally
        {
            IsLookingAgain = false;
        }
    }

    [RelayCommand]
    private void DismissArtworkError() => ArtworkError = null;

    private ErrorCardViewModel ArtworkCard(ErrorInfo error) =>
        new(error, _text, [new ErrorCardAction(_text.Message("common.dismiss"), DismissArtworkErrorCommand)]);

    // Copies the chosen image in, or removes the game's own, once the game exists. When that fails the game is already saved, so
    // the editor stays open as an editor of the saved game, with the problem shown, and Save tries the artwork again.
    private async Task<bool> ApplyArtworkAsync(GameDefinition game)
    {
        if (!HasPendingArtwork)
        {
            return true;
        }

        var result = _pendingImage is { } path
            ? await _artwork.SetCustomImageAsync(game.Id, path)
            : await _artwork.ClearCustomImageAsync(game.Id);
        if (!result.Ok)
        {
            ArtworkError = ArtworkCard(result.Error);
            await StayOpenAsSavedAsync(game);
            return false;
        }

        _knownArtwork = result.Value;
        _pendingImage = null;
        _removeImage = false;
        ArtworkError = null;
        ShowPreview(force: false);
        return true;
    }

    private async Task StayOpenAsSavedAsync(GameDefinition game)
    {
        _saved = game;
        _source = game;
        _pendingId = game.Id;
        _baseline = BuildDefinition(game.Id);
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(IsCreating));
        NotifyTemplateNotes();
        NameMatches.Clear();
        NotifyNameMatchesChanged();
        OnPropertyChanged(nameof(CanPickTemplate));
        PickTemplateCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanLookAgain));
        OnPropertyChanged(nameof(LookAgainWaitsForSave));
        ImportCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        LookAgainCommand.NotifyCanExecuteChanged();
        await _shell.RefreshLibraryAsync();
    }
}
