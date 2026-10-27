using Savepoint.Core.Api;

namespace Savepoint.App.Features.Common;

/// <summary>
/// Asks the backend, in the background, about how much space Free up space would free, and keeps that to one question for each preview.
/// The screens show the number only when it is ready: while it is being worked out, or when it cannot be, the words say nothing about size.
/// </summary>
internal sealed class FreeUpSizeEstimate : IDisposable
{
    // How long a confirmation waits for a size that is still being worked out before it asks without one. Long enough for the usual
    // store; a bigger one keeps working and the next question has it.
    private static readonly TimeSpan _defaultGrace = TimeSpan.FromMilliseconds(1500);

    private readonly TimeProvider _clock;
    private readonly TimeSpan _grace;
    private CancellationTokenSource _running = new();
    private IReadOnlyList<FreeUpSpaceRecord>? _askedFor;
    private Task<long?>? _task;
    private bool _disposed;

    /// <param name="clock">Times the wait of <see cref="WaitAsync"/>.</param>
    /// <param name="grace">How long <see cref="WaitAsync"/> waits; null uses the usual wait.</param>
    public FreeUpSizeEstimate(TimeProvider clock, TimeSpan? grace = null)
    {
        _clock = clock;
        _grace = grace ?? _defaultGrace;
    }

    /// <summary>
    /// Starts asking for the preview's size, or joins the question still running for the same records. A different preview cancels the
    /// question before it.
    /// </summary>
    /// <param name="token">The preview's token.</param>
    /// <param name="ask">Asks the backend; it is given the token to ask about and one that is cancelled when the question is dropped.</param>
    /// <returns>The size in bytes; null when it is zero, cannot be worked out, or the question was dropped.</returns>
    public Task<long?> Start(FreeUpSpaceToken token, Func<FreeUpSpaceToken, CancellationToken, Task<OperationResult<long>>> ask)
    {
        // A click whose preview comes back after the screen is gone must not ask or throw.
        if (_disposed)
        {
            return Task.FromResult<long?>(null);
        }

        // Only a question still running is shared. One that has answered may be out of date (a snapshot kept since changes what is
        // freed while the records stay the same), and the backend remembers its answer until the store changes, so asking again is quick.
        if (_task is { IsCompleted: false } && _askedFor is not null && _askedFor.SequenceEqual(token.Records))
        {
            return _task;
        }

        Cancel();
        _running = new CancellationTokenSource();
        _askedFor = token.Records;
        return _task = AskAsync(token, ask, _running.Token);
    }

    /// <summary>Waits for a question started by <see cref="Start"/>, but only as long as the grace allows.</summary>
    /// <param name="estimate">The task <see cref="Start"/> returned.</param>
    /// <returns>The size; null when it did not arrive in time or there is none.</returns>
    public async Task<long?> WaitAsync(Task<long?> estimate)
    {
        if (estimate.IsCompleted)
        {
            return await estimate;
        }

        using var stop = new CancellationTokenSource();
        var timeout = Task.Delay(_grace, _clock, stop.Token);
        var first = await Task.WhenAny(estimate, timeout);
        await stop.CancelAsync();
        return first == estimate ? await estimate : null;
    }

    /// <summary>Drops the question that is running, if any, and forgets it.</summary>
    public void Cancel()
    {
        if (_disposed)
        {
            return;
        }

        _running.Cancel();
        _askedFor = null;
        _task = null;
    }

    /// <summary>Drops the question that is running. Asking or cancelling afterwards does nothing.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _running.Cancel();
        _running.Dispose();
    }

    private static async Task<long?> AskAsync(FreeUpSpaceToken token, Func<FreeUpSpaceToken, CancellationToken, Task<OperationResult<long>>> ask, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await ask(token, cancellationToken);

            // A failure is logged by the backend that had it; here it only means there is no size to show.
            return answer.Ok && answer.Value > 0 && !cancellationToken.IsCancellationRequested ? answer.Value : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
