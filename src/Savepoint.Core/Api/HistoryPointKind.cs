namespace Savepoint.Core.Api;

// Which folders one capture changed (D15).
public enum HistoryPointKind
{
    VersionModsAndSave,
    VersionAndMods,
    SaveOnly,
}
