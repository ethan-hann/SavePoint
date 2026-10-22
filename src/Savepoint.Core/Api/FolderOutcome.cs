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
/// True when a save left this folder as it was because it sits on another profile's history, which a point of this profile cannot be
/// written onto. That is not an error: nothing is lost, and what the session changed in the folder stays there, to be listed
/// as the other profile's unsaved changes at the next launch.
/// </param>
/// <param name="LeftOnBranch">The branch the folder sits on when <paramref name="LeftAlone"/> is true; null when it sits on an older point and on no branch.</param>
public sealed record FolderOutcome(RepoRole Role, ErrorInfo? Error = null, bool LeftAlone = false, string? LeftOnBranch = null)
{
    /// <summary>True when the job succeeded on this folder, that is, <see cref="Error"/> is null. A folder that was left alone is still Ok.</summary>
    public bool Ok => Error is null;
}
