using System.Diagnostics.CodeAnalysis;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

// Exceptions never cross the Api boundary; every call answers with a value or an ErrorInfo.
public sealed record OperationResult<T>
{
    private OperationResult(T? value, ErrorInfo? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public ErrorInfo? Error { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool Ok => Error is null;

    // A null value would make Ok lie about Value; calls with nothing to return use Unit.
    public static OperationResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value, null);
    }

    public static OperationResult<T> Failure(ErrorInfo error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, error);
    }
}

public static class OperationResult
{
    public static OperationResult<Unit> Success() => OperationResult<Unit>.Success(Unit.Value);

    public static OperationResult<T> Success<T>(T value) => OperationResult<T>.Success(value);

    public static OperationResult<T> Failure<T>(ErrorInfo error) => OperationResult<T>.Failure(error);

    // Same mapping the facade uses, so a fake and a real service report an exception identically.
    public static OperationResult<T> Failure<T>(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return OperationResult<T>.Failure(ErrorInfo.From(exception));
    }
}
