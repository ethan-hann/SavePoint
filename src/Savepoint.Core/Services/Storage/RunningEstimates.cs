using Savepoint.Core.Errors;

namespace Savepoint.Core.Services.Storage;

// The Free up space size estimates that are walking a game's git directories right now. They read without the game's lock, so a long
// walk would otherwise keep a pack open while the game is deleted. Deleting a game stops them and waits for them to let go instead of
// taking the lock for them, which would hold up snapshots and launches for as long as a walk takes.
internal sealed class RunningEstimates
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<Running>> _running = new(StringComparer.Ordinal);
    private readonly HashSet<string> _closed = new(StringComparer.Ordinal);

    private sealed record Running(CancellationTokenSource Source, TaskCompletionSource Finished);

    // Runs the walk, unless the game is being deleted (then it is cancelled before it starts).
    public async Task<T> RunAsync<T>(string gameId, CancellationToken cancellationToken, Func<CancellationToken, Task<T>> walk)
    {
        Running entry;
        lock (_gate)
        {
            if (_closed.Contains(gameId))
            {
                throw new RequestCancelledException();
            }

            entry = new Running(CancellationTokenSource.CreateLinkedTokenSource(cancellationToken), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            if (!_running.TryGetValue(gameId, out var list))
            {
                _running[gameId] = list = [];
            }

            list.Add(entry);
        }

        try
        {
            return await walk(entry.Source.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The caller did not cancel, so the game's deletion did; the call ends as a cancelled request rather than an unknown failure.
            throw new RequestCancelledException();
        }
        finally
        {
            lock (_gate)
            {
                _running[gameId].Remove(entry);
                entry.Source.Dispose();
            }

            entry.Finished.SetResult();
        }
    }

    // Cancels every walk of the game and returns once each has ended. Walks asked for until the returned scope is disposed are cancelled too.
    public async Task<IDisposable> StopAsync(string gameId)
    {
        Task[] ending;
        lock (_gate)
        {
            _closed.Add(gameId);
            var list = _running.GetValueOrDefault(gameId) ?? [];
            foreach (var entry in list)
            {
                entry.Source.Cancel();
            }

            ending = [.. list.Select(entry => entry.Finished.Task)];
        }

        await Task.WhenAll(ending).ConfigureAwait(false);
        return new Scope(this, gameId);
    }

    private sealed class Scope(RunningEstimates owner, string gameId) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate)
            {
                owner._closed.Remove(gameId);
            }
        }
    }
}
