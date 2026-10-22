using Savepoint.App.Platform;

namespace Savepoint.App.Tests.Platform;

// A machine that will not create the named mutex still gets one running copy, decided by a lock on a file.
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

    private SingleInstance Create(string? folder = null)
    {
        var instance = new SingleInstance(_appId, "test-user", folder ?? SingleInstanceTests.PipeDirectory(), TimeSpan.FromSeconds(10), RefusedMutex);
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
    public void With_neither_a_mutex_nor_a_lock_file_no_copy_runs()
    {
        var first = Create(Path.Combine(SingleInstanceTests.PipeDirectory(), "no-such-folder-" + Guid.NewGuid().ToString("N")));

        Assert.False(first.TryAcquire());
    }
}
