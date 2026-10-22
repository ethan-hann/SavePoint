using Savepoint.App.Features.Activity;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using static Savepoint.App.Tests.Fakes.MockupScenario;

namespace Savepoint.App.Tests.Features.Activity;

public class ActivityScreenViewModelTests
{
    private const string _skyrim = "Skyrim Special Edition";

    private static JobDetailsViewModel Details(ActivityRig rig) => Assert.IsType<JobDetailsViewModel>(rig.Screen.Details);

    private static JobRowViewModel FirstRow(ActivityRig rig) => Assert.Single(rig.Screen.Rows.Take(1));

    private static JobRowViewModel RowWhere(ActivityRig rig, Predicate<JobRowViewModel> match) => Assert.Single(rig.Screen.Rows, match);

    private static ScopeOption Scope(ActivityRig rig, int index) => Assert.IsType<ScopeOption>(rig.Screen.ScopeOptions.ElementAtOrDefault(index));

    private static JobLineViewModel Line(JobDetailsViewModel details, int index) => Assert.IsType<JobLineViewModel>(details.Lines.ElementAtOrDefault(index));

    // ---- What is listed

    [Fact]
    public async Task The_screen_opens_on_the_selected_games_jobs_newest_first()
    {
        var rig = await ActivityRig.ShownAsync();
        await rig.StartCaptureAsync();
        rig.Pass(TimeSpan.FromMinutes(1));
        await rig.StartFreeUpAsync();
        rig.Pass(TimeSpan.FromMinutes(1));
        await rig.StartFreeUpAsync(CaretakerId);

        Assert.Equal([$"Freeing up space for {_skyrim}", $"Taking a snapshot of {_skyrim}"], rig.RowTitles());
        Assert.Equal("Activity", rig.Screen.Title);
        Assert.Equal($"{_skyrim} · 2 jobs", rig.Screen.Subtitle);
        Assert.False(rig.Screen.IsAllGames);
    }

    [Fact]
    public async Task All_games_lists_every_games_jobs_and_the_jobs_that_span_every_game()
    {
        var rig = await ActivityRig.ShownAsync();
        await rig.StartCaptureAsync();
        rig.Pass(TimeSpan.FromMinutes(1));
        await rig.StartFreeUpAsync(CaretakerId);
        rig.Pass(TimeSpan.FromMinutes(1));
        await rig.StartMoveAsync();
        rig.Pass(TimeSpan.FromMinutes(1));
        await rig.StartClearAsync();

        rig.Screen.SelectedScope = Scope(rig, 1);

        Assert.Equal(
            ["Clearing SavePoint's data", "Moving the snapshot folder", "Freeing up space for The Last Caretaker", $"Taking a snapshot of {_skyrim}"],
            rig.RowTitles());
        Assert.Equal("All games · 4 jobs", rig.Screen.Subtitle);
        Assert.True(rig.Screen.IsAllGames);
    }

    [Fact]
    public async Task The_scope_choice_is_this_game_or_all_games_with_a_name_for_a_screen_reader()
    {
        var rig = await ActivityRig.ShownAsync();

        Assert.Equal(["This game", "All games"], rig.Screen.ScopeOptions.Select(option => option.Label));
        Assert.Equal([ActivityScope.ThisGame, ActivityScope.AllGames], rig.Screen.ScopeOptions.Select(option => option.Scope));
        Assert.Equal("Show jobs for", rig.Screen.ScopeLabel);
        Assert.True(rig.Screen.ShowScope);
        Assert.Same(Scope(rig, 0), rig.Screen.SelectedScope);
    }

    [Fact]
    public async Task Selecting_another_game_in_the_rail_lists_that_games_jobs_even_from_all_games()
    {
        var rig = await ActivityRig.ShownAsync();
        await rig.StartFreeUpAsync(CaretakerId);
        rig.Screen.SelectedScope = Scope(rig, 1);

        rig.Shell.SelectGame(CaretakerId);

        Assert.False(rig.Screen.IsAllGames);
        Assert.Equal(["Freeing up space for The Last Caretaker"], rig.RowTitles());
    }

    [Fact]
    public async Task Showing_activity_through_the_shell_picks_the_scope_it_asks_for()
    {
        var rig = await ActivityRig.StartAsync();
        await rig.StartFreeUpAsync(CaretakerId);

        rig.Shell.ShowActivity(null);

        Assert.True(rig.Screen.IsAllGames);
        Assert.Equal(["Freeing up space for The Last Caretaker"], rig.RowTitles());

        rig.Shell.ShowActivity(CaretakerId);

        Assert.False(rig.Screen.IsAllGames);
        Assert.Equal(["Freeing up space for The Last Caretaker"], rig.RowTitles());
        Assert.Equal("The Last Caretaker · 1 job", rig.Screen.Subtitle);
    }

    [Fact]
    public async Task With_no_game_in_the_library_the_screen_lists_every_job_and_offers_no_scope_choice()
    {
        var rig = await ActivityRig.ShownAsync(FakeBackend.Empty());
        await rig.StartClearAsync();

        Assert.False(rig.Screen.ShowScope);
        Assert.True(rig.Screen.IsAllGames);
        Assert.Equal(["Clearing SavePoint's data"], rig.RowTitles());
        Assert.Equal("All games · 1 job", rig.Screen.Subtitle);
    }

    [Fact]
    public async Task A_job_of_a_game_that_was_removed_lists_under_all_games_with_a_plain_name()
    {
        var rig = await ActivityRig.ShownAsync();
        rig.Backend.Script(() => rig.Backend.Jobs.Start(JobKind.Capture, "gone", null));

        rig.Screen.SelectedScope = Scope(rig, 1);

        Assert.Equal(["Taking a snapshot of a game that was removed"], rig.RowTitles());
    }

    [Fact]
    public async Task A_job_that_ran_before_the_screen_was_built_is_listed()
    {
        var backend = FakeBackend.Mockup();
        var early = FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(SkyrimId)).JobId;
        backend.Jobs.Complete(early);

        var rig = await ActivityRig.ShownAsync(backend);

        Assert.Equal([$"Freeing up space for {_skyrim}"], rig.RowTitles());
        Assert.Equal("Done", FirstRow(rig).StatusText);
    }

    // ---- Following the jobs

    [Fact]
    public async Task A_job_that_starts_appears_at_once_and_its_progress_moves_the_bar_in_place()
    {
        var rig = await ActivityRig.ShownAsync();

        var id = await rig.StartCaptureAsync();
        var row = Assert.Single(rig.Screen.Rows);
        Assert.True(row.IsIndeterminate);

        rig.Backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 30, FilesTotal: 120));

        Assert.Same(row, Assert.Single(rig.Screen.Rows));
        Assert.Equal("30 of 120 files", row.ProgressText);
        Assert.Equal(25, row.ProgressPercent, 1);
    }

    [Fact]
    public async Task A_job_that_finishes_changes_state_in_its_row_without_the_row_being_replaced()
    {
        var rig = await ActivityRig.ShownAsync();
        var id = await rig.StartCaptureAsync();
        var row = FirstRow(rig);
        rig.Screen.SelectedRow = row;

        rig.Backend.Jobs.Complete(id);

        Assert.Same(row, FirstRow(rig));
        Assert.Same(row, rig.Screen.SelectedRow);
        Assert.Equal("Done", row.StatusText);
        Assert.False(row.IsRunning);
    }

    [Fact]
    public async Task Job_changes_are_taken_on_the_ui_thread_through_the_dispatcher()
    {
        var dispatcher = new QueueingDispatcher();
        var rig = await ActivityRig.ShownAsync(dispatcher: dispatcher);
        dispatcher.RunAll();

        await rig.StartCaptureAsync();

        Assert.Empty(rig.Screen.Rows);
        Assert.True(dispatcher.Pending > 0);

        dispatcher.RunAll();

        Assert.Single(rig.Screen.Rows);
    }

    [Fact]
    public async Task Disposing_the_screen_stops_it_following_the_jobs()
    {
        var rig = await ActivityRig.ShownAsync();
        rig.Screen.Dispose();

        await rig.StartCaptureAsync();

        Assert.Empty(rig.Screen.Rows);
    }

    // ---- Choosing a job

    [Fact]
    public async Task The_newest_running_job_is_chosen_first_and_otherwise_the_newest()
    {
        var backend = FakeBackend.Mockup();
        var running = FakeAssert.Ok(await backend.Profiles.CaptureAsync(SkyrimId, ModdedId, "Still running")).JobId;
        backend.Clock.Advance(TimeSpan.FromMinutes(1));
        var done = FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(SkyrimId)).JobId;
        backend.Jobs.Complete(done);

        var rig = await ActivityRig.ShownAsync(backend);

        Assert.Equal(running, rig.Screen.SelectedRow?.JobId);

        var finished = FakeBackend.Mockup();
        FakeAssert.Ok(await finished.Games.OptimizeStorageAsync(SkyrimId));
        finished.Jobs.Complete(finished.Jobs.LastStarted().JobId);
        finished.Clock.Advance(TimeSpan.FromMinutes(1));
        var newest = FakeAssert.Ok(await finished.Profiles.CaptureAsync(SkyrimId, ModdedId, "Newest")).JobId;
        finished.Jobs.Complete(newest);

        var other = await ActivityRig.ShownAsync(finished);

        Assert.Equal(newest, other.Screen.SelectedRow?.JobId);
    }

    [Fact]
    public async Task The_readers_choice_stays_while_other_jobs_start_and_change()
    {
        var rig = await ActivityRig.ShownAsync();
        var first = await rig.StartCaptureAsync();
        rig.Screen.SelectedRow = FirstRow(rig);
        rig.Pass(TimeSpan.FromMinutes(1));

        await rig.StartFreeUpAsync();
        rig.Backend.Jobs.ReportProgress(first, new JobProgress(FilesDone: 1, FilesTotal: 2));

        Assert.Equal(first, rig.Screen.SelectedRow?.JobId);
        Assert.Equal(first, rig.Screen.Details?.Row.JobId);
    }

    [Fact]
    public async Task When_the_chosen_job_leaves_the_list_another_is_chosen_and_when_none_is_left_there_are_no_details()
    {
        var rig = await ActivityRig.ShownAsync();
        await rig.StartFreeUpAsync(CaretakerId);
        rig.Screen.SelectedScope = Scope(rig, 1);
        var caretaker = rig.Screen.SelectedRow;
        await rig.StartCaptureAsync();
        rig.Screen.SelectedRow = RowWhere(rig, row => row.Title.StartsWith("Freeing", StringComparison.Ordinal));
        Assert.Same(caretaker, rig.Screen.SelectedRow);

        rig.Screen.SelectedScope = Scope(rig, 0);

        Assert.Equal($"Taking a snapshot of {_skyrim}", rig.Screen.SelectedRow?.Title);
        Assert.NotNull(rig.Screen.Details);

        rig.Screen.SelectedRow = null;

        Assert.Null(rig.Screen.Details);
        Assert.False(rig.Screen.ShowDetails);
    }

    // ---- The details' lines

    [Fact]
    public async Task The_details_show_the_jobs_lines_in_words()
    {
        var rig = await ActivityRig.ShownAsync();
        var id = await rig.StartCaptureAsync();
        rig.Backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 1, FilesTotal: 4, CurrentPath: @"Data\Textures\a.dds"));

        var lines = Details(rig).Lines;

        Assert.Equal(
            ["Taking the snapshot \"After the Blackreach run\" of the profile \"Modded playthrough - 7-2-26\".", @"Data\Textures\a.dds"],
            lines.Select(line => line.Text));
        Assert.Equal([true, false], lines.Select(line => line.IsStep));
        Assert.True(Details(rig).HasLines);
    }

    [Fact]
    public async Task New_lines_are_added_at_the_end_and_the_lines_already_held_stay_the_same_objects()
    {
        var rig = await ActivityRig.ShownAsync();
        var id = await rig.StartCaptureAsync();
        var held = Line(Details(rig), 0);

        rig.Backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 1, CurrentPath: "a.esp"));
        rig.Backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 2, CurrentPath: "b.esp"));
        rig.Backend.Jobs.ReportStep(id, JobSteps.CaptureFolderDone, ("folder", RepoRole.Install), ("files", 2));

        Assert.Same(held, Line(Details(rig), 0));
        Assert.Equal(["a.esp", "b.esp", "Added 2 new or changed files from the game folder."], Details(rig).Lines.Skip(1).Select(line => line.Text));
        Assert.Equal([0L, 1, 2, 3], Details(rig).Lines.Select(line => line.Number));
    }

    [Fact]
    public async Task Lines_that_fall_off_the_front_of_the_job_leave_the_list_and_the_rest_stay_put()
    {
        var rig = await ActivityRig.ShownAsync();
        var id = await rig.StartCaptureAsync();
        for (var i = 1; i <= 199; i++)
        {
            rig.Backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: i, CurrentPath: $"file{i}.esp"));
        }

        var heldLast = Details(rig).Lines.LastOrDefault();
        Assert.Equal(200, Details(rig).Lines.Count);

        rig.Backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 200, CurrentPath: "file200.esp"));
        rig.Backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 201, CurrentPath: "file201.esp"));

        var lines = Details(rig).Lines;
        Assert.Equal(200, lines.Count);
        Assert.Equal("file2.esp", lines[0].Text);
        Assert.Equal("file201.esp", lines[^1].Text);
        Assert.Equal(2L, lines[0].Number);
        Assert.Contains(heldLast, lines);
    }

    [Fact]
    public async Task Another_job_shows_its_own_lines()
    {
        var rig = await ActivityRig.ShownAsync();
        await rig.StartCaptureAsync();
        rig.Pass(TimeSpan.FromMinutes(1));
        await rig.StartFreeUpAsync();

        rig.Screen.SelectedRow = RowWhere(rig, row => row.Title.StartsWith("Taking", StringComparison.Ordinal));

        Assert.StartsWith("Taking the snapshot", Line(Details(rig), 0).Text, StringComparison.Ordinal);

        rig.Screen.SelectedRow = RowWhere(rig, row => row.Title.StartsWith("Freeing", StringComparison.Ordinal));

        Assert.Equal(["Freeing up space for Skyrim Special Edition."], Details(rig).Lines.Select(line => line.Text));
    }

    // ---- Cancel

    [Fact]
    public async Task Cancel_is_offered_on_a_running_job_that_can_be_stopped()
    {
        var rig = await ActivityRig.ShownAsync();
        var id = await rig.StartCaptureAsync();

        Assert.True(Details(rig).CanCancel);
        Assert.False(Details(rig).HasStopNote);

        rig.Backend.Jobs.Complete(id);

        Assert.False(Details(rig).CanCancel);
    }

    [Fact]
    public async Task Pressing_cancel_asks_the_job_to_stop_and_the_row_says_it_is_stopping()
    {
        var rig = await ActivityRig.ShownAsync();
        var id = await rig.StartCaptureAsync();

        await Details(rig).CancelCommand.ExecuteAsync(null);

        Assert.True(rig.Backend.Jobs.Get(id)!.CancelRequested);
        Assert.Equal("Stopping…", FirstRow(rig).StatusText);
        Assert.False(Details(rig).CanCancel);
        Assert.True(Details(rig).HasStopNote);
        Assert.Equal("Stopping. SavePoint is finishing the step it is on, then it stops.", Details(rig).StopNote);

        rig.Backend.Jobs.CompleteCancelled(id);

        Assert.Equal("Canceled", FirstRow(rig).StatusText);
        Assert.False(Details(rig).HasStopNote);
    }

    [Fact]
    public async Task A_cancel_the_job_ignores_says_so_instead_of_leaving_the_row_looking_stuck()
    {
        var ignoring = (IgnoringCancelJobs?)null;
        var rig = await ActivityRig.ShownAsync(jobs: inner => ignoring = new IgnoringCancelJobs(inner));
        var id = await rig.StartCaptureAsync();

        await Details(rig).CancelCommand.ExecuteAsync(null);

        Assert.Equal([id], ignoring!.Asked);
        Assert.Equal("That job couldn't be stopped just now. It will finish on its own.", Details(rig).StopNote);
        Assert.True(Details(rig).HasStopNote);
        Assert.Equal("In progress", FirstRow(rig).StatusText);

        rig.Backend.Jobs.Complete(id);

        Assert.False(Details(rig).HasStopNote);
    }

    [Fact]
    public async Task A_job_that_cannot_be_stopped_offers_no_cancel_and_says_why()
    {
        var rig = await ActivityRig.ShownAsync(FakeBackend.Empty());
        var clear = await rig.StartClearAsync();

        Assert.False(Details(rig).CanCancel);
        Assert.Equal("Clearing SavePoint's data can't be stopped once it has started.", Details(rig).StopNote);

        rig.Backend.Jobs.Complete(clear);
        var move = await rig.StartMoveAsync();
        rig.Screen.SelectedRow = RowWhere(rig, row => row.JobId == move);
        Assert.True(Details(rig).CanCancel);

        rig.Backend.Jobs.ReportNoLongerCancellable(move);

        Assert.False(Details(rig).CanCancel);
        Assert.Equal("The move can't be stopped any more, because SavePoint already uses the new folder.", Details(rig).StopNote);
    }

    // ---- The error

    [Fact]
    public async Task A_failed_job_shows_its_error_in_the_shared_error_card()
    {
        var rig = await ActivityRig.ShownAsync();
        var id = await rig.StartCaptureAsync();

        rig.Backend.Jobs.Fail(id, FileInUseError);

        var card = Details(rig).Error;
        Assert.NotNull(card);
        Assert.True(Details(rig).HasError);
        Assert.Equal(new ErrorCardViewModel(FileInUseError, rig.Text).Title, card.Title);
        Assert.Equal(new ErrorCardViewModel(FileInUseError, rig.Text).Body, card.Body);
    }

    [Fact]
    public async Task A_partly_done_job_shows_the_error_of_the_first_folder_that_failed()
    {
        var rig = await ActivityRig.ShownAsync();
        var id = await rig.StartCaptureAsync();

        rig.Backend.Jobs.Complete(id, HalfSaved);

        Assert.Equal("Partly done", FirstRow(rig).StatusText);
        Assert.Equal(new ErrorCardViewModel(SavesOutOfRoomError, rig.Text).Body, Details(rig).Error?.Body);
    }

    [Fact]
    public async Task A_canceled_job_shows_what_the_cancel_did_and_a_job_that_worked_shows_no_error()
    {
        var rig = await ActivityRig.ShownAsync();
        var stopped = await rig.StartCaptureAsync();
        rig.Backend.Jobs.CompleteCancelled(stopped);

        Assert.True(Details(rig).HasError);

        rig.Pass(TimeSpan.FromMinutes(1));
        var worked = await rig.StartFreeUpAsync();
        rig.Backend.Jobs.Complete(worked);
        rig.Screen.SelectedRow = RowWhere(rig, row => row.JobId == worked);

        Assert.False(Details(rig).HasError);
        Assert.Null(Details(rig).Error);
    }

    // ---- Copy

    [Fact]
    public async Task Copy_puts_the_selected_lines_in_order_on_the_clipboard()
    {
        var rig = await ActivityRig.ShownAsync();
        var id = await rig.StartCaptureAsync();
        rig.Backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 1, CurrentPath: "a.esp"));
        rig.Backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 2, CurrentPath: "b.esp"));
        var details = Details(rig);
        var copied = new List<string>();
        details.CopyRequested += (_, text) => copied.Add(text);

        details.SetSelection([Line(details, 2), Line(details, 1)]);
        details.CopyCommand.Execute(null);

        Assert.Equal(["a.esp" + "\n" + "b.esp"], copied);
    }

    [Fact]
    public async Task Copy_with_nothing_selected_takes_every_line()
    {
        var rig = await ActivityRig.ShownAsync();
        var id = await rig.StartCaptureAsync();
        rig.Backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 1, CurrentPath: "a.esp"));
        var details = Details(rig);
        var copied = new List<string>();
        details.CopyRequested += (_, text) => copied.Add(text);

        details.CopyCommand.Execute(null);

        Assert.Equal(["Taking the snapshot \"After the Blackreach run\" of the profile \"Modded playthrough - 7-2-26\".\na.esp"], copied);
        Assert.Equal(copied[0], details.AllText);
    }

    [Fact]
    public async Task Copy_cannot_run_for_a_job_that_has_written_nothing()
    {
        var rig = await ActivityRig.ShownAsync();
        rig.Backend.Script(() => rig.Backend.Jobs.Start(JobKind.Capture, SkyrimId, null));

        Assert.False(Details(rig).HasLines);
        Assert.False(Details(rig).CopyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Copying_tells_a_screen_reader_how_many_lines_were_copied()
    {
        var rig = await ActivityRig.ShownAsync();
        var id = await rig.StartCaptureAsync();
        rig.Backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 1, CurrentPath: "a.esp"));
        var details = Details(rig);

        details.SetSelection([Line(details, 1)]);
        details.CopyCommand.Execute(null);

        Assert.Equal("Copied 1 line.", rig.Screen.Announcement);

        details.SetSelection([]);
        details.CopyCommand.Execute(null);

        Assert.Equal("Copied 2 lines.", rig.Screen.Announcement);
    }

    // ---- Empty states, the footer and announcements

    [Fact]
    public async Task With_no_jobs_at_all_the_screen_says_so()
    {
        var rig = await ActivityRig.ShownAsync();

        Assert.True(rig.Screen.IsEmpty);
        Assert.False(rig.Screen.IsFilteredEmpty);
        Assert.False(rig.Screen.HasRows);
        Assert.False(rig.Screen.ShowDetails);
        Assert.Equal($"{_skyrim} · 0 jobs", rig.Screen.Subtitle);
    }

    [Fact]
    public async Task A_game_with_no_jobs_while_others_have_some_offers_all_games()
    {
        var rig = await ActivityRig.ShownAsync();
        await rig.StartFreeUpAsync(CaretakerId);

        Assert.True(rig.Screen.IsEmpty);
        Assert.True(rig.Screen.IsFilteredEmpty);

        rig.Screen.ShowAllGamesCommand.Execute(null);

        Assert.True(rig.Screen.IsAllGames);
        Assert.False(rig.Screen.IsEmpty);
        Assert.True(rig.Screen.HasRows);
    }

    [Fact]
    public async Task The_footer_says_how_many_finished_jobs_are_kept()
    {
        var rig = await ActivityRig.ShownAsync();

        Assert.Equal("SavePoint remembers the last five finished jobs of each game until you close it.", rig.Screen.FooterText);
    }

    [Fact]
    public async Task A_job_starting_and_ending_is_announced_once_for_each_change_of_state_and_progress_is_not()
    {
        var rig = await ActivityRig.ShownAsync();
        var spoken = new List<string>();
        rig.Screen.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ActivityScreenViewModel.Announcement))
            {
                spoken.Add(rig.Screen.Announcement);
            }
        };

        var id = await rig.StartCaptureAsync();
        rig.Backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 1, FilesTotal: 10));
        rig.Backend.Jobs.ReportProgress(id, new JobProgress(FilesDone: 2, FilesTotal: 10));
        rig.Backend.Jobs.Complete(id);

        Assert.Equal([$"Taking a snapshot of {_skyrim}: In progress.", $"Taking a snapshot of {_skyrim}: Done."], spoken);
    }

    [Fact]
    public async Task A_stop_is_announced_too()
    {
        var rig = await ActivityRig.ShownAsync();
        await rig.StartCaptureAsync();

        await Details(rig).CancelCommand.ExecuteAsync(null);

        Assert.Equal($"Taking a snapshot of {_skyrim}: Stopping.", rig.Screen.Announcement);
    }
}
