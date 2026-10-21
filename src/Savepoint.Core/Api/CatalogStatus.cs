namespace Savepoint.Core.Api;

// No catalog write happens while CorruptFiles is non-empty (D13.2).
public sealed record CatalogStatus(string Directory, IReadOnlyList<CorruptCatalogFile> CorruptFiles);
