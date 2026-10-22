namespace Savepoint.Core.Errors;

/// <summary>
/// The folder chosen as the new storage location holds the partial copy of an earlier move that never finished
/// (<c>error.storageRelocation.moveUnfinished</c>), so a new move would mix with it. Args: <c>path</c>.
/// </summary>
public sealed class StorageMoveUnfinishedException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The destination folder that carries the earlier move's marker.</param>
    public StorageMoveUnfinishedException(string path)
        : base(
            "error.storageRelocation.moveUnfinished",
            $"An earlier move to '{path}' did not finish, so the folder holds a partial copy.",
            new Dictionary<string, object?> { ["path"] = path })
    {
    }
}
