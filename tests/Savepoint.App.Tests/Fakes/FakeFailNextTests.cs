using Savepoint.Core.Api;
using Savepoint.Core.Errors;
using static Savepoint.App.Tests.Fakes.FakeAssert;
using static Savepoint.App.Tests.Fakes.SampleScenario;

namespace Savepoint.App.Tests.Fakes;

// The scripted failures screen tests rely on to reach their error paths.
public class FakeFailNextTests
{
    private static readonly ErrorInfo _diskFull = ErrorInfo.From(new DiskSpaceException(StorageRoot, 5, 1));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FailNext_fails_only_the_next_call_to_that_member()
    {
        var backend = FakeBackend.Sample();
        backend.FailNext<IGamesApi>(nameof(IGamesApi.ListAsync), _diskFull);

        var first = await backend.Games.ListAsync(Ct);
        var second = await backend.Games.ListAsync(Ct);

        Assert.Same(_diskFull, first.Error);
        Assert.True(second.Ok);
    }

    [Fact]
    public async Task FailNext_queues_one_failure_per_call()
    {
        var backend = FakeBackend.Sample();
        var other = ErrorInfo.From(new GitUnavailableException(@"C:\git", "missing", null));
        backend.FailNext<IGamesApi>(nameof(IGamesApi.ListAsync), _diskFull);
        backend.FailNext<IGamesApi>(nameof(IGamesApi.ListAsync), other);

        Assert.Same(_diskFull, (await backend.Games.ListAsync(Ct)).Error);
        Assert.Same(other, (await backend.Games.ListAsync(Ct)).Error);
        Assert.True((await backend.Games.ListAsync(Ct)).Ok);
    }

    [Fact]
    public async Task A_failed_call_is_recorded_and_changes_nothing()
    {
        var backend = FakeBackend.Empty();
        backend.FailNext<IGamesApi>(nameof(IGamesApi.AddAsync), _diskFull);

        await backend.Games.AddAsync(Skyrim);

        Assert.Single(backend.Calls.To<IGamesApi>(nameof(IGamesApi.AddAsync)));
        Assert.Empty(Ok(await backend.Games.ListAsync(Ct)));
    }
}
