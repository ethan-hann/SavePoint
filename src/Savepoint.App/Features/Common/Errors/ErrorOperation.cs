using Savepoint.Core.Api;

namespace Savepoint.App.Features.Common.Errors;

/// <summary>
/// What SavePoint was doing when an error happened. A few errors (a file held open, too little disk space) can come from several
/// operations, and the words that fit one are wrong for another, so a card that knows the operation says it in that operation's terms.
/// </summary>
public enum ErrorOperation
{
    /// <summary>Not known; the copy stays neutral and names no operation.</summary>
    Unspecified,

    /// <summary>Switching a game's folders to a profile's snapshot, the first half of Play.</summary>
    Switch,

    /// <summary>Taking a snapshot of the game's folders.</summary>
    Snapshot,

    /// <summary>Freeing up space in the snapshot folder.</summary>
    FreeUpSpace,

    /// <summary>Moving the snapshot folder to a new place.</summary>
    MoveSnapshots,

    /// <summary>Clearing all of SavePoint's data.</summary>
    ClearData,
}

/// <summary>Maps what a job does to the operation an error card names.</summary>
public static class ErrorOperations
{
    /// <summary>The operation a job of this kind performs.</summary>
    /// <param name="kind">What the job does.</param>
    public static ErrorOperation Of(JobKind kind) => kind switch
    {
        JobKind.Launch => ErrorOperation.Switch,
        JobKind.Capture => ErrorOperation.Snapshot,
        JobKind.Compact => ErrorOperation.FreeUpSpace,
        JobKind.RelocateStorage => ErrorOperation.MoveSnapshots,
        JobKind.ClearData => ErrorOperation.ClearData,
        _ => ErrorOperation.Unspecified,
    };
}
