namespace Savepoint.Core.Catalog;

/// <summary>
/// The part of a template file that is not a game definition. It is read from the same text as the definition, which skips it, so
/// the hints never reach a stored game.
/// </summary>
/// <param name="LibraryHints">The file's <c>libraryHints</c> block, or null when it has none.</param>
public sealed record TemplateFileHints(TemplateLibraryHints? LibraryHints = null);
