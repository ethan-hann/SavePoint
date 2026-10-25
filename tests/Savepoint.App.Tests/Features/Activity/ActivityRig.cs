using Savepoint.App.Features.Activity;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.History;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using static Savepoint.App.Tests.Fakes.SampleScenario;

namespace Savepoint.App.Tests.Features.Activity;

// The real shell with the Activity screen the production factory builds, over the fake backend; times read in the sample scenario's zone (UTC-5).
internal sealed class ActivityRig
{
    private ActivityRig(ShellRig shell, ActivityScreenViewModel screen, FakeBackend backend, UiFormatter text, FakeDialogs dialogs)
    {
        ShellRig = shell;
        Screen = screen;
        Backend = backend;
        Text = text;
        Dialogs = dialogs;
    }

    // What the screen asks with; the shell has its own.
    public FakeDialogs Dialogs { get; }

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
        backend ??= FakeBackend.Sample();
        dispatcher ??= new ImmediateUiDispatcher();
        var text = HistoryRig.FormatterFor(backend);
        var dialogs = new FakeDialogs();
        var factory = new CapturingFactory(new ActivityScreenFactory(new ActivityDependencies(jobs is null ? backend.Jobs : jobs(backend.Jobs), dialogs, text, dispatcher, backend.Clock)));
        var shell = await ShellRig.StartAsync(backend, dispatcher, screens: [factory]);
        return new ActivityRig(shell, (ActivityScreenViewModel)factory.Created!, backend, text, dialogs);
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

    public async Task<Guid> StartLaunchAsync(string gameId = SkyrimId, string profileId = ModdedId) =>
        FakeAssert.Ok(await Backend.Launch.StartAsync(gameId, new LaunchTarget.ProfileTip(profileId))).JobId;

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
// The jobs the Activity screen is drawn with: a game's canceled, partly done, failed and finished jobs, and one still running with its files.
internal static class ActivityScenario
{
    public static async Task AddJobsAsync(FakeBackend backend)
    {
        var canceled = FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(SkyrimId)).JobId;
        backend.Jobs.ReportStep(canceled, JobSteps.CompactFolderFailed, ("game", "Skyrim Special Edition"), ("folder", RepoRole.External));
        backend.Jobs.CompleteCancelled(canceled);
        backend.Clock.Advance(TimeSpan.FromMinutes(4));

        var partial = FakeAssert.Ok(await backend.Profiles.CaptureAsync(SkyrimId, ModdedId, "Before the final boss")).JobId;
        backend.Jobs.ReportStep(partial, JobSteps.CaptureFolder, ("folder", RepoRole.Install), ("path", @"C:\Games\Skyrim Special Edition"));
        backend.Jobs.ReportStep(partial, JobSteps.CaptureFolderDone, ("folder", RepoRole.Install), ("files", 14));
        backend.Jobs.ReportStep(partial, JobSteps.CaptureFolderFailed, ("folder", RepoRole.External));
        backend.Jobs.Complete(partial, HalfSaved);
        backend.Clock.Advance(TimeSpan.FromMinutes(7));

        var failed = FakeAssert.Ok(await backend.Profiles.CaptureAsync(SkyrimId, ModdedId, "After the patch")).JobId;
        backend.Jobs.Fail(failed, FileInUseError);
        backend.Clock.Advance(TimeSpan.FromMinutes(5));

        var done = FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(SkyrimId)).JobId;
        backend.Jobs.ReportStep(done, JobSteps.CompactFreed, ("game", "Skyrim Special Edition"), ("bytes", 3_400_000_000L));
        backend.Jobs.Complete(done);
        backend.Clock.Advance(TimeSpan.FromMinutes(3));

        var running = FakeAssert.Ok(await backend.Profiles.CaptureAsync(SkyrimId, ModdedId, "Session 7-8-26")).JobId;
        backend.Jobs.ReportStep(running, JobSteps.CaptureFolder, ("folder", RepoRole.Install), ("path", @"C:\Games\Skyrim Special Edition"));
        for (var i = 1; i <= 30; i++)
        {
            backend.Jobs.ReportProgress(running, new JobProgress(FilesDone: i, FilesTotal: 120, CurrentPath: $@"Data\Textures\Landscape\tile{i:D3}.dds"));
        }

        backend.Clock.Advance(TimeSpan.FromSeconds(40));
    }
}

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

    public IReadOnlyList<JobLine> GetLines(Guid jobId) => inner.GetLines(jobId);

    public Task<OperationResult<Unit>> CancelAsync(Guid jobId)
    {
        Asked.Add(jobId);
        return Task.FromResult(OperationResult.Success());
    }
}
