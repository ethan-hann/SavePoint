using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

public sealed record ClearAllDataPreview(
    string StorageRoot,
    long StorageBytes,
    int GameCount,
    int ProfileCount,
    string CatalogDirectory,

    // Game folders that lose only their SavePoint marker; their files stay as they are (D8).
    IReadOnlyList<string> ConsentMarkerFolders,

    // Null when the clear can go ahead; a running game blocks it (D13.5).
    ErrorInfo? BlockedBy);
