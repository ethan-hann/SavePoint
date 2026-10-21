using Savepoint.App.Hosting;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Hosting;

public class UnavailableApiTests
{
    private static readonly ErrorInfo _error = new("error.git.unavailable", new Dictionary<string, object?> { ["path"] = "/app/git" }, "detail");

    [Fact]
    public async Task Every_async_call_fails_with_the_same_error()
    {
        var games = UnavailableApi.Create<IGamesApi>(_error);

        var listed = await games.ListAsync(TestContext.Current.CancellationToken);
        var added = await games.AddAsync(new GameDefinition());
        var deleted = await games.DeleteAsync("any");

        Assert.Equal(_error, listed.Error);
        Assert.Equal(_error, added.Error);
        Assert.Equal(_error, deleted.Error);
    }

    [Fact]
    public async Task The_startup_check_reports_the_failure_so_the_startup_screen_can_show_it()
    {
        var status = UnavailableApi.Create<IAppStatusApi>(_error);

        var checkedStatus = await status.CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(checkedStatus.Ok);
        Assert.Equal("error.git.unavailable", checkedStatus.Error?.MessageId);
    }

    [Fact]
    public void Lists_are_empty_and_lookups_find_nothing()
    {
        var games = UnavailableApi.Create<IGamesApi>(_error);
        var jobs = UnavailableApi.Create<IJobsApi>(_error);
        var launch = UnavailableApi.Create<ILaunchApi>(_error);

        Assert.Empty(games.Templates);
        Assert.Empty(jobs.List());
        Assert.Null(jobs.Get(Guid.NewGuid()));
        Assert.Empty(launch.ListSessions());
        Assert.Null(launch.GetSession("any"));
    }

    [Fact]
    public void Subscribing_to_an_event_is_allowed_and_nothing_is_ever_raised()
    {
        var jobs = UnavailableApi.Create<IJobsApi>(_error);
        var raised = 0;

        jobs.JobChanged += (_, _) => raised++;

        Assert.Equal(0, raised);
    }
}
