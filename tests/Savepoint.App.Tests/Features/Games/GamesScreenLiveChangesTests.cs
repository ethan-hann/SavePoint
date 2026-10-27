using Savepoint.App.Features.Games;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

// A change made in a game's folders outside SavePoint reaches the profile table while the Games screen stays open. The fake watcher
// raises the changes and the manual clock moves the settle time, so no test sleeps.
public class GamesScreenLiveChangesTests
{
    private static readonly TimeSpan _settle = GamesScreenViewModel.ChangeSettleTime;
    private static readonly TimeSpan _longestWait = GamesScreenViewModel.ChangeLongestWait;

    private static int StatusReads(GamesRig rig) =>
        rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.GetRowStatsAsync)).Count;

    // The profile list is read with the status, so a read of one is a read of the other.
    private static async Task<(GamesRig Rig, ManualTimeProvider Clock)> StartAsync(FakeBackend? backend = null)
    {
        var clock = new ManualTimeProvider();
        var rig = await GamesRig.StartAsync(backend, clock: clock);
        return (rig, clock);
    }

    // What a program writing into the folder does to the state the Api reports.
    private static void EditOutsideSavePoint(GamesRig rig, string profileId = SampleScenario.ModdedId)
    {
        var state = rig.Backend.State;
        rig.Backend.Script(() => state.RowStats[profileId] = state.StatsFor(state.Profiles.Single(profile => profile.Id == profileId)) with { UnsavedChanges = [RepoRole.Install] });
    }

    private static ProfileRowViewModel Modded(GamesRig rig) => rig.Screen.Profiles.Single(row => row.Id == SampleScenario.ModdedId);

    private static async Task UntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "The read never finished.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private static async Task SettledAsync(GamesRig rig, ManualTimeProvider clock, TimeSpan by)
    {
        clock.Advance(by);

        // The fake answers without waiting on anything, so a few turns of the scheduler let a read run to its end.
        for (var turn = 0; turn < 10; turn++)
        {
            await Task.Yield();
        }
    }

    [Fact]
    public async Task A_change_in_a_watched_folder_shows_new_changes_on_the_loaded_profile_once_it_settles()
    {
        var (rig, clock) = await StartAsync();
        var watch = Assert.Single(rig.Watcher.Running);
        Assert.False(Modded(rig).HasUnsavedChanges);

        EditOutsideSavePoint(rig);
        watch.RaiseChanged();
        await SettledAsync(rig, clock, _settle);

        Assert.True(Modded(rig).HasUnsavedChanges);
        Assert.True(Modded(rig).IsUnsavedLine);
    }

    [Fact]
    public async Task A_change_is_not_read_before_the_folder_has_been_quiet_for_the_settle_time()
    {
        var (rig, clock) = await StartAsync();
        var reads = StatusReads(rig);

        Assert.Single(rig.Watcher.Running).RaiseChanged();
        await SettledAsync(rig, clock, _settle - TimeSpan.FromMilliseconds(1));

        Assert.Equal(reads, StatusReads(rig));
    }

    [Fact]
    public async Task A_burst_of_changes_is_read_once()
    {
        var (rig, clock) = await StartAsync();
        var reads = StatusReads(rig);
        var watch = Assert.Single(rig.Watcher.Running);

        for (var i = 0; i < 50; i++)
        {
            watch.RaiseChanged();
            clock.Advance(TimeSpan.FromMilliseconds(10));
        }

        await SettledAsync(rig, clock, _settle);

        Assert.Equal(reads + 1, StatusReads(rig));
    }

    [Fact]
    public async Task Changes_that_arrive_while_a_read_runs_are_read_once_more_when_it_ends()
    {
        var (rig, clock) = await StartAsync();
        var reads = StatusReads(rig);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Backend.Profiles.StatsGate = token => release.Task.WaitAsync(token);
        var watch = Assert.Single(rig.Watcher.Running);

        watch.RaiseChanged();
        clock.Advance(_settle);
        watch.RaiseChanged();
        watch.RaiseChanged();
        clock.Advance(_settle * 3);

        // The gate holds the first read before the fake records it, so nothing is counted until it is let go.
        Assert.Equal(reads, StatusReads(rig));

        rig.Backend.Profiles.StatsGate = null;
        release.SetResult();

        // The read ends on another thread; the extra read is waiting once the timer is armed again.
        await UntilAsync(() => clock.ActiveTimers == 1);
        Assert.Equal(reads + 1, StatusReads(rig));
        await SettledAsync(rig, clock, _settle);

        Assert.Equal(reads + 2, StatusReads(rig));
    }

    [Fact]
    public async Task A_folder_that_never_goes_quiet_is_still_read_within_the_longest_wait()
    {
        var (rig, clock) = await StartAsync();
        var reads = StatusReads(rig);
        var watch = Assert.Single(rig.Watcher.Running);
        var step = _settle / 3;

        for (var waited = TimeSpan.Zero; waited < _longestWait; waited += step)
        {
            watch.RaiseChanged();
            clock.Advance(step);
        }

        await SettledAsync(rig, clock, TimeSpan.Zero);

        Assert.True(StatusReads(rig) > reads);
    }

    [Fact]
    public async Task Changes_while_a_job_of_the_game_runs_are_not_read_and_the_end_of_the_job_reads_once()
    {
        var (rig, clock) = await StartAsync();
        var jobId = Guid.Empty;
        rig.Backend.Script(() => jobId = rig.Backend.Jobs.Start(JobKind.Capture, SampleScenario.SkyrimId, null));
        var reads = StatusReads(rig);

        Assert.Single(rig.Watcher.Running).RaiseChanged();
        await SettledAsync(rig, clock, _settle * 2);
        Assert.Equal(reads, StatusReads(rig));

        rig.Backend.Jobs.Complete(jobId);
        await SettledAsync(rig, clock, _settle * 2);

        Assert.Equal(reads + 1, StatusReads(rig));
    }

    [Fact]
    public async Task A_job_of_another_game_does_not_stop_the_reads()
    {
        var (rig, clock) = await StartAsync();
        rig.Backend.Script(() => rig.Backend.Jobs.Start(JobKind.Capture, SampleScenario.SatisfactoryId, null));
        var reads = StatusReads(rig);

        Assert.Single(rig.Watcher.Running).RaiseChanged();
        await SettledAsync(rig, clock, _settle);

        Assert.Equal(reads + 1, StatusReads(rig));
    }

    [Fact]
    public async Task Changes_while_the_game_runs_through_SavePoint_are_not_read()
    {
        var (rig, clock) = await StartAsync();
        rig.LaunchFlow.SetActive(SampleScenario.SkyrimId, true);
        var reads = StatusReads(rig);

        Assert.Single(rig.Watcher.Running).RaiseChanged();
        await SettledAsync(rig, clock, _settle * 2);

        Assert.Equal(reads, StatusReads(rig));
    }

    [Fact]
    public async Task The_install_saves_and_mods_folders_of_the_game_on_screen_are_watched()
    {
        var (rig, _) = await StartAsync(new FakeBackend(ScenarioBuilder.Empty().WithGame(ModsSamples.SevenDays)));

        var watch = Assert.Single(rig.Watcher.Running);

        Assert.Equal([ModsSamples.InstallPath, ModsSamples.ModsResolved, ModsSamples.SavesResolved], watch.Folders.Order());
    }

    [Fact]
    public async Task Picking_another_game_stops_watching_the_old_games_folders_and_watches_the_new_ones()
    {
        var (rig, _) = await StartAsync();
        var first = Assert.Single(rig.Watcher.Running);

        rig.Shell.SelectGame(SampleScenario.SatisfactoryId);
        await rig.Screen.ReloadAsync();

        Assert.True(first.Disposed);
        var second = Assert.Single(rig.Watcher.Running);
        Assert.NotEqual(first.Folders, second.Folders);
        Assert.Contains(rig.Backend.State.Games.Single(game => game.Id == SampleScenario.SatisfactoryId).Install.Path, second.Folders);
    }

    [Fact]
    public async Task A_change_in_the_old_games_folders_after_picking_another_game_reads_nothing()
    {
        var (rig, clock) = await StartAsync();
        var first = Assert.Single(rig.Watcher.Running);
        rig.Shell.SelectGame(SampleScenario.SatisfactoryId);
        await rig.Screen.ReloadAsync();
        var reads = StatusReads(rig);

        first.RaiseChanged();
        await SettledAsync(rig, clock, _settle * 2);

        Assert.Equal(reads, StatusReads(rig));
    }

    [Fact]
    public async Task A_reload_that_finds_the_same_folders_keeps_the_watch()
    {
        var (rig, _) = await StartAsync();
        var watch = Assert.Single(rig.Watcher.Running);

        await rig.Screen.ReloadAsync();

        Assert.Same(watch, Assert.Single(rig.Watcher.Running));
        Assert.False(watch.Disposed);
    }

    [Fact]
    public async Task Leaving_the_screen_stops_watching_and_coming_back_watches_again()
    {
        var (rig, clock) = await StartAsync();
        var first = Assert.Single(rig.Watcher.Running);

        rig.Shell.Navigate(NavDestination.History);

        Assert.True(first.Disposed);
        Assert.Empty(rig.Watcher.Running);
        var reads = StatusReads(rig);
        first.RaiseChanged();
        await SettledAsync(rig, clock, _settle * 2);
        Assert.Equal(reads, StatusReads(rig));

        rig.Shell.Navigate(NavDestination.Games);
        await rig.Screen.ReloadAsync();

        Assert.Single(rig.Watcher.Running);
    }

    [Fact]
    public async Task A_watch_that_lost_track_of_what_changed_leads_to_one_read()
    {
        var (rig, clock) = await StartAsync();
        var reads = StatusReads(rig);
        var watch = Assert.Single(rig.Watcher.Running);

        watch.RaiseFaulted();
        watch.RaiseFaulted();
        await SettledAsync(rig, clock, _settle);

        Assert.Equal(reads + 1, StatusReads(rig));
    }

    [Fact]
    public async Task Activating_the_window_reads_the_status_again()
    {
        var (rig, clock) = await StartAsync();
        var reads = StatusReads(rig);
        EditOutsideSavePoint(rig);

        rig.Screen.OnWindowActivated();
        await SettledAsync(rig, clock, _settle);

        Assert.Equal(reads + 1, StatusReads(rig));
        Assert.True(Modded(rig).HasUnsavedChanges);
    }

    [Fact]
    public async Task Activating_the_window_while_the_game_runs_through_SavePoint_reads_nothing()
    {
        var (rig, clock) = await StartAsync();
        rig.LaunchFlow.SetActive(SampleScenario.SkyrimId, true);
        var reads = StatusReads(rig);

        rig.Screen.OnWindowActivated();
        await SettledAsync(rig, clock, _settle * 2);

        Assert.Equal(reads, StatusReads(rig));
    }

    [Fact]
    public async Task Activating_the_window_after_leaving_the_screen_reads_nothing()
    {
        var (rig, clock) = await StartAsync();
        rig.Shell.Navigate(NavDestination.History);
        var reads = StatusReads(rig);

        rig.Screen.OnWindowActivated();
        await SettledAsync(rig, clock, _settle * 2);

        Assert.Equal(reads, StatusReads(rig));
    }

    [Fact]
    public async Task Disposing_the_screen_stops_the_watch_and_drops_a_read_that_was_waiting()
    {
        var (rig, clock) = await StartAsync();
        var watch = Assert.Single(rig.Watcher.Running);
        watch.RaiseChanged();
        var reads = StatusReads(rig);

        rig.Screen.Dispose();

        Assert.True(watch.Disposed);
        Assert.Equal(0, clock.ActiveTimers);
        await SettledAsync(rig, clock, _settle * 2);
        Assert.Equal(reads, StatusReads(rig));
    }
}
