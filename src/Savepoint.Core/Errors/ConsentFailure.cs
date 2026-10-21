namespace Savepoint.Core.Errors;

/// <summary>Why a folder's consent marker does not allow the operation.</summary>
public enum ConsentFailure
{
    /// <summary>The folder has no marker, so the user has not confirmed it for SavePoint.</summary>
    Missing,

    /// <summary>The marker names a different game or folder role, or the file is not a marker SavePoint wrote.</summary>
    Conflict,
}
