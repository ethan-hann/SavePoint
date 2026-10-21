using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

public sealed record FolderStatus(
    RepoRole Role,

    // As stored in the definition, tokens included.
    string Path,

    // Null when a token in Path cannot be resolved on this machine.
    string? ResolvedPath,

    // False is the "folder has moved or is gone" case.
    bool Exists,
    ConsentStatus Consent);
