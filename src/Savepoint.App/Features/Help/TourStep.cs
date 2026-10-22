namespace Savepoint.App.Features.Help;

/// <summary>The four things the first-run tour points at, in the order it visits them.</summary>
public enum TourStep
{
    /// <summary>The first profile's row in the profile table.</summary>
    Profile,

    /// <summary>That row's Play button.</summary>
    Play,

    /// <summary>The way to History: the chip under the game's header, or the History item in the library layout's top bar.</summary>
    History,

    /// <summary>The New profile button.</summary>
    NewProfile,
}
