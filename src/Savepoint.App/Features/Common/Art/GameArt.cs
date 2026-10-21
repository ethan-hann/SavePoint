using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Common.Art;

/// <summary>
/// One game's artwork as the screens see it: the two image paths, where they came from, and the Steam app id the lookup used. The
/// same instance serves every tile of the game, so a list that is rebuilt keeps its images. It changes only on the UI thread.
/// </summary>
public sealed class GameArt : ObservableObject
{
    private readonly ArtImages _images;
    private bool _announced;

    /// <summary>Creates artwork for a game that has none yet.</summary>
    /// <param name="gameId">The game.</param>
    /// <param name="images">Decodes and caches the bitmaps.</param>
    public GameArt(string gameId, ArtImages images)
    {
        GameId = gameId;
        _images = images;
    }

    /// <summary>The game.</summary>
    public string GameId { get; }

    /// <summary>Where the paths came from.</summary>
    public ArtworkSource Source { get; private set; }

    /// <summary>The wide image, or null.</summary>
    public string? WidePath { get; private set; }

    /// <summary>The square or tall image, or null.</summary>
    public string? SquarePath { get; private set; }

    /// <summary>The Steam app id in use, or null.</summary>
    public int? SteamAppId { get; private set; }

    /// <summary>True when <see cref="SteamAppId"/> was found rather than stored on the game.</summary>
    public bool SteamAppIdDetected { get; private set; }

    /// <summary>Grows each time the images may have changed, so a tile reads its file again.</summary>
    public int Revision { get; private set; }

    /// <summary>True when there is an image to draw.</summary>
    public bool HasArtwork => WidePath is not null || SquarePath is not null;

    /// <summary>Called once, the first time a tile showing this game is on screen; the provider starts reading the artwork then.</summary>
    internal Action<GameArt>? OnScreen { get; set; }

    /// <summary>Takes what the Api says the game has now.</summary>
    /// <param name="artwork">The artwork the Api reported.</param>
    /// <param name="contentMayHaveChanged">True when the files may have changed even if their paths did not.</param>
    public void Show(GameArtwork artwork, bool contentMayHaveChanged = false)
    {
        ArgumentNullException.ThrowIfNull(artwork);
        var same = Source == artwork.Source
            && WidePath == artwork.WidePath
            && SquarePath == artwork.SquarePath
            && SteamAppId == artwork.SteamAppId
            && SteamAppIdDetected == artwork.SteamAppIdDetected;
        if (same && !contentMayHaveChanged)
        {
            return;
        }

        Source = artwork.Source;
        WidePath = artwork.WidePath;
        SquarePath = artwork.SquarePath;
        SteamAppId = artwork.SteamAppId;
        SteamAppIdDetected = artwork.SteamAppIdDetected;
        Revision++;
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(WidePath));
        OnPropertyChanged(nameof(SquarePath));
        OnPropertyChanged(nameof(SteamAppId));
        OnPropertyChanged(nameof(SteamAppIdDetected));
        OnPropertyChanged(nameof(HasArtwork));
        OnPropertyChanged(nameof(Revision));
    }

    /// <summary>The path a tile of this shape draws: its own kind of image, else the other one; null when there is none.</summary>
    /// <param name="shape">The shape of the tile.</param>
    public string? PathFor(ArtShape shape) => shape == ArtShape.Wide ? WidePath ?? SquarePath : SquarePath ?? WidePath;

    /// <summary>Tells the provider a tile of this game is on screen; only the first call does anything.</summary>
    public void NotifyOnScreen()
    {
        if (_announced)
        {
            return;
        }

        _announced = true;
        OnScreen?.Invoke(this);
    }

    /// <summary>The bitmap already decoded for a request, or null.</summary>
    /// <param name="request">What a tile wants.</param>
    public Bitmap? TryGetBitmap(ArtRequest request) => _images.TryGet(KeyOf(request));

    /// <summary>Decodes the file for a request, off the UI thread; null when it cannot be decoded.</summary>
    /// <param name="request">What a tile wants.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    public Task<Bitmap?> LoadBitmapAsync(ArtRequest request, CancellationToken cancellationToken = default) =>
        _images.LoadAsync(KeyOf(request), cancellationToken);

    private static ArtKey KeyOf(ArtRequest request) => new(request.Path, request.Revision, request.Width, request.Height, request.ScalePercent);
}

/// <summary>What a tile asks for: one file at the size it shows it.</summary>
/// <param name="Path">The image file.</param>
/// <param name="Revision">The <see cref="GameArt.Revision"/> the path belongs to.</param>
/// <param name="Width">The tile's width in device-independent pixels.</param>
/// <param name="Height">The tile's height in device-independent pixels.</param>
/// <param name="ScalePercent">The render scaling in percent.</param>
public readonly record struct ArtRequest(string Path, int Revision, int Width, int Height, int ScalePercent);
