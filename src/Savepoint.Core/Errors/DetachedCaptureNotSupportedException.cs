using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

/// <summary>
/// A capture was asked for while a folder sits on an older save point, which cannot be updated in place
/// (<c>error.launch.detachedCaptureNotSupported</c>). It is the v1 error; v2 handles that case with the fork and move-forward
/// choices of <c>SessionCompletion</c> (D14). Args: <c>role</c>.
/// </summary>
public sealed class DetachedCaptureNotSupportedException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="role">Which of the game's folders is on the older save point.</param>
    public DetachedCaptureNotSupportedException(RepoRole role)
        : base(
            "error.launch.detachedCaptureNotSupported",
            $"The {ErrorArgs.NameOf(role)} folder is on an older save, which cannot be updated in place.",
            new Dictionary<string, object?> { ["role"] = role })
    {
    }
}
