namespace Savepoint.Core.Api;

/// <summary>Which folders one capture changed (D15).</summary>
public enum HistoryPointKind
{
    /// <summary>Both the install folder and the saves folder changed. The list view shows it in both columns.</summary>
    VersionModsAndSave,

    /// <summary>Only the install folder changed: a new version or different mods.</summary>
    VersionAndMods,

    /// <summary>Only the saves folder changed.</summary>
    SaveOnly,
}
