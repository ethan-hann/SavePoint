namespace Savepoint.Core.Errors;

/// <summary>
/// Base of every error SavePoint raises on purpose. It carries a catalog key the UI formats for the user, the values that fill
/// the message's placeholders, and an English message for logs. The Api facade turns one into an <see cref="ErrorInfo"/>.
/// </summary>
public abstract class SavepointException : Exception
{
    /// <summary>Creates the error.</summary>
    /// <param name="messageId">Catalog key the UI formats, such as <c>error.diskSpace.low</c>.</param>
    /// <param name="devMessage">English text for logs and "Show details"; it becomes <see cref="Exception.Message"/>.</param>
    /// <param name="args">Values for the message's placeholders and extras the UI can use; null for none.</param>
    /// <param name="inner">The exception that caused this one, when there was one.</param>
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

    /// <summary>
    /// Catalog key the UI formats for the user (<c>error.pathSafety.tier1Blocked</c>, say). Stable across versions and never shown
    /// as it is; <see cref="Exception.Message"/> stays English text for logs and developers.
    /// </summary>
    public string MessageId { get; }

    /// <summary>
    /// Values for the message's ICU placeholders plus extras such as <c>path</c> or <c>role</c>; empty, never null, when the error
    /// has none. Enum values are still enums here; <see cref="ErrorInfo"/> turns them into camelCase strings.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Args { get; }
}
