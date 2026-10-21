using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>
/// How a job fared on one folder. A job reports one for each folder it touched, so half a save is never reported as a whole one
/// (D13.20).
/// </summary>
/// <param name="Role">Which folder.</param>
/// <param name="Error">Why the folder failed, or null when the job succeeded on it. A cancellation is an error here too.</param>
public sealed record FolderOutcome(RepoRole Role, ErrorInfo? Error = null)
{
    /// <summary>True when the job succeeded on this folder, that is, <see cref="Error"/> is null.</summary>
    public bool Ok => Error is null;
}
