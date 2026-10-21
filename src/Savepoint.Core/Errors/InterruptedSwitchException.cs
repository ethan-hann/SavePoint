using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

/// <summary>
/// The last switch of a folder started and did not finish, so the folder holds files of two points
/// (<c>error.launch.switchInterrupted</c>). Nothing was written or recorded. Switching to the point the unfinished switch was
/// heading for finishes it, and discarding the changes brings the folder back to a known point. Args: <c>role</c> and
/// <c>targetPoint</c>, the full id of the point the unfinished switch was heading for (null when it is not known).
/// </summary>
public sealed class InterruptedSwitchException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="role">Which of the game's folders was left half switched.</param>
    /// <param name="targetPoint">The full id of the point the unfinished switch was heading for; null when it is not known.</param>
    public InterruptedSwitchException(RepoRole role, string? targetPoint = null)
        : base(
            "error.launch.switchInterrupted",
            $"The last switch of the {ErrorArgs.NameOf(role)} folder did not finish, so it holds files of two points; finish the switch or discard the changes.",
            new Dictionary<string, object?> { ["role"] = role, ["targetPoint"] = targetPoint })
    {
    }
}
