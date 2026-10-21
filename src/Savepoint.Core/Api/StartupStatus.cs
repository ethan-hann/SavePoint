namespace Savepoint.Core.Api;

/// <summary>
/// The result of the checks made before any screen shows. Either problem replaces the app with its own error screen (D18).
/// </summary>
/// <param name="Git">Whether the bundled git runs.</param>
/// <param name="Catalog">Whether the catalog files can be read.</param>
public sealed record StartupStatus(GitStatus Git, CatalogStatus Catalog);
