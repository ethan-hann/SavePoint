namespace Savepoint.Core.Errors;

/// <summary>The only error shape that leaves Core: a catalog key, the values to format it with, and developer detail.</summary>
/// <param name="MessageId">
/// Catalog key the UI formats, such as <c>error.diskSpace.low</c>; <c>error.unknown</c> when the cause was not an error SavePoint
/// raises on purpose.
/// </param>
/// <param name="Args">
/// Values for the message's ICU placeholders plus extras such as <c>path</c> or <c>role</c>. The dictionary is copied when it is
/// set and enum values become camelCase strings, so an ICU select can match them and a later change to the caller's dictionary
/// never shows.
/// </param>
/// <param name="Detail">English text for logs and "Show details": the exception's full <c>ToString()</c>. Never the user's copy.</param>
public sealed record ErrorInfo(string MessageId, IReadOnlyDictionary<string, object?> Args, string Detail)
{
    private readonly IReadOnlyDictionary<string, object?> _args = ErrorArgs.Normalize(Args);

    /// <summary>
    /// Values for the message's placeholders. Always a snapshot taken when the value was set; enum values are camelCase strings so
    /// an ICU select can branch on them.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Args
    {
        get => _args;
        init => _args = ErrorArgs.Normalize(value);
    }

    /// <summary>Maps an exception to the shape the UI formats.</summary>
    /// <param name="ex">The exception to report.</param>
    /// <returns>
    /// For a <see cref="SavepointException"/>, its own id and args. Anything else, a plain <see cref="OperationCanceledException"/>
    /// included, becomes <c>error.unknown</c> with no args. <see cref="Detail"/> is the exception's <c>ToString()</c> either way.
    /// </returns>
    public static ErrorInfo From(Exception ex) => ex switch
    {
        SavepointException known => new ErrorInfo(known.MessageId, known.Args, ex.ToString()),
        _ => new ErrorInfo("error.unknown", ErrorArgs.None, ex.ToString()),
    };
}
