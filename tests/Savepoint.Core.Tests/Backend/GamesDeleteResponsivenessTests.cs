using System.Collections.Concurrent;
using Savepoint.Core.Api;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

// A large delete must not hold the thread that asked for it: that thread is the window's (D31).
public class GamesDeleteResponsivenessTests
{
    // Only waited out when the test is failing; a delete that frees the thread returns at once.
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(20);

    [Fact(Timeout = 120_000)]
    public async Task Deleting_a_game_hands_the_calling_thread_back_while_the_folder_is_still_being_deleted()
    {
        using var reachedFolder = new ManualResetEventSlim();
        using var letDeleteFinish = new ManualResetEventSlim();
        var deletingThread = 0;
        var testToken = TestContext.Current.CancellationToken;
        using var workspace = new BackendWorkspace(options => options with
        {
            Engine = new GitEngineOptions
            {
                // The slow part, held shut until the test opens it.
                BeforeGitDirDelete = _ =>
                {
                    deletingThread = Environment.CurrentManagedThreadId;
                    reachedFolder.Set();
                    letDeleteFinish.Wait(testToken);
                },
            },
        });
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        using var window = new SingleThreadContext();
        var callReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<OperationResult<Unit>>? deleting = null;

        window.Post(
            _ =>
            {
                deleting = workspace.Games.DeleteAsync(game.Id);
                callReturned.SetResult();
            },
            null);

        try
        {
            var first = await Task.WhenAny(callReturned.Task, Task.Delay(_patience, workspace.Token));
            Assert.True(first == callReturned.Task, "the call did not return to the calling thread while the folder delete was held shut");
            Assert.True(reachedFolder.Wait(_patience, workspace.Token), "the delete never reached the folder");
            Assert.NotEqual(window.ThreadId, deletingThread);
            Assert.True(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id)), "the folder was already gone when the call returned");
        }
        finally
        {
            letDeleteFinish.Set();
        }

        var deleted = await deleting!;
        Assert.True(deleted.Ok, deleted.Error?.Detail);
        Assert.False(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id)));
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

        public int ThreadId => _thread.ManagedThreadId;

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                _queue.Add((d, state));
            }
            catch (InvalidOperationException)
            {
                // Work that outlives a failed test has no thread left to run on.
            }
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
