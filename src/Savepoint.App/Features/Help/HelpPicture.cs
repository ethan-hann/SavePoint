namespace Savepoint.App.Features.Help;

/// <summary>
/// The picture that goes with a topic of the "How SavePoint works" page. Each one is drawn from the app's own shapes and icons, so
/// no image file ships, and it is decoration only: the topic's text carries everything a picture shows.
/// </summary>
public enum HelpPicture
{
    /// <summary>The topic has no picture.</summary>
    None,

    /// <summary>A game with three profiles, each holding a row of snapshots.</summary>
    Basics,

    /// <summary>The game's own saves beside one save snapshot that holds all of them.</summary>
    Saves,

    /// <summary>A profile, the two folders Play puts files into, and the game.</summary>
    Play,

    /// <summary>A folder with changed files, taking a snapshot of them or discarding them.</summary>
    NewChanges,

    /// <summary>The three choices after playing: keep, replace and not now.</summary>
    AfterPlay,

    /// <summary>A session played from an older snapshot, kept as a new profile or continued.</summary>
    Older,

    /// <summary>A removed snapshot waiting in Recently removed and coming back as a new profile.</summary>
    Removed,

    /// <summary>The jobs Activity lists.</summary>
    Activity,

    /// <summary>The snapshot folder on a drive, one big first snapshot and thin later ones.</summary>
    Space,
}
