using System.Runtime.CompilerServices;
using Savepoint.Core.Api;
using Savepoint.Core.Diagnostics;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Services;

/// <summary>The one place an exception becomes an <see cref="OperationResult{T}"/>, so none ever crosses the Api boundary.</summary>
internal static class ApiGuard
{
    /// <summary>
    /// Runs the work and answers its value, or the <see cref="Savepoint.Core.Errors.ErrorInfo"/> of whatever it threw. A
    /// cancelled token answers <c>error.operation.cancelled</c> (D23) and writes nothing, because it is not a failure. Every
    /// failure is written to the log (D34): a <see cref="SavepointException"/> as a warning, because SavePoint raised it on purpose,
    /// and anything else as an error.
    /// </summary>
    /// <param name="log">Where a failure is recorded.</param>
    /// <param name="body">The work.</param>
    /// <param name="cancellationToken">The caller's token; checked before the work starts.</param>
    /// <param name="operation">The Api member that called, filled in by the compiler.</param>
    public static async Task<OperationResult<T>> RunAsync<T>(IDiagnosticLog log, Func<Task<T>> body, CancellationToken cancellationToken = default, [CallerMemberName] string operation = "")
        where T : notnull
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return OperationResult.Success(await body().ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // ErrorInfo.From knows only SavePoint's own errors, so the caller's cancellation is named here (D23). It is not logged: screens cancel their reads whenever the user moves on.
            return OperationResult.Failure<T>(new RequestCancelledException());
        }
        catch (Exception exception)
        {
            var error = ErrorInfo.From(exception);
            var message = $"{operation} failed with {error.MessageId}: {exception.Message}";
            if (exception is SavepointException)
            {
                log.Warning(message, exception);
            }
            else
            {
                log.Error(message, exception);
            }

            return OperationResult.Failure<T>(error);
        }
    }
}
