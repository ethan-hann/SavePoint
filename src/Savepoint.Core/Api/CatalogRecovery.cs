namespace Savepoint.Core.Api;

/// <summary>
/// How to deal with a corrupt catalog file. Either way the corrupt file is moved aside, never deleted, so nothing the user might
/// still need is lost.
/// </summary>
public enum CatalogRecovery
{
    /// <summary>Put back the last good copy of the file, which is the <see cref="CorruptCatalogFile.BackupPath"/>.</summary>
    RestoreBackup,

    /// <summary>Start that file empty. The games, profiles, or settings in it are no longer listed.</summary>
    StartEmpty,
}
