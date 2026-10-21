using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

// BackupPath is the last good copy (D13.2), null when there is none to restore.
public sealed record CorruptCatalogFile(string Path, string? BackupPath, ErrorInfo Error);
