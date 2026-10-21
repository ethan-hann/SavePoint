namespace Savepoint.Core.Errors;

public sealed class AmendNotAvailableException : SavepointException
{
    public AmendNotAvailableException()
        : base("error.gitOperation.amendNotAvailable", "Amending the last save is not available here.")
    {
    }
}
