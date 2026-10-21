namespace Savepoint.Core.Errors;

public abstract class SavepointException : Exception
{
    protected SavepointException(
        string messageId,
        string devMessage,
        IReadOnlyDictionary<string, object?>? args = null,
        Exception? inner = null)
        : base(devMessage, inner)
    {
        MessageId = messageId;
        Args = args ?? ErrorArgs.None;
    }

    // Catalog key the UI formats for the user; Message stays English text for logs and developers.
    public string MessageId { get; }

    public IReadOnlyDictionary<string, object?> Args { get; }
}
