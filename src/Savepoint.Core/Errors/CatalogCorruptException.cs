namespace Savepoint.Core.Errors;

public sealed class CatalogCorruptException : SavepointException
{
    public CatalogCorruptException(string path, Exception? inner)
        : base(
            "error.catalog.corrupt",
            $"The catalog at '{path}' is unreadable or corrupt.",
            new Dictionary<string, object?> { ["path"] = path },
            inner)
    {
    }
}
