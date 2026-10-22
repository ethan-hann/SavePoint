using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>What deleting a profile would lose, so the confirmation can name it (D17).</summary>
/// <param name="ProfileId">The profile.</param>
/// <param name="PointsOnlyHere">
/// History points lost for good with the profile: no other profile holds them and no recently removed snapshot keeps them. 0 means a
/// plain confirmation.
/// </param>
/// <param name="Folders">The same loss per folder.</param>
/// <param name="BlockedBy">
/// The error the delete would fail with, such as <c>error.gameService.profileActive</c> or <c>error.gameService.gameRunning</c>, or
/// null when it can go ahead.
/// </param>
/// <param name="PointsBackToRemoved">
/// History points only this profile holds that a record of a recently removed snapshot also keeps (a snapshot brought back from there,
/// or one a continue took off another profile). The delete does not lose them: they are listed under recently removed again until
/// the player next frees up space.
/// </param>
public sealed record ProfileDeletePreview(
    string ProfileId,
    int PointsOnlyHere,
    IReadOnlyList<FolderPointLoss> Folders,
    ErrorInfo? BlockedBy,
    int PointsBackToRemoved = 0);
