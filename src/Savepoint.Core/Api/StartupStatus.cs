namespace Savepoint.Core.Api;

/// <summary>
/// The result of the checks made before any screen shows. A problem replaces the app with its own error screen (D18).
/// </summary>
/// <param name="Git">Whether the bundled git runs.</param>
/// <param name="Catalog">Whether the catalog files can be read.</param>
/// <param name="Storage">
/// Whether the storage folder can be reached; null when it was not checked, which is while git is unavailable or a catalog file is
/// unreadable, since those screens come first and the storage is only touched once they are out of the way.
/// </param>
public sealed record StartupStatus(GitStatus Git, CatalogStatus Catalog, StorageStatus? Storage = null);
