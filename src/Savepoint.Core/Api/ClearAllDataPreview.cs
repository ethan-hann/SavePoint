using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>What clearing all data would remove, so the typed confirmation can be exact (D8).</summary>
/// <param name="StorageRoot">The storage folder whose contents would be removed.</param>
/// <param name="StorageBytes">Bytes of SavePoint's own data in the storage folder.</param>
/// <param name="GameCount">Games whose records would be removed.</param>
/// <param name="ProfileCount">Profiles whose records would be removed.</param>
/// <param name="CatalogDirectory">The folder holding the catalog files, which would be removed too.</param>
/// <param name="ConsentMarkerFolders">Game folders that lose only their SavePoint marker; their files stay as they are (D8).</param>
/// <param name="BlockedBy">The error the clear would fail with, or null when it can go ahead. A running game blocks it (D13.5).</param>
public sealed record ClearAllDataPreview(
    string StorageRoot,
    long StorageBytes,
    int GameCount,
    int ProfileCount,
    string CatalogDirectory,
    IReadOnlyList<string> ConsentMarkerFolders,
    ErrorInfo? BlockedBy);
