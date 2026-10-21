namespace Savepoint.Core.Platform;

/// <summary>Where SavePoint keeps its own data on this OS.</summary>
/// <param name="CatalogDir">Holds games.json, profiles.json, settings.json.</param>
/// <param name="DefaultStorageRoot">Where git data goes unless Settings moved it.</param>
/// <param name="LogsDir">Log files.</param>
/// <param name="RuntimeDir">Scratch files owned by SavePoint.</param>
/// <param name="HomeDir">The user's home folder.</param>
public sealed record AppDirectories(string CatalogDir, string DefaultStorageRoot, string LogsDir, string RuntimeDir, string HomeDir);
