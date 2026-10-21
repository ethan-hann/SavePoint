namespace Savepoint.Core.Api;

// Checked before any screen shows; either problem replaces the app with its own error screen (D18).
public sealed record StartupStatus(GitStatus Git, CatalogStatus Catalog);
