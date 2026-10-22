using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Games;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using static Savepoint.App.Tests.Fakes.MockupScenario;

namespace Savepoint.App.Tests.Features.Games;

public class FreeUpSpaceViewModelTests
{
    private const string _skyrim = "Skyrim Special Edition";

    private static OptimizeResult Freed(long? bytes) =>
        new([new GameOptimizeOutcome(SkyrimId, [new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.External)], bytes)]);

    // ---- Starting

    [Fact]
    public async Task Pressing_free_up_space_starts_the_games_compaction_and_says_it_is_running()
    {
        var rig = new Rig();

        await rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        Assert.Equal([SkyrimId], rig.Backend.Jobs.List().Where(job => job.Kind == JobKind.Compact).Select(job => job.GameId));
        Assert.True(rig.FreeUp.IsRunning);
        Assert.Equal("Freeing up space…", rig.FreeUp.StatusText);
        Assert.True(rig.FreeUp.HasStatus);
        Assert.False(rig.FreeUp.FreeUpCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_compaction_that_cannot_start_is_shown_as_an_error_and_nothing_is_left_running()
    {
        var rig = new Rig();
        rig.Backend.FailNext<IGamesApi>(nameof(IGamesApi.OptimizeStorageAsync), FileInUseError);

        await rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        Assert.Equal([FileInUseError], rig.Errors);
        Assert.False(rig.FreeUp.IsRunning);
        Assert.False(rig.FreeUp.HasStatus);
        Assert.True(rig.FreeUp.FreeUpCommand.CanExecute(null));
    }

    [Fact]
    public async Task Without_a_game_there_is_nothing_to_free_up()
    {
        var rig = new Rig(selected: false);

        Assert.False(rig.FreeUp.FreeUpCommand.CanExecute(null));
        await rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        Assert.Empty(rig.Backend.Jobs.List());
    }

    [Fact]
    public void The_button_is_named_for_the_game()
    {
        var rig = new Rig();

        Assert.Equal("Free up space for Skyrim Special Edition", rig.FreeUp.Name);
    }

    // ---- How it ends

    [Fact]
    public async Task A_run_that_frees_space_says_how_much_and_the_button_comes_back()
    {
        var rig = new Rig();
        var job = await rig.StartAsync();

        rig.Backend.Jobs.Complete(job, Freed(3_400_000_000));

        Assert.False(rig.FreeUp.IsRunning);
        Assert.Equal("Freed up 3.4 GB.", rig.FreeUp.StatusText);
        Assert.True(rig.FreeUp.FreeUpCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_run_that_frees_nothing_says_so()
    {
        var rig = new Rig();
        var job = await rig.StartAsync();

        rig.Backend.Jobs.Complete(job, Freed(null));

        Assert.Equal("Done. There was nothing to clear out.", rig.FreeUp.StatusText);
    }

    [Fact]
    public async Task A_run_that_did_only_part_of_the_job_says_where_to_look()
    {
        var rig = new Rig();
        var job = await rig.StartAsync();
        var partial = new OptimizeResult([new GameOptimizeOutcome(SkyrimId, [new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.External, FileInUseError)], 1_000_000)]);

        rig.Backend.Jobs.Complete(job, partial);

        Assert.Equal("Freed up only part of the space. Activity shows what went wrong.", rig.FreeUp.StatusText);
    }

    [Fact]
    public async Task A_run_that_fails_says_why()
    {
        var rig = new Rig();
        var job = await rig.StartAsync();

        rig.Backend.Jobs.Fail(job, FileInUseError);

        Assert.False(rig.FreeUp.IsRunning);
        Assert.Equal($"Couldn't free up space. {rig.Text.Message(FileInUseError)}", rig.FreeUp.StatusText);
    }

    [Fact]
    public async Task A_run_that_is_canceled_says_so()
    {
        var rig = new Rig();
        var job = await rig.StartAsync();

        rig.Backend.Jobs.CompleteCancelled(job);

        Assert.False(rig.FreeUp.IsRunning);
        Assert.Equal("Canceled.", rig.FreeUp.StatusText);
    }

    [Fact]
    public async Task When_a_run_ends_the_footer_is_told_to_read_the_storage_use_again()
    {
        var rig = new Rig();
        var job = await rig.StartAsync();
        Assert.Equal(0, rig.AfterRuns);

        rig.Backend.Jobs.Complete(job, Freed(1_000_000));

        Assert.Equal(1, rig.AfterRuns);
    }

    [Fact]
    public async Task The_status_is_spoken_when_the_run_starts_and_when_it_ends()
    {
        var rig = new Rig();
        var job = await rig.StartAsync();
        Assert.Equal("Freeing up space…", rig.FreeUp.Announcement);

        rig.Backend.Jobs.Complete(job, Freed(3_400_000_000));

        Assert.Equal("Freed up 3.4 GB.", rig.FreeUp.Announcement);
    }

    [Fact]
    public async Task Clearing_the_result_empties_the_status_of_a_finished_run_but_not_a_running_one()
    {
        var rig = new Rig();
        var job = await rig.StartAsync();

        rig.FreeUp.ClearResult();
        Assert.Equal("Freeing up space…", rig.FreeUp.StatusText);

        rig.Backend.Jobs.Complete(job, Freed(3_400_000_000));
        rig.FreeUp.ClearResult();

        Assert.False(rig.FreeUp.HasStatus);
    }

    // ---- Following jobs started elsewhere

    [Fact]
    public async Task A_compaction_of_the_game_that_is_already_running_shows_when_the_game_is_picked()
    {
        var rig = new Rig(selected: false);
        FakeAssert.Ok(await rig.Backend.Games.OptimizeStorageAsync(SkyrimId));

        rig.FreeUp.SetGame(SkyrimId, _skyrim);

        Assert.True(rig.FreeUp.IsRunning);
        Assert.Equal("Freeing up space…", rig.FreeUp.StatusText);
        Assert.False(rig.FreeUp.FreeUpCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_compaction_of_every_game_counts_for_this_one_and_ends_the_same_way()
    {
        var rig = new Rig();
        var all = FakeAssert.Ok(await rig.Backend.Settings.OptimizeAllStorageAsync()).JobId;

        Assert.True(rig.FreeUp.IsRunning);

        rig.Backend.Jobs.Complete(all, new OptimizeResult([new GameOptimizeOutcome(SkyrimId, [new FolderOutcome(RepoRole.Install)], 2_000_000_000)]));

        Assert.False(rig.FreeUp.IsRunning);
        Assert.Equal("Freed up 2 GB.", rig.FreeUp.StatusText);
    }

    [Fact]
    public async Task Another_games_compaction_is_none_of_this_games_business()
    {
        var rig = new Rig();

        FakeAssert.Ok(await rig.Backend.Games.OptimizeStorageAsync(SatisfactoryId));

        Assert.False(rig.FreeUp.IsRunning);
        Assert.False(rig.FreeUp.HasStatus);
    }

    [Fact]
    public async Task A_compaction_that_ended_before_the_game_was_picked_is_history_and_says_nothing()
    {
        var rig = new Rig(selected: false);
        var job = FakeAssert.Ok(await rig.Backend.Games.OptimizeStorageAsync(SkyrimId)).JobId;
        rig.Backend.Jobs.Complete(job, Freed(3_400_000_000));

        rig.FreeUp.SetGame(SkyrimId, _skyrim);

        Assert.False(rig.FreeUp.IsRunning);
        Assert.False(rig.FreeUp.HasStatus);
    }

    // ---- Switching games

    [Fact]
    public async Task Picking_another_game_clears_the_status_and_ignores_the_first_games_run()
    {
        var rig = new Rig();
        var job = await rig.StartAsync();

        rig.FreeUp.SetGame(SatisfactoryId, "Satisfactory");

        Assert.False(rig.FreeUp.IsRunning);
        Assert.False(rig.FreeUp.HasStatus);
        Assert.Equal("Free up space for Satisfactory", rig.FreeUp.Name);

        rig.Backend.Jobs.Complete(job, Freed(3_400_000_000));

        Assert.False(rig.FreeUp.HasStatus);
        Assert.Equal(0, rig.AfterRuns);
    }

    [Fact]
    public async Task Picking_the_same_game_again_keeps_the_status()
    {
        var rig = new Rig();
        await rig.StartAsync();

        rig.FreeUp.SetGame(SkyrimId, _skyrim);

        Assert.True(rig.FreeUp.IsRunning);
        Assert.Equal("Freeing up space…", rig.FreeUp.StatusText);
    }

    // ---- Done

    [Fact]
    public async Task A_disposed_footer_stops_following_jobs()
    {
        var rig = new Rig();
        var job = await rig.StartAsync();

        rig.FreeUp.Dispose();
        rig.Backend.Jobs.Complete(job, Freed(3_400_000_000));

        Assert.True(rig.FreeUp.IsRunning);
        Assert.Equal(0, rig.AfterRuns);
    }

    private sealed class Rig
    {
        public Rig(bool selected = true)
        {
            Backend = FakeBackend.Mockup();
            Text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), Backend.Clock, "windows", TimeZoneInfo.Utc);
            FreeUp = new FreeUpSpaceViewModel(
                Backend.Games,
                Backend.Jobs,
                Text,
                new ImmediateUiDispatcher(),
                Errors.Add,
                () =>
                {
                    AfterRuns++;
                    return Task.CompletedTask;
                });
            if (selected)
            {
                FreeUp.SetGame(SkyrimId, _skyrim);
            }
        }

        public FakeBackend Backend { get; }

        public UiFormatter Text { get; }

        public FreeUpSpaceViewModel FreeUp { get; }

        public List<ErrorInfo> Errors { get; } = [];

        public int AfterRuns { get; private set; }

        public async Task<Guid> StartAsync()
        {
            await FreeUp.FreeUpCommand.ExecuteAsync(null);
            var started = Backend.Jobs.List().LastOrDefault(job => job.Kind == JobKind.Compact);
            Assert.NotNull(started);
            return started.JobId;
        }
    }
}
