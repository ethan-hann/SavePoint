using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Services;

/// <summary>The one place an exception becomes an <see cref="OperationResult{T}"/>, so none ever crosses the Api boundary.</summary>
internal static class ApiGuard
{
    /// <summary>
    /// Runs the work and answers its value, or the <see cref="Savepoint.Core.Errors.ErrorInfo"/> of whatever it threw. A
    /// cancelled token answers <c>error.operation.cancelled</c> (D23).
    /// </summary>
    /// <param name="body">The work.</param>
    /// <param name="cancellationToken">The caller's token; checked before the work starts.</param>
    public static async Task<OperationResult<T>> RunAsync<T>(Func<Task<T>> body, CancellationToken cancellationToken = default)
        where T : notnull
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return OperationResult.Success(await body().ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // ErrorInfo.From knows only SavePoint's own errors, so the caller's cancellation is named here (D23).
            return OperationResult.Failure<T>(new RequestCancelledException());
        }
        catch (Exception exception)
        {
            return OperationResult.Failure<T>(exception);
        }
    }
}
