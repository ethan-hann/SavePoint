using Savepoint.App.Platform;

namespace Savepoint.App.Tests.Platform;

// A machine that will not create the named mutex still gets one running copy, decided by a lock on a file; a copy that holds
// the mutex locks the file too, so either guard alone keeps a second copy out.
public sealed class SingleInstanceFallbackTests : IDisposable
{
    private readonly List<IDisposable> _disposables = [];
    private readonly string _appId = "test-" + Guid.NewGuid().ToString("N");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    // A backslash or slash inside the name names a folder that does not exist, so the machine refuses it as a refusing machine would.
    private static InstanceMutex RefusedMutex(string name) => InstanceMutex.Acquire("Global\\sp-no-such-folder\\mutex/name");

    // A copy on a machine that gives it no mutex, unless a mutex factory says otherwise (InstanceMutex.Acquire is the real one).
    private SingleInstance Create(string? folder = null, Func<string, InstanceMutex>? mutex = null)
    {
        var instance = new SingleInstance(_appId, "test-user", folder ?? SingleInstanceTests.PipeDirectory(), TimeSpan.FromSeconds(10), mutex ?? RefusedMutex);
        _disposables.Add(instance);
        return instance;
    }

    [Fact]
    public void A_copy_the_machine_gives_no_mutex_still_runs_and_keeps_a_second_copy_out()
    {
        var first = Create();
        var second = Create();

        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());
    }

    [Fact]
    public async Task A_second_copy_asks_the_copy_holding_the_lock_file_to_show_itself()
    {
        var first = Create();
        var second = Create();
        var activations = new SemaphoreSlim(0);
        first.ActivationRequested += (_, _) => activations.Release();
        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());

        var delivered = second.SignalFirstInstance();

        Assert.True(delivered);
        Assert.True(await activations.WaitAsync(TimeSpan.FromSeconds(10), Ct));
    }

    [Fact]
    public void Once_the_copy_holding_the_lock_file_is_disposed_another_can_take_over()
    {
        var first = Create();
        var second = Create();
        Assert.True(first.TryAcquire());

        first.Dispose();

        Assert.True(second.TryAcquire());
    }

    [Fact]
    public void A_lock_file_left_by_a_copy_that_crashed_does_not_lock_everyone_out()
    {
        var first = Create();
        File.WriteAllText(first.LockFilePath, "left behind");

        Assert.True(first.TryAcquire());
    }

    [Fact]
    public void A_copy_holding_the_mutex_keeps_out_a_copy_the_machine_gives_no_mutex()
    {
        var holder = Create(mutex: InstanceMutex.Acquire);
        var refused = Create();

        Assert.True(holder.TryAcquire());
        Assert.False(refused.TryAcquire());
    }

    [Fact]
    public void A_copy_that_could_take_the_mutex_does_not_run_beside_a_copy_holding_the_lock_file()
    {
        var fileHolder = Create();
        var mutexHolder = Create(mutex: InstanceMutex.Acquire);

        Assert.True(fileHolder.TryAcquire());
        Assert.False(mutexHolder.TryAcquire());
    }

    [Fact]
    public void A_copy_turned_away_by_the_lock_file_lets_go_of_the_mutex()
    {
        var fileHolder = Create();
        var turnedAway = Create(mutex: InstanceMutex.Acquire);
        var later = Create(mutex: InstanceMutex.Acquire);
        Assert.True(fileHolder.TryAcquire());
        Assert.False(turnedAway.TryAcquire());

        fileHolder.Dispose();

        Assert.True(later.TryAcquire());
    }

    [Fact]
    public void A_copy_holding_the_mutex_runs_on_it_alone_when_the_folder_cannot_be_written()
    {
        var folder = Path.Combine(SingleInstanceTests.PipeDirectory(), "no-such-folder-" + Guid.NewGuid().ToString("N"));
        var first = Create(folder, InstanceMutex.Acquire);
        var second = Create(folder, InstanceMutex.Acquire);

        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());
    }

    [Fact]
    public void A_copy_holding_the_mutex_runs_on_it_alone_when_the_lock_path_cannot_be_opened_as_a_file()
    {
        var first = Create(mutex: InstanceMutex.Acquire);
        var second = Create(mutex: InstanceMutex.Acquire);
        Directory.CreateDirectory(first.LockFilePath);

        try
        {
            Assert.True(first.TryAcquire());
            Assert.False(second.TryAcquire());
        }
        finally
        {
            first.Dispose();
            Directory.Delete(first.LockFilePath);
        }
    }

    [Fact]
    public void A_lock_file_left_by_a_copy_that_crashed_does_not_stop_a_copy_holding_the_mutex()
    {
        var first = Create(mutex: InstanceMutex.Acquire);
        File.WriteAllText(first.LockFilePath, "left behind");

        Assert.True(first.TryAcquire());
    }

    [Fact]
    public void With_neither_a_mutex_nor_a_lock_file_no_copy_runs()
    {
        var first = Create(Path.Combine(SingleInstanceTests.PipeDirectory(), "no-such-folder-" + Guid.NewGuid().ToString("N")));

        Assert.False(first.TryAcquire());
    }
}
