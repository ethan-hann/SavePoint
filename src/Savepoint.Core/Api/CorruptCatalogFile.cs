using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>A catalog file that could not be read.</summary>
/// <param name="Path">The unreadable file.</param>
/// <param name="BackupPath">The last good copy of it (D13.2), or null when there is none to restore.</param>
/// <param name="Error">Why the file could not be read, normally <c>error.catalog.corrupt</c>.</param>
public sealed record CorruptCatalogFile(string Path, string? BackupPath, ErrorInfo Error);
