namespace Savepoint.Core.Catalog;

// Positional records, because source-generated JSON overwrites init-only property defaults with null when a key is absent.
public sealed record InstallLocation(string Path = "");
