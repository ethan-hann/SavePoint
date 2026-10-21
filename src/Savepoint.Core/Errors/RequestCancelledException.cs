namespace Savepoint.Core.Errors;

/// <summary>
/// A call stopped because its caller cancelled the token it passed (D23). The caller asked for this, so the UI discards the
/// result instead of showing the message; it exists so no cancellation falls through to <c>error.unknown</c>.
/// </summary>
public sealed class RequestCancelledException : SavepointException
{
    /// <summary>Creates the error.</summary>
    public RequestCancelledException()
        : base("error.operation.cancelled", "The request was cancelled by its caller.")
    {
    }
}
