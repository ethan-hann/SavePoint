using Savepoint.App.Platform;
using Savepoint.App.Tests.Platform.Fakes;

namespace Savepoint.App.Tests.Platform;

public sealed class InstanceMutexTests : IDisposable
{
    private readonly List<IDisposable> _disposables = [];
    private readonly string _name = "Global\\sp-test-" + Guid.NewGuid().ToString("N")[..16];

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    private InstanceMutex Acquire(string? name = null)
    {
        var mutex = InstanceMutex.Acquire(name ?? _name);
        _disposables.Add(mutex);
        return mutex;
    }

    [Fact]
    public void The_first_to_ask_holds_the_mutex()
    {
        Assert.Equal(MutexOutcome.Held, Acquire().Outcome);
    }

    [Fact]
    public void A_second_to_ask_finds_it_taken_even_in_the_same_process()
    {
        var first = Acquire();
        Assert.Equal(MutexOutcome.Held, first.Outcome);

        var second = Acquire();

        Assert.Equal(MutexOutcome.Taken, second.Outcome);
    }

    [Fact]
    public void Once_the_holder_lets_go_the_next_to_ask_holds_it()
    {
        var first = Acquire();
        Assert.Equal(MutexOutcome.Held, first.Outcome);

        first.Dispose();

        Assert.Equal(MutexOutcome.Held, Acquire().Outcome);
    }

    [Fact]
    public void A_waiter_that_was_refused_does_not_disturb_the_holder()
    {
        var first = Acquire();
        Assert.Equal(MutexOutcome.Held, first.Outcome);
        var second = Acquire();
        Assert.Equal(MutexOutcome.Taken, second.Outcome);

        second.Dispose();

        Assert.Equal(MutexOutcome.Taken, Acquire().Outcome);
    }

    [Fact]
    public void Another_name_is_another_mutex()
    {
        var first = Acquire();
        Assert.Equal(MutexOutcome.Held, first.Outcome);

        Assert.Equal(MutexOutcome.Held, Acquire(_name + "-other").Outcome);
    }

    [Fact]
    public void A_mutex_left_held_by_a_crashed_copy_is_ours_to_take()
    {
        var crashed = MutexCrash.LeaveHeld(_name);

        var outcome = Acquire().Outcome;

        GC.KeepAlive(crashed);
        Assert.Equal(MutexOutcome.Held, outcome);
    }

    [Fact]
    public void The_simulated_crash_really_leaves_the_mutex_abandoned()
    {
        var name = _name + "-sanity";
        var crashed = MutexCrash.LeaveHeld(name);
        using var waiter = new Mutex(false, name);

        Assert.Throws<AbandonedMutexException>(() => waiter.WaitOne(0));
        GC.KeepAlive(crashed);
    }

    [Fact]
    public void Letting_go_twice_is_harmless()
    {
        var first = Acquire();
        Assert.Equal(MutexOutcome.Held, first.Outcome);

        first.Dispose();
        first.Dispose();

        Assert.Equal(MutexOutcome.Held, Acquire().Outcome);
    }

    [Fact]
    public void A_name_the_machine_will_not_create_is_unavailable_not_an_error()
    {
        // A backslash or slash inside the name names a folder that does not exist, on Windows and on Unix alike.
        var outcome = Acquire("Global\\sp-no-such-folder\\mutex/name").Outcome;

        Assert.Equal(MutexOutcome.Unavailable, outcome);
    }
}
