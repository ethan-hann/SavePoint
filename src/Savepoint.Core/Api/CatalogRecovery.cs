namespace Savepoint.Core.Api;

// Either way the corrupt file is moved aside, never deleted, so nothing the user might still need is lost.
public enum CatalogRecovery
{
    RestoreBackup,
    StartEmpty,
}
