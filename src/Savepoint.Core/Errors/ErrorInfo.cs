namespace Savepoint.Core.Errors;

public sealed record ErrorInfo(string MessageId, IReadOnlyDictionary<string, object?> Args, string Detail)
{
    private readonly IReadOnlyDictionary<string, object?> _args = ErrorArgs.Normalize(Args);

    // Enum values arrive as camelCase strings so an ICU select can branch on them.
    public IReadOnlyDictionary<string, object?> Args
    {
        get => _args;
        init => _args = ErrorArgs.Normalize(value);
    }

    public static ErrorInfo From(Exception ex) => ex switch
    {
        SavepointException known => new ErrorInfo(known.MessageId, known.Args, ex.ToString()),

        // A cancelled token is the caller's own doing; it must not read as an unknown failure (D23).
        OperationCanceledException => ErrorInfo.From(new RequestCancelledException()),
        _ => new ErrorInfo("error.unknown", ErrorArgs.None, ex.ToString()),
    };
}
