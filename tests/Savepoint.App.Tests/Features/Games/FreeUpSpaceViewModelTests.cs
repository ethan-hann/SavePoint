using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Games;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Common;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using static Savepoint.App.Tests.Fakes.SampleScenario;

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
        Assert.Equal([ErrorOperation.FreeUpSpace], rig.Operations);
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

    // ---- Asking first

    // Freeing up space deletes the game's recently removed snapshots for good, so when there are some the footer says how many and asks first.
    [Fact]
    public async Task With_removed_snapshots_it_asks_first_naming_how_many_it_deletes_and_starts_after_a_yes()
    {
        var rig = new Rig(removed: true);
        rig.Dialogs.AnswerConfirm(true);

        await rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        var question = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal("Free up space?", question.Title);
        Assert.Equal("This also deletes 2 recently removed snapshots for good. You can't bring them back.", question.Message);
        Assert.Equal("Delete 2 snapshots and free up space", question.ConfirmLabel);
        Assert.True(question.Destructive);
        Assert.Equal([SkyrimId], rig.Backend.Jobs.List().Where(job => job.Kind == JobKind.Compact).Select(job => job.GameId));
        Assert.True(rig.FreeUp.IsRunning);
    }

    [Fact]
    public async Task The_run_it_starts_carries_the_records_the_player_was_asked_about()
    {
        var rig = new Rig(removed: true);
        rig.Dialogs.AnswerConfirm(true);
        var asked = FakeAssert.Ok(await rig.Backend.Games.GetFreeUpSpacePreviewAsync(SkyrimId, TestContext.Current.CancellationToken)).Token;

        await rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        var run = Assert.Single(rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.OptimizeStorageAsync)));
        Assert.Equal(asked.Records, Assert.IsType<FreeUpSpaceToken>(run.Args[1]).Records);
        Assert.Equal(2, asked.Records.Count);
    }

    [Fact]
    public async Task Declining_starts_nothing_and_leaves_the_button_ready()
    {
        var rig = new Rig(removed: true);
        rig.Dialogs.AnswerConfirm(false);

        await rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        Assert.Single(rig.Dialogs.Confirmations);
        Assert.Empty(rig.Backend.Jobs.List());
        Assert.False(rig.FreeUp.IsRunning);
        Assert.False(rig.FreeUp.HasStatus);
        Assert.True(rig.FreeUp.FreeUpCommand.CanExecute(null));
        Assert.Equal(2, FakeAssert.Ok(await rig.Backend.Games.GetFreeUpSpacePreviewAsync(SkyrimId, TestContext.Current.CancellationToken)).RecentlyRemovedSnapshots);
    }

    [Fact]
    public async Task With_nothing_removed_it_asks_nothing_and_starts_at_once()
    {
        var rig = new Rig();

        await rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        Assert.Empty(rig.Dialogs.Confirmations);
        Assert.True(rig.FreeUp.IsRunning);
    }

    // ---- Saying how much space comes back

    [Fact]
    public async Task The_question_says_about_how_much_space_comes_back_once_the_backend_has_worked_it_out()
    {
        var rig = new Rig(removed: true);
        rig.Backend.State.FreeUpEstimate = 1_200_000_000;
        rig.Dialogs.AnswerConfirm(true);

        await rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        var question = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal("This also deletes 2 recently removed snapshots for good. Deleting them frees up about 1.2 GB. You can't bring them back.", question.Message);
        Assert.Equal("Delete 2 snapshots and free up space", question.ConfirmLabel);
    }

    [Fact]
    public async Task A_size_that_cannot_be_worked_out_leaves_the_question_without_one_and_shows_no_error()
    {
        var rig = new Rig(removed: true);
        rig.Backend.FailNext<IGamesApi>(nameof(IGamesApi.EstimateFreeUpSpaceAsync), FileInUseError);
        rig.Dialogs.AnswerConfirm(true);

        await rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        Assert.Equal("This also deletes 2 recently removed snapshots for good. You can't bring them back.", Assert.Single(rig.Dialogs.Confirmations).Message);
        Assert.Empty(rig.Errors);
        Assert.True(rig.FreeUp.IsRunning);
    }

    [Fact]
    public async Task A_size_that_is_slow_does_not_hold_the_question_back()
    {
        var rig = new Rig(removed: true);
        rig.Backend.State.FreeUpEstimate = 1_200_000_000;
        rig.Backend.Games.EstimateHold = new TaskCompletionSource();
        rig.Dialogs.AnswerConfirm(true);

        await rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        Assert.Equal("This also deletes 2 recently removed snapshots for good. You can't bring them back.", Assert.Single(rig.Dialogs.Confirmations).Message);
        Assert.True(rig.FreeUp.IsRunning);
    }

    // A store that takes longer than the question will wait has the answer ready by the time the player presses the button.
    [Fact]
    public async Task The_size_is_worked_out_as_soon_as_a_game_with_removed_snapshots_is_picked()
    {
        var rig = new Rig(selected: false, removed: true);

        rig.FreeUp.SetGame(SkyrimId, _skyrim);

        Assert.True(await UiWait.UntilAsync(() => rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.EstimateFreeUpSpaceAsync)).Count == 1));
    }

    [Fact]
    public async Task Picking_another_game_stops_the_size_worked_out_for_the_one_before()
    {
        var rig = new Rig(selected: false, removed: true);
        rig.Backend.Games.EstimateHold = new TaskCompletionSource();
        rig.FreeUp.SetGame(SkyrimId, _skyrim);
        Assert.True(await UiWait.UntilAsync(() => rig.Backend.Games.EstimateTokens.Count == 1));

        rig.FreeUp.SetGame(SatisfactoryId, "Satisfactory");

        Assert.True(rig.Backend.Games.EstimateTokens[0].IsCancellationRequested);
    }

    // The estimate reads the store the run is about to repack and prune, so it must be over before the run starts.
    [Fact]
    public async Task A_yes_stops_the_size_that_is_still_being_worked_out_before_the_run_starts()
    {
        var rig = new Rig(removed: true);
        rig.Backend.Games.EstimateHold = new TaskCompletionSource();
        rig.Dialogs.AnswerConfirm(true);

        await rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        Assert.Equal("This also deletes 2 recently removed snapshots for good. You can't bring them back.", Assert.Single(rig.Dialogs.Confirmations).Message);
        Assert.True(rig.FreeUp.IsRunning);
        Assert.NotEmpty(rig.Backend.Games.EstimateTokens);
        Assert.All(rig.Backend.Games.EstimateTokens, token => Assert.True(token.IsCancellationRequested));
    }

    // ---- Leaving the game while the question is being prepared

    // The question names a count but not the game, and a yes deletes that game's recently removed snapshots for good.
    [Fact]
    public async Task Picking_another_game_while_the_count_is_read_asks_nothing_and_starts_nothing()
    {
        var rig = new Rig(removed: true);
        var hold = rig.Backend.Games.PreviewHold = new TaskCompletionSource();
        rig.Dialogs.AnswerConfirm(true);
        var pressed = rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        rig.FreeUp.SetGame(SatisfactoryId, "Satisfactory");
        hold.SetResult();
        await pressed;

        Assert.Empty(rig.Dialogs.Confirmations);
        Assert.Empty(rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.OptimizeStorageAsync)));
        Assert.Empty(rig.Backend.Jobs.List());
    }

    [Fact]
    public async Task Picking_another_game_while_the_size_is_worked_out_asks_nothing_and_starts_nothing()
    {
        var rig = new Rig(removed: true);
        rig.Backend.Games.EstimateHold = new TaskCompletionSource();
        rig.Dialogs.AnswerConfirm(true);
        var pressed = rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        rig.FreeUp.SetGame(SatisfactoryId, "Satisfactory");
        await pressed;

        Assert.Empty(rig.Dialogs.Confirmations);
        Assert.Empty(rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.OptimizeStorageAsync)));
        Assert.Empty(rig.Backend.Jobs.List());
    }

    [Fact]
    public async Task Picking_another_game_while_the_question_is_open_starts_nothing_even_after_a_yes()
    {
        var rig = new Rig(removed: true);
        rig.Dialogs.WhileConfirming = _ =>
        {
            rig.FreeUp.SetGame(SatisfactoryId, "Satisfactory");
            return Task.CompletedTask;
        };
        rig.Dialogs.AnswerConfirm(true);

        await rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        Assert.Empty(rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.OptimizeStorageAsync)));
        Assert.Empty(rig.Backend.Jobs.List());
    }

    [Fact]
    public async Task No_size_is_asked_for_when_nothing_is_removed()
    {
        var rig = new Rig();
        rig.Backend.State.FreeUpEstimate = 1_200_000_000;

        await rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        Assert.Empty(rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.EstimateFreeUpSpaceAsync)));
        Assert.Empty(rig.Dialogs.Confirmations);
    }

    [Fact]
    public async Task A_count_that_cannot_be_read_is_shown_as_an_error_and_starts_nothing()
    {
        var rig = new Rig(removed: true);
        rig.Backend.FailNext<IGamesApi>(nameof(IGamesApi.GetFreeUpSpacePreviewAsync), FileInUseError);

        await rig.FreeUp.FreeUpCommand.ExecuteAsync(null);

        Assert.Equal([FileInUseError], rig.Errors);
        Assert.Equal([ErrorOperation.FreeUpSpace], rig.Operations);
        Assert.Empty(rig.Dialogs.Confirmations);
        Assert.Empty(rig.Backend.Jobs.List());
        Assert.False(rig.FreeUp.IsRunning);
        Assert.True(rig.FreeUp.FreeUpCommand.CanExecute(null));
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
        Assert.Equal($"Couldn't free up space. {rig.Text.Message(FileInUseError, ErrorOperation.FreeUpSpace)}", rig.FreeUp.StatusText);
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
    public async Task A_run_that_a_launch_stopped_says_it_stopped_so_another_task_could_go_first()
    {
        var rig = new Rig();
        var job = await rig.StartAsync();

        rig.Backend.Script(() => rig.Backend.Jobs.FinishWith(job, JobStatus.Cancelled, null, ErrorInfo.From(new CompactionPreemptedException(SkyrimId))));

        Assert.False(rig.FreeUp.IsRunning);
        Assert.Equal("Stopped so another task could go first.", rig.FreeUp.StatusText);
        Assert.Equal("Stopped so another task could go first.", rig.FreeUp.Announcement);
    }

    [Fact]
    public async Task A_run_the_player_canceled_still_says_canceled_even_with_an_error_attached()
    {
        var rig = new Rig();
        var job = await rig.StartAsync();

        rig.Backend.Script(() => rig.Backend.Jobs.FinishWith(job, JobStatus.Cancelled, null, FileInUseError));

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
        // With removed, two snapshots of Skyrim and one of Satisfactory are waiting to be deleted for good.
        public Rig(bool selected = true, bool removed = false)
        {
            Backend = removed ? new FakeBackend(RemovedSnapshotSamples.Scenario()) : FakeBackend.Sample();
            Text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), Backend.Clock, "windows", TimeZoneInfo.Utc);
            FreeUp = new FreeUpSpaceViewModel(
                Backend.Games,
                Backend.Jobs,
                Dialogs,
                Text,
                new ImmediateUiDispatcher(),
                (error, operation) =>
                {
                    Errors.Add(error);
                    Operations.Add(operation);
                },
                () =>
                {
                    AfterRuns++;
                    return Task.CompletedTask;
                },
                Backend.Clock);
            if (selected)
            {
                FreeUp.SetGame(SkyrimId, _skyrim);
            }
        }

        public FakeBackend Backend { get; }

        public FakeDialogs Dialogs { get; } = new();

        public UiFormatter Text { get; }

        public FreeUpSpaceViewModel FreeUp { get; }

        public List<ErrorInfo> Errors { get; } = [];

        public List<ErrorOperation> Operations { get; } = [];

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
