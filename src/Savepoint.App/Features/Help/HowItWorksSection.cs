namespace Savepoint.App.Features.Help;

/// <summary>
/// The topics of the "How SavePoint works" page that other screens link to, so a question button can open the page at the topic that
/// answers it.
/// </summary>
public enum HowItWorksSection
{
    /// <summary>What Steam and GOG Galaxy do with the saves SavePoint changes and removes.</summary>
    CloudSaves,

    /// <summary>The two lists of rules for files SavePoint skips.</summary>
    FilesToLeaveOut,

    /// <summary>The mods folder outside the game folder, and the mod files inside it that the mod counts include.</summary>
    ModsFolders,

    /// <summary>Running the game's own program versus starting it through Steam, and how SavePoint knows the player has stopped.</summary>
    HowTheGameStarts,
}
