using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Errors;

namespace Savepoint.App.Features.Editor;

public sealed partial class GameEditorViewModel
{
    /// <summary>The Steam app id as typed; empty for none.</summary>
    [ObservableProperty]
    private string _steamAppIdText = string.Empty;

    /// <summary>What is wrong with the Steam app id, or null.</summary>
    [ObservableProperty]
    private string? _steamAppIdError;

    /// <summary>True while a look again is running.</summary>
    [ObservableProperty]
    private bool _isLookingAgain;

    /// <summary>An error card about the artwork, drawn in its section, or null.</summary>
    [ObservableProperty]
    private ErrorCardViewModel? _artworkError;

    /// <summary>True when the editor has an artwork Api, so the Artwork section shows.</summary>
    public bool HasArtworkSection => false;

    /// <summary>What the Artwork section previews: the chosen image, or what the game has now.</summary>
    public GameArt ArtworkPreview { get; } = new("", new ArtImages());

    /// <summary>Where the previewed art comes from: "From Steam", "Your image", or "No artwork found".</summary>
    public string ArtworkSourceText => string.Empty;

    /// <summary>The Steam app id field's hint: the detected id when the field is empty and one was found.</summary>
    public string SteamAppIdHint => string.Empty;

    /// <summary>True when "Remove your image" is offered.</summary>
    public bool CanRemoveImage => false;

    /// <summary>True when "Look again" can run.</summary>
    public bool CanLookAgain => false;

    /// <summary>Picks an image file for the game; it is copied in when the game is saved.</summary>
    public IAsyncRelayCommand ChooseImageCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    /// <summary>Removes the game's own image when the game is saved.</summary>
    public IRelayCommand RemoveImageCommand { get; } = new RelayCommand(() => { });

    /// <summary>Looks for Steam artwork again right now.</summary>
    public IAsyncRelayCommand LookAgainCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}
