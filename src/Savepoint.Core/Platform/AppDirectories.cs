namespace Savepoint.Core.Platform;

/// <summary>RuntimeDir holds SavePoint-owned scratch files; DefaultStorageRoot applies until Settings moves storage.</summary>
public sealed record AppDirectories(string CatalogDir, string DefaultStorageRoot, string LogsDir, string RuntimeDir, string HomeDir)
{
    /// <summary>
    /// The folder earlier builds kept the default storage (<c>Storage</c>) and logs (<c>Logs</c>) in; null where the defaults never moved.
    /// On Windows it is the folder the installer owns and clears, so nothing new is written there.
    /// </summary>
    public string? LegacyLocalDir { get; init; }

    /// <summary>
    /// The folder the Windows installer of the current builds owns (named by the pack id) and clears on install and uninstall; null
    /// where there is no installer folder. Nothing is stored there, and a storage destination inside it is refused.
    /// </summary>
    public string? InstallerLocalDir { get; init; }
}
