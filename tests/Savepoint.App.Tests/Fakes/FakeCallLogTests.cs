using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using static Savepoint.App.Tests.Fakes.FakeAssert;
using static Savepoint.App.Tests.Fakes.MockupScenario;

namespace Savepoint.App.Tests.Fakes;

public class FakeCallLogTests
{
    private static readonly ErrorInfo _diskFull = ErrorInfo.From(new DiskSpaceException(StorageRoot, 5, 1));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_call_is_recorded_with_its_arguments()
    {
        var backend = FakeBackend.Mockup();

        await backend.Profiles.CaptureAsync(SkyrimId, SurvivalId, "Before switching", [RepoRole.External]);

        var call = Assert.Single(backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));
        Assert.Equal(SkyrimId, call.Args[0]);
        Assert.Equal(SurvivalId, call.Args[1]);
        Assert.Equal("Before switching", call.Args[2]);
        Assert.Equal<RepoRole>([RepoRole.External], (IEnumerable<RepoRole>)call.Args[3]!);
    }

    [Fact]
    public async Task Calls_across_apis_are_kept_in_the_order_they_were_made()
    {
        var backend = FakeBackend.Mockup();

        await backend.Settings.GetAsync(Ct);
        await backend.Games.ListAsync(Ct);
        await backend.History.GetAsync(SkyrimId, cancellationToken: Ct);

        Assert.Equal<string>(
            ["ISettingsApi.GetAsync", "IGamesApi.ListAsync", "IHistoryApi.GetAsync"],
            backend.Calls.All.Select(c => $"{c.Api}.{c.Member}"));
    }

    [Fact]
    public async Task Cancellation_tokens_are_left_out_of_the_recorded_arguments()
    {
        var backend = FakeBackend.Mockup();

        await backend.Profiles.GetRowStatsAsync(SkyrimId, Ct);

        var call = Assert.Single(backend.Calls.All);
        Assert.Equal<object?>([SkyrimId], call.Args);
    }

    [Fact]
    public async Task FailNext_fails_only_the_next_call_to_that_member()
    {
        var backend = FakeBackend.Mockup();
        backend.FailNext<IGamesApi>(nameof(IGamesApi.ListAsync), _diskFull);

        var first = await backend.Games.ListAsync(Ct);
        var second = await backend.Games.ListAsync(Ct);

        Assert.Same(_diskFull, first.Error);
        Assert.True(second.Ok);
    }

    [Fact]
    public async Task FailNext_leaves_other_members_alone()
    {
        var backend = FakeBackend.Mockup();
        backend.FailNext<IGamesApi>(nameof(IGamesApi.ListAsync), _diskFull);

        var profiles = await backend.Profiles.ListAsync(cancellationToken: Ct);

        Assert.True(profiles.Ok);
    }

    [Fact]
    public async Task FailNext_queues_one_failure_per_call()
    {
        var backend = FakeBackend.Mockup();
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

    [Fact]
    public async Task Clear_forgets_earlier_calls()
    {
        var backend = FakeBackend.Mockup();
        await backend.Games.ListAsync(Ct);

        backend.Calls.Clear();

        Assert.Empty(backend.Calls.All);
    }
}
