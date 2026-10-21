namespace Savepoint.Core.Errors;

public sealed record ErrorInfo(string MessageId, IReadOnlyDictionary<string, object?> Args, string Detail)
{
    public static ErrorInfo From(Exception ex) => ex switch
    {
        SavepointException known => new ErrorInfo(known.MessageId, known.Args, ex.ToString()),
        _ => new ErrorInfo("error.unknown", ErrorArgs.None, ex.ToString()),
    };
}
