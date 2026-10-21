namespace Savepoint.Core.Errors;

/// <summary>
/// Replacing the latest save point in place (<c>SessionCompletion.UpdateExisting</c>, an amend) is not possible for this folder
/// (<c>error.gitOperation.amendNotAvailable</c>). Carries no args.
/// </summary>
public sealed class AmendNotAvailableException : SavepointException
{
    /// <summary>Creates the error.</summary>
    public AmendNotAvailableException()
        : base("error.gitOperation.amendNotAvailable", "Amending the last save is not available here.")
    {
    }
}
