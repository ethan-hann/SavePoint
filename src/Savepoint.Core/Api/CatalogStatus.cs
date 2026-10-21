namespace Savepoint.Core.Api;

/// <summary>Whether the catalog files can be read.</summary>
/// <param name="Directory">The folder holding the catalog files.</param>
/// <param name="CorruptFiles">
/// The files that could not be read; empty when all is well. No catalog write happens while it is not empty (D13.2).
/// </param>
public sealed record CatalogStatus(string Directory, IReadOnlyList<CorruptCatalogFile> CorruptFiles);
