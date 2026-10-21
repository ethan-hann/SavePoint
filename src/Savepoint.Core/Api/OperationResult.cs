using System.Diagnostics.CodeAnalysis;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>
/// The answer to every async Api call: a non-null <see cref="Value"/> when it worked, or an <see cref="ErrorInfo"/> when it did not.
/// Exceptions never cross the Api boundary.
/// </summary>
/// <typeparam name="T">The value a successful call returns; <see cref="Unit"/> for a call with nothing to return.</typeparam>
/// <remarks>
/// Rules that hold for every Api member. A read takes a <see cref="CancellationToken"/>, and a read whose token is cancelled fails
/// with <c>error.operation.cancelled</c> (D23), which callers discard rather than show. Long work starts a job and returns
/// <see cref="JobStarted"/> at once, so job starters take no token; the job is cancelled with <see cref="IJobsApi.CancelAsync"/>.
/// Other mutations take no token either, because stopping one halfway is worse than finishing it.
/// </remarks>
public sealed record OperationResult<T>
{
    private OperationResult(T? value, ErrorInfo? error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>The result of the call. Non-null when <see cref="Ok"/> is true, and always null when it is false.</summary>
    public T? Value { get; }

    /// <summary>What went wrong. Null when <see cref="Ok"/> is true, and always set when it is false.</summary>
    public ErrorInfo? Error { get; }

    /// <summary>True when the call worked. The compiler then knows <see cref="Value"/> is set, and otherwise that <see cref="Error"/> is.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool Ok => Error is null;

    /// <summary>
    /// Makes a successful result. A null value would make <see cref="Ok"/> lie about <see cref="Value"/>, so calls with nothing to
    /// return use <see cref="Unit"/>.
    /// </summary>
    /// <param name="value">The value to return; must not be null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static OperationResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value, null);
    }

    /// <summary>Makes a failed result.</summary>
    /// <param name="error">What went wrong; must not be null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public static OperationResult<T> Failure(ErrorInfo error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, error);
    }
}

/// <summary>Shorthand for making <see cref="OperationResult{T}"/> values without spelling out the type argument.</summary>
public static class OperationResult
{
    /// <summary>Makes a successful result for a call with nothing to return.</summary>
    public static OperationResult<Unit> Success() => OperationResult<Unit>.Success(Unit.Value);

    /// <summary>Makes a successful result.</summary>
    /// <param name="value">The value to return; must not be null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static OperationResult<T> Success<T>(T value) => OperationResult<T>.Success(value);

    /// <summary>Makes a failed result from an error that is already in its final shape.</summary>
    /// <param name="error">What went wrong; must not be null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public static OperationResult<T> Failure<T>(ErrorInfo error) => OperationResult<T>.Failure(error);

    /// <summary>
    /// Makes a failed result from an exception, through <see cref="ErrorInfo.From"/>. It is the mapping the facade uses, so a fake
    /// and a real service report an exception the same way.
    /// </summary>
    /// <param name="exception">The exception to report; must not be null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    public static OperationResult<T> Failure<T>(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return OperationResult<T>.Failure(ErrorInfo.From(exception));
    }
}
