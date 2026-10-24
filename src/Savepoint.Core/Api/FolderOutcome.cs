using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>
/// How a job fared on one folder. A job reports one for each folder it touched, so half a save is never reported as a whole one
/// (D13.20).
/// </summary>
/// <param name="Role">Which folder.</param>
/// <param name="Error">Why the folder failed, or null when the job succeeded on it. A cancellation is an error here too.</param>
/// <param name="LeftAlone">
/// True when a save left this folder as it was because it sits on another profile's branch or on an older point, neither of which a
/// point of this profile can be written onto. That is not an error: nothing is lost, and what the session changed in the folder stays
/// there, to be listed as unsaved changes at the next launch (as the other profile's, when the folder sits on its branch).
/// </param>
/// <param name="LeftOnBranch">The branch the folder sits on when <paramref name="LeftAlone"/> is true; null when it sits on an older point and on no branch.</param>
/// <param name="KeptAsNew">
/// True when a save that was asked to replace a snapshot found that snapshot was not the newest in this folder (the folder holds no commit
/// of it, or a newer one), so the session was added as a new snapshot there instead. Nothing was replaced or removed in this folder.
/// </param>
/// <param name="Skipped">
/// True when a save that was asked to update the latest snapshot found nothing unsaved in this folder and so left its snapshot exactly as
/// it was. That is not an error and not a write: the folder is as saved as it ever was.
/// </param>
public sealed record FolderOutcome(RepoRole Role, ErrorInfo? Error = null, bool LeftAlone = false, string? LeftOnBranch = null, bool KeptAsNew = false, bool Skipped = false)
{
    /// <summary>True when the job succeeded on this folder, that is, <see cref="Error"/> is null. A folder that was left alone or skipped is still Ok.</summary>
    public bool Ok => Error is null;

    /// <summary>True when the job wrote this folder: it succeeded on it (<see cref="Ok"/>) and neither left it alone nor skipped it.</summary>
    public bool Saved => Ok && !LeftAlone && !Skipped;
}
