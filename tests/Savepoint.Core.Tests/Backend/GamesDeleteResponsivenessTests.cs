using System.Collections.Concurrent;
using System.Diagnostics;

namespace Savepoint.Core.Tests.Backend;

// A large delete must not hold the thread that asked for it: that thread is the window's (D31).
public class GamesDeleteResponsivenessTests
{
    private static readonly TimeSpan _longestAllowedGap = TimeSpan.FromMilliseconds(250);

    [Fact(Timeout = 180_000)]
    public async Task Deleting_a_game_with_a_large_history_never_blocks_the_calling_thread_for_long()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        var padding = Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git", "objects", "padding");
        Directory.CreateDirectory(padding);
        Parallel.For(0, 30_000, i => File.WriteAllText(Path.Combine(padding, i + ".tmp"), "x"));
        using var window = new SingleThreadContext();
        var beats = new ConcurrentQueue<long>();
        using var heartbeat = new Timer(_ => window.Post(_ => beats.Enqueue(Stopwatch.GetTimestamp()), null), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(20));

        var deleted = await window.InvokeAsync(() => workspace.Games.DeleteAsync(game.Id));
        await Task.Delay(100, workspace.Token);

        Assert.True(deleted.Ok, deleted.Error?.Detail);
        Assert.False(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id)));
        var stamps = beats.ToArray();
        var longest = stamps.Zip(stamps.Skip(1), (before, after) => Stopwatch.GetElapsedTime(before, after)).DefaultIfEmpty().Max();
        Assert.True(longest < _longestAllowedGap, $"the calling thread was held for {longest.TotalMilliseconds:F0} ms at a stretch");
    }

    // One thread that runs what is posted to it, one item at a time, as the window's does.
    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
        private readonly Thread _thread;

        public SingleThreadContext()
        {
            _thread = new Thread(Run) { IsBackground = true };
            _thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public Task<T> InvokeAsync<T>(Func<Task<T>> work)
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(
                async _ =>
                {
                    try
                    {
                        done.SetResult(await work());
                    }
                    catch (Exception e)
                    {
                        done.SetException(e);
                    }
                },
                null);
            return done.Task;
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(5));
        }

        private void Run()
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }
    }
}
