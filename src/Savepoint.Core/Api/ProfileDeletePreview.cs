using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

public sealed record ProfileDeletePreview(
    string ProfileId,

    // History points lost with the profile; zero means a plain confirmation (D17).
    int PointsOnlyHere,
    IReadOnlyList<FolderPointLoss> Folders,

    // The error the delete would fail with, such as the active profile or a running game; null when it can go ahead.
    ErrorInfo? BlockedBy);
