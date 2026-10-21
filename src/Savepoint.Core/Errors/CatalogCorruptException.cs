namespace Savepoint.Core.Errors;

/// <summary>
/// A catalog file (<c>games.json</c>, <c>profiles.json</c>, or <c>settings.json</c>) exists but cannot be read or parsed
/// (<c>error.catalog.corrupt</c>). A missing file is not corrupt; it reads as empty. No write happens until the user resolves it
/// (D13.2). Args: <c>path</c>, the file.
/// </summary>
public sealed class CatalogCorruptException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The unreadable catalog file.</param>
    /// <param name="inner">The IO or JSON exception behind it, or null when the content parsed but was not a valid payload.</param>
    public CatalogCorruptException(string path, Exception? inner)
        : base(
            "error.catalog.corrupt",
            $"The catalog at '{path}' is unreadable or corrupt.",
            new Dictionary<string, object?> { ["path"] = path },
            inner)
    {
    }
}
