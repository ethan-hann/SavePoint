using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>Where one of a game's folders is and whether it is there.</summary>
/// <param name="Role">Which folder.</param>
/// <param name="Path">The path as stored in the definition, tokens included.</param>
/// <param name="ResolvedPath">The path on this machine; null when a token in it cannot be resolved here.</param>
/// <param name="Exists">False is the "folder has moved or is gone" case.</param>
/// <param name="Consent">Whether the user confirmed the folder for SavePoint.</param>
public sealed record FolderStatus(
    RepoRole Role,
    string Path,
    string? ResolvedPath,
    bool Exists,
    ConsentStatus Consent);
