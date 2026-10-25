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
    /// Where installs made under the <c>SavePointApp</c> pack id keep their app files; the installer clears it on install and uninstall.
    /// Null where there is no such folder. Nothing is stored there, and a storage destination inside it is refused.
    /// </summary>
    public string? InstallerLocalDir { get; init; }
}
