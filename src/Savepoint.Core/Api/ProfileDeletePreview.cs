using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>What deleting a profile would lose, so the confirmation can name it (D17).</summary>
/// <param name="ProfileId">The profile.</param>
/// <param name="PointsOnlyHere">History points lost with the profile because no other profile holds them; 0 means a plain confirmation.</param>
/// <param name="Folders">The same loss per folder.</param>
/// <param name="BlockedBy">
/// The error the delete would fail with, such as <c>error.gameService.profileActive</c> or <c>error.gameService.gameRunning</c>, or
/// null when it can go ahead.
/// </param>
public sealed record ProfileDeletePreview(
    string ProfileId,
    int PointsOnlyHere,
    IReadOnlyList<FolderPointLoss> Folders,
    ErrorInfo? BlockedBy);
