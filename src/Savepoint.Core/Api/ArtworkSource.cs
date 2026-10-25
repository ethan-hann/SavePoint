namespace Savepoint.Core.Api;

/// <summary>Where a game's artwork comes from; the user's own image beats Steam's, and Steam's beats none (D30).</summary>
public enum ArtworkSource
{
    /// <summary>No artwork was found, so the game shows its name tint.</summary>
    None,

    /// <summary>Images from the Steam client's own cache, or from Steam's servers.</summary>
    Steam,

    /// <summary>The image the user chose for the game.</summary>
    Custom,

    /// <summary>Images GOG Galaxy already downloaded to this computer, for a game installed by GOG.</summary>
    Gog,
}
