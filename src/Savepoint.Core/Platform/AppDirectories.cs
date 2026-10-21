namespace Savepoint.Core.Platform;

/// <summary>RuntimeDir holds SavePoint-owned scratch files; DefaultStorageRoot applies until Settings moves storage.</summary>
public sealed record AppDirectories(string CatalogDir, string DefaultStorageRoot, string LogsDir, string RuntimeDir, string HomeDir);
