using Savepoint.App.Features.Activity;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.History;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.Core.Api;
using static Savepoint.App.Tests.Fakes.MockupScenario;

namespace Savepoint.App.Tests.Features.Activity;

// The real shell with the Activity screen the production factory builds, over the fake backend; times read in the mockup's zone (UTC-5).
internal sealed class ActivityRig
{
    private ActivityRig(ShellRig shell, ActivityScreenViewModel screen, FakeBackend backend, UiFormatter text)
    {
        ShellRig = shell;
        Screen = screen;
        Backend = backend;
        Text = text;
    }

    public ShellRig ShellRig { get; }

    public ShellViewModel Shell => ShellRig.Shell;

    public ActivityScreenViewModel Screen { get; }

    public FakeBackend Backend { get; }

    public UiFormatter Text { get; }

    // Starts the shell on Games and leaves Activity unvisited, so a test decides when the screen first shows.
    public static async Task<ActivityRig> StartAsync(
        FakeBackend? backend = null,
        IUiDispatcher? dispatcher = null,
        Func<IJobsApi, IJobsApi>? jobs = null)
    {
        backend ??= FakeBackend.Mockup();
        dispatcher ??= new ImmediateUiDispatcher();
        var text = HistoryRig.FormatterFor(backend);
        var factory = new CapturingFactory(new ActivityScreenFactory(new ActivityDependencies(jobs is null ? backend.Jobs : jobs(backend.Jobs), text, dispatcher, backend.Clock)));
        var shell = await ShellRig.StartAsync(backend, dispatcher, screens: [factory]);
        return new ActivityRig(shell, (ActivityScreenViewModel)factory.Created!, backend, text);
    }

    // Shows the screen the way the nav item does.
    public static async Task<ActivityRig> ShownAsync(FakeBackend? backend = null, IUiDispatcher? dispatcher = null, Func<IJobsApi, IJobsApi>? jobs = null)
    {
        var rig = await StartAsync(backend, dispatcher, jobs);
        rig.Shell.Navigate(NavDestination.Activity);
        return rig;
    }

    public async Task<Guid> StartCaptureAsync(string gameId = SkyrimId, string profileId = ModdedId, string snapshot = "After the Blackreach run")
    {
        var started = await Backend.Profiles.CaptureAsync(gameId, profileId, snapshot);
        return FakeAssert.Ok(started).JobId;
    }

    public async Task<Guid> StartFreeUpAsync(string gameId = SkyrimId) => FakeAssert.Ok(await Backend.Games.OptimizeStorageAsync(gameId)).JobId;

    public async Task<Guid> StartMoveAsync() => FakeAssert.Ok(await Backend.Settings.RelocateStorageAsync(@"D:\SavePoint")).JobId;

    public async Task<Guid> StartClearAsync() => FakeAssert.Ok(await Backend.Settings.ClearAllDataAsync()).JobId;

    // Jobs start at the moment the fake clock says, so a test moves the clock to give them an order.
    public void Pass(TimeSpan by) => Backend.Clock.Advance(by);

    public IEnumerable<string> RowTitles() => Screen.Rows.Select(row => row.Title);

    private sealed class CapturingFactory(IScreenFactory inner) : IScreenFactory
    {
        public NavDestination Destination => inner.Destination;

        public IScreen? Created { get; private set; }

        public IScreen Create(IShellContext shell) => Created = inner.Create(shell);
    }
}

// A jobs Api whose cancel is accepted and then does nothing, as when a job reaches a point it cannot stop at between the press and the call.
internal sealed class IgnoringCancelJobs(IJobsApi inner) : IJobsApi
{
    public event EventHandler<JobSnapshot>? JobChanged
    {
        add => inner.JobChanged += value;
        remove => inner.JobChanged -= value;
    }

    public List<Guid> Asked { get; } = [];

    public IReadOnlyList<JobSnapshot> List() => inner.List();

    public JobSnapshot? Get(Guid jobId) => inner.Get(jobId);

    public Task<OperationResult<Unit>> CancelAsync(Guid jobId)
    {
        Asked.Add(jobId);
        return Task.FromResult(OperationResult.Success());
    }
}
