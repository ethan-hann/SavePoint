using Savepoint.Core.Git;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Launch;
using Savepoint.Core.Tests.Services;
using Savepoint.Core.Tests.Services.Exe;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Git;

// A SavePoint that was stopped from outside can leave the git it was running. The next start finds it by where it runs from: the bundled git's folder.
public sealed class BundledGitLeftoversTests : IDisposable
{
    private static readonly TimeSpan _short = TimeSpan.FromMilliseconds(400);

    private readonly TempFolder _temp = new("savepoint-leftovers-");
    private readonly RecordingDiagnosticLog _log = new();
    private readonly List<IDisposable> _bundles = [];

    public void Dispose()
    {
        foreach (var bundle in _bundles)
        {
            bundle.Dispose();
        }

        _temp.Dispose();
    }

    [Fact(Timeout = 120_000)]
    public async Task A_leftover_running_from_the_bundle_is_ended_and_one_running_from_another_folder_is_left_alone()
    {
        var bundle = Bundle("bundle");
        var elsewhere = Bundle("elsewhere");
        var leftover = await bundle.StartLeftoverAsync();
        var bystander = await elsewhere.StartLeftoverAsync();

        var clear = await Leftovers(bundle).EndAsync(null, TestContext.Current.CancellationToken);

        Assert.True(clear, "the sweep reported a process still running");
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => !FakeGameProcess.IsRunning(leftover), TimeSpan.FromSeconds(10)), "the leftover from the bundle is still running");
        Assert.True(FakeGameProcess.IsRunning(bystander), "a program from another folder was ended");
        Assert.NotEmpty(_log.Containing("leftover", leftover.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_folder_that_only_starts_with_the_bundles_name_is_not_the_bundle()
    {
        var bundle = Bundle("git");
        var neighbour = Bundle("git2");
        var bystander = await neighbour.StartLeftoverAsync();

        var clear = await Leftovers(bundle).EndAsync(null, TestContext.Current.CancellationToken);

        Assert.True(clear);
        Assert.True(FakeGameProcess.IsRunning(bystander), "a program from git2 was ended by a sweep of git");
    }

    [Fact(Timeout = 120_000)]
    public async Task A_program_this_program_started_from_the_bundle_is_not_a_leftover()
    {
        var bundle = Bundle("bundle");
        var below = await bundle.StartBelowLiveLauncherAsync();

        var found = Leftovers(bundle).Find();
        var clear = await Leftovers(bundle).EndAsync(null, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(found, process => process.Pid == below);
        Assert.True(clear);
        Assert.True(FakeGameProcess.IsRunning(below), "a program below one of this program's own children was ended");
    }

    [Fact(Timeout = 120_000)]
    public async Task A_program_in_the_list_of_running_git_is_not_a_leftover()
    {
        var bundle = Bundle("bundle");
        var leftover = await bundle.StartLeftoverAsync();

        var found = Leftovers(bundle, isRegistered: id => id == leftover).Find();

        Assert.Empty(found);
        Assert.True(FakeGameProcess.IsRunning(leftover));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_leftover_that_ends_by_itself_within_the_wait_is_not_ended_by_force()
    {
        var bundle = Bundle("bundle");
        var leftover = await bundle.StartLeftoverAsync("--exit-after", "1500");
        var ended = new List<int>();

        var clear = await Leftovers(bundle, wait: TimeSpan.FromSeconds(30), kill: process => ended.Add(process.Pid)).EndAsync(null, TestContext.Current.CancellationToken);

        Assert.True(clear);
        Assert.Empty(ended);
        Assert.False(FakeGameProcess.IsRunning(leftover));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_leftover_that_cannot_be_ended_is_reported_as_still_running()
    {
        var bundle = Bundle("bundle");
        var leftover = await bundle.StartLeftoverAsync();

        var clear = await Leftovers(bundle, kill: _ => { }).EndAsync(null, TestContext.Current.CancellationToken);

        Assert.False(clear, "a process that was not ended was reported as gone");
        Assert.True(FakeGameProcess.IsRunning(leftover));
        Assert.NotEmpty(_log.Containing("could not be ended"));
    }

    [Fact(Timeout = 120_000)]
    public async Task Nothing_is_said_and_nobody_waits_when_nothing_is_left()
    {
        var bundle = Bundle("bundle");
        var told = 0;

        var clear = await Leftovers(bundle, wait: TimeSpan.FromMinutes(5)).EndAsync(() => told++, TestContext.Current.CancellationToken);

        Assert.True(clear);
        Assert.Equal(0, told);
        Assert.Empty(_log.Entries);
    }

    [Fact(Timeout = 120_000)]
    public async Task The_screen_is_told_before_the_wait_begins()
    {
        var bundle = Bundle("bundle");
        await bundle.StartLeftoverAsync();
        var stillRunningWhenTold = false;
        var leftovers = Leftovers(bundle);

        await leftovers.EndAsync(() => stillRunningWhenTold = leftovers.Find().Count > 0, TestContext.Current.CancellationToken);

        Assert.True(stillRunningWhenTold);
    }

    private StandInGitBundle Bundle(string name)
    {
        var bundle = new StandInGitBundle(_temp, name);
        _bundles.Add(bundle);
        return bundle;
    }

    private BundledGitLeftovers Leftovers(StandInGitBundle bundle, Func<int, bool>? isRegistered = null, TimeSpan? wait = null, Action<RunningProcess>? kill = null) =>
        new(FakeGameInstall.Inspector(), bundle.Root, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase, _log, isRegistered, wait ?? _short, _short, TimeSpan.FromMilliseconds(50), kill);
}
