using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Settings;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Settings;

public class StorageSectionViewModelTests
{
    private const string _newRoot = @"E:\NewData";

    private static StorageSectionViewModel Storage(SettingsRig rig) => rig.Screen.Storage;

    private static FakeBackend WithBlockedMove() => new(ScenarioBuilder.Mockup().WithRelocationPreview(new StorageRelocationPreview(
        MockupScenario.StorageRoot,
        _newRoot,
        [],
        0,
        0,
        100_000_000_000,
        ErrorInfo.From(new StorageDestinationNotEmptyException(_newRoot)))));

    private static async Task<JobSnapshot> StartedMoveAsync(SettingsRig rig)
    {
        rig.Dialogs.AnswerFolder(_newRoot).AnswerConfirm(true);
        await Storage(rig).ChangeCommand.ExecuteAsync(null);
        return rig.Backend.Jobs.LastStarted(JobKind.RelocateStorage);
    }

    // ---- Where SavePoint keeps its data

    [Fact]
    public async Task The_section_shows_where_the_data_is_and_how_much_space_it_takes()
    {
        var rig = await SettingsRig.ShownAsync();

        Assert.Equal(MockupScenario.StorageRoot, Storage(rig).StoragePath);
        Assert.True(Storage(rig).HasSummary);
        Assert.Equal("79.3 GB used by 3 games", Storage(rig).UsedText);
        Assert.Equal("131 GB free", Storage(rig).FreeText);
        Assert.Equal(79.3 / 210, Storage(rig).UsedFraction, 3);
    }

    [Fact]
    public async Task A_summary_that_cannot_be_read_hides_the_bar_and_nothing_else()
    {
        var rig = await SettingsRig.StartAsync();
        rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetStorageSummaryAsync), ErrorInfo.From(new InvalidOperationException("No size.")));

        rig.Show();

        Assert.False(Storage(rig).HasSummary);
        Assert.Equal(MockupScenario.StorageRoot, Storage(rig).StoragePath);
        Assert.True(rig.Screen.IsLoaded);
    }

    [Fact]
    public async Task The_optimize_row_says_when_it_last_ran()
    {
        var rig = await SettingsRig.ShownAsync();

        Assert.Equal("Squeezes down space from profiles you've deleted. Last run Jul 1.", Storage(rig).OptimizeDescription);
    }

    [Fact]
    public async Task The_optimize_row_says_so_when_it_has_never_run()
    {
        var never = MockupScenario.Settings with { LastOptimizedAt = null };
        var rig = await SettingsRig.ShownAsync(new FakeBackend(ScenarioBuilder.Mockup().WithSettings(never)));

        Assert.Equal("Squeezes down space from profiles you've deleted. It hasn't run yet.", Storage(rig).OptimizeDescription);
    }

    // ---- Optimize storage

    [Fact]
    public async Task Run_now_starts_the_optimization_for_every_game_and_shows_it_running()
    {
        var rig = await SettingsRig.ShownAsync();

        await Storage(rig).RunNowCommand.ExecuteAsync(null);

        Assert.Equal(1, rig.Calls<ISettingsApi>(nameof(ISettingsApi.OptimizeAllStorageAsync)));
        Assert.True(Storage(rig).IsOptimizing);
        Assert.False(Storage(rig).RunNowCommand.CanExecute(null));
    }

    [Fact]
    public async Task When_the_optimization_finishes_the_row_shows_the_new_date_and_the_summary_is_read_again()
    {
        var rig = await SettingsRig.ShownAsync();
        await Storage(rig).RunNowCommand.ExecuteAsync(null);
        var summaries = rig.Calls<ISettingsApi>(nameof(ISettingsApi.GetStorageSummaryAsync));

        rig.Backend.Jobs.Complete(rig.Backend.Jobs.LastStarted(JobKind.Compact).JobId);

        Assert.False(Storage(rig).IsOptimizing);
        Assert.True(Storage(rig).RunNowCommand.CanExecute(null));
        Assert.Equal("Squeezes down space from profiles you've deleted. Last run Jul 8.", Storage(rig).OptimizeDescription);
        Assert.True(rig.Calls<ISettingsApi>(nameof(ISettingsApi.GetStorageSummaryAsync)) > summaries);
    }

    [Fact]
    public async Task Cancel_stops_the_optimization_through_the_jobs_api()
    {
        var rig = await SettingsRig.ShownAsync();
        await Storage(rig).RunNowCommand.ExecuteAsync(null);
        var job = rig.Backend.Jobs.LastStarted(JobKind.Compact);

        await Storage(rig).CancelOptimizeCommand.ExecuteAsync(null);

        var cancel = Assert.Single(rig.Backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
        Assert.Equal([job.JobId], cancel.Args);
        rig.Backend.Jobs.CompleteCancelled(job.JobId);
        Assert.False(Storage(rig).IsOptimizing);
    }

    [Fact]
    public async Task An_optimization_already_running_before_the_screen_existed_is_picked_up_when_it_shows()
    {
        var backend = FakeBackend.Mockup();
        FakeAssert.Ok(await backend.Settings.OptimizeAllStorageAsync());
        var rig = await SettingsRig.StartAsync(backend);

        rig.Show();

        Assert.True(Storage(rig).IsOptimizing);
        Assert.True(Storage(rig).CancelOptimizeCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_finished_optimization_from_before_the_screen_existed_is_not_running()
    {
        var backend = FakeBackend.Mockup();
        var started = FakeAssert.Ok(await backend.Settings.OptimizeAllStorageAsync());
        backend.Jobs.Complete(started.JobId);
        var rig = await SettingsRig.StartAsync(backend);

        rig.Show();

        Assert.False(Storage(rig).IsOptimizing);
    }

    [Fact]
    public async Task An_optimization_that_runs_while_another_screen_shows_is_known_when_the_screen_comes_back()
    {
        var rig = await SettingsRig.ShownAsync();
        rig.Shell.Navigate(Savepoint.App.Features.Shell.NavDestination.Games);

        FakeAssert.Ok(await rig.Backend.Settings.OptimizeAllStorageAsync());
        rig.Show();

        Assert.True(Storage(rig).IsOptimizing);
    }

    [Fact]
    public async Task An_optimization_that_cannot_start_says_so_and_is_not_shown_running()
    {
        var rig = await SettingsRig.ShownAsync();
        rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.OptimizeAllStorageAsync), ErrorInfo.From(new InvalidOperationException("Busy.")));

        await Storage(rig).RunNowCommand.ExecuteAsync(null);

        Assert.False(Storage(rig).IsOptimizing);
        Assert.Equal(ToastKind.Error, Assert.Single(rig.Toasts.Toasts).Message.Kind);
    }

    [Fact]
    public async Task A_game_s_own_optimization_does_not_count_as_the_global_one()
    {
        var rig = await SettingsRig.ShownAsync();

        FakeAssert.Ok(await rig.Backend.Games.OptimizeStorageAsync(MockupScenario.SkyrimId));

        Assert.False(Storage(rig).IsOptimizing);
    }

    // ---- Moving the data

    [Fact]
    public async Task Change_opens_the_folder_picker_at_the_current_folder()
    {
        var rig = await SettingsRig.ShownAsync();

        await Storage(rig).ChangeCommand.ExecuteAsync(null);

        var pick = Assert.Single(rig.Dialogs.FolderPicks);
        Assert.Equal(MockupScenario.StorageRoot, pick.StartPath);
        Assert.Equal("Choose a new folder for SavePoint's data", pick.Title);
    }

    [Fact]
    public async Task A_cancelled_picker_changes_nothing()
    {
        var rig = await SettingsRig.ShownAsync();
        rig.Dialogs.AnswerFolder(null);

        await Storage(rig).ChangeCommand.ExecuteAsync(null);

        Assert.Equal(0, rig.Calls<ISettingsApi>(nameof(ISettingsApi.GetRelocationPreviewAsync)));
        Assert.Empty(rig.Dialogs.Confirmations);
    }

    [Fact]
    public async Task The_confirmation_names_how_much_will_be_copied_and_where()
    {
        var rig = await SettingsRig.ShownAsync();
        rig.Dialogs.AnswerFolder(_newRoot).AnswerConfirm(false);

        await Storage(rig).ChangeCommand.ExecuteAsync(null);

        var confirmation = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal("Move SavePoint's data?", confirmation.Title);
        Assert.Contains("79.3 GB", confirmation.Message);
        Assert.Contains("3,430 files", confirmation.Message);
        Assert.Contains(_newRoot, confirmation.Message);
        Assert.Equal("Move data", confirmation.ConfirmLabel);
        Assert.False(confirmation.Destructive);
    }

    [Fact]
    public async Task Declining_the_confirmation_moves_nothing()
    {
        var rig = await SettingsRig.ShownAsync();
        rig.Dialogs.AnswerFolder(_newRoot).AnswerConfirm(false);

        await Storage(rig).ChangeCommand.ExecuteAsync(null);

        Assert.Equal(0, rig.Calls<ISettingsApi>(nameof(ISettingsApi.RelocateStorageAsync)));
        Assert.False(Storage(rig).IsRelocating);
    }

    [Fact]
    public async Task A_move_the_preview_says_cannot_happen_shows_why_and_asks_nothing()
    {
        var rig = await SettingsRig.ShownAsync(WithBlockedMove());
        rig.Dialogs.AnswerFolder(_newRoot);

        await Storage(rig).ChangeCommand.ExecuteAsync(null);

        var problem = Storage(rig).Problem;
        Assert.NotNull(problem);
        Assert.Equal("That folder isn't empty", problem.Title);
        Assert.Empty(rig.Dialogs.Confirmations);
        Assert.Equal(0, rig.Calls<ISettingsApi>(nameof(ISettingsApi.RelocateStorageAsync)));
    }

    [Fact]
    public async Task A_preview_that_cannot_be_read_shows_why_and_asks_nothing()
    {
        var rig = await SettingsRig.ShownAsync();
        rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetRelocationPreviewAsync), ErrorInfo.From(new InvalidOperationException("No preview.")));
        rig.Dialogs.AnswerFolder(_newRoot);

        await Storage(rig).ChangeCommand.ExecuteAsync(null);

        Assert.NotNull(Storage(rig).Problem);
        Assert.Empty(rig.Dialogs.Confirmations);
    }

    [Fact]
    public async Task Confirming_starts_the_move_to_the_chosen_folder_and_shows_it_running()
    {
        var rig = await SettingsRig.ShownAsync();

        await StartedMoveAsync(rig);

        Assert.Equal([_newRoot], rig.Backend.Calls.To<ISettingsApi>(nameof(ISettingsApi.RelocateStorageAsync)).Single().Args);
        Assert.True(Storage(rig).IsRelocating);
        Assert.False(Storage(rig).ChangeCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_move_shows_its_progress_and_the_file_it_is_on()
    {
        var rig = await SettingsRig.ShownAsync();
        var job = await StartedMoveAsync(rig);

        rig.Backend.Jobs.ReportProgress(job.JobId, new JobProgress(FilesDone: 812, FilesTotal: 3_430, BytesDone: 19_800_000_000, BytesTotal: 79_200_000_000, CurrentPath: @"Skyrim\Data\textures\wrbuilding05.dds"));

        Assert.Equal(0.25, Storage(rig).RelocationFraction);
        Assert.Equal(0.25, Storage(rig).RelocationValue);
        Assert.False(Storage(rig).IsRelocationIndeterminate);
        Assert.Equal("19.8 GB of 79.2 GB", Storage(rig).RelocationDetail);
        Assert.Equal(@"Skyrim\Data\textures\wrbuilding05.dds", Storage(rig).RelocationPath);
    }

    [Fact]
    public async Task The_move_announces_its_start_and_each_quarter_once()
    {
        var rig = await SettingsRig.ShownAsync();
        var spoken = new List<string>();
        Storage(rig).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StorageSectionViewModel.Announcement))
            {
                spoken.Add(Storage(rig).Announcement);
            }
        };

        var job = await StartedMoveAsync(rig);
        rig.Backend.Jobs.ReportProgress(job.JobId, new JobProgress(BytesDone: 100, BytesTotal: 1_000));
        rig.Backend.Jobs.ReportProgress(job.JobId, new JobProgress(BytesDone: 260, BytesTotal: 1_000));
        rig.Backend.Jobs.ReportProgress(job.JobId, new JobProgress(BytesDone: 300, BytesTotal: 1_000));
        rig.Backend.Jobs.ReportProgress(job.JobId, new JobProgress(BytesDone: 800, BytesTotal: 1_000));

        Assert.Equal(["Moving your data\u2026", "25% moved.", "75% moved."], spoken);
    }

    [Fact]
    public async Task A_move_with_no_totals_announces_only_that_it_started()
    {
        var rig = await SettingsRig.ShownAsync();
        var job = await StartedMoveAsync(rig);

        rig.Backend.Jobs.ReportProgress(job.JobId, new JobProgress(FilesDone: 40));

        Assert.Equal("Moving your data\u2026", Storage(rig).Announcement);
    }

    [Fact]
    public async Task A_move_with_no_totals_yet_shows_an_indeterminate_bar()
    {
        var rig = await SettingsRig.ShownAsync();
        await StartedMoveAsync(rig);

        Assert.Null(Storage(rig).RelocationFraction);
        Assert.True(Storage(rig).IsRelocationIndeterminate);
        Assert.Equal(0, Storage(rig).RelocationValue);
        Assert.Equal(string.Empty, Storage(rig).RelocationDetail);
    }

    [Fact]
    public async Task Cancel_stops_the_move_through_the_jobs_api()
    {
        var rig = await SettingsRig.ShownAsync();
        var job = await StartedMoveAsync(rig);

        await Storage(rig).CancelRelocationCommand.ExecuteAsync(null);

        var cancel = Assert.Single(rig.Backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
        Assert.Equal([job.JobId], cancel.Args);
    }

    [Fact]
    public async Task A_cancelled_move_leaves_the_old_folder_and_no_error()
    {
        var rig = await SettingsRig.ShownAsync();
        var job = await StartedMoveAsync(rig);

        rig.Backend.Jobs.CompleteCancelled(job.JobId);

        Assert.False(Storage(rig).IsRelocating);
        Assert.Equal(MockupScenario.StorageRoot, Storage(rig).StoragePath);
        Assert.Null(Storage(rig).Problem);
    }

    [Fact]
    public async Task A_finished_move_shows_the_new_folder_and_the_shell_learns_of_it()
    {
        var rig = await SettingsRig.ShownAsync();
        var job = await StartedMoveAsync(rig);

        rig.Backend.Jobs.Complete(job.JobId);

        Assert.False(Storage(rig).IsRelocating);
        Assert.Equal(_newRoot, Storage(rig).StoragePath);
        Assert.Equal(_newRoot, rig.Shell.FirstRun.StoragePath);
    }

    [Fact]
    public async Task A_move_that_could_not_remove_every_old_copy_still_switches_and_lists_what_stayed()
    {
        var rig = await SettingsRig.ShownAsync();
        var job = await StartedMoveAsync(rig);
        var stayed = new ClearFailure(@"D:\Old\game-1\install.git", ErrorInfo.From(new GameRunningException("game-1", "Skyrim")));

        rig.Backend.Jobs.Complete(job.JobId, new StorageRelocationResult(MockupScenario.StorageRoot, _newRoot, 10, [stayed]));

        Assert.False(Storage(rig).IsRelocating);
        Assert.Equal(_newRoot, Storage(rig).StoragePath);
        Assert.Equal(_newRoot, rig.Shell.FirstRun.StoragePath);
        Assert.Null(Storage(rig).Problem);
        var left = Storage(rig).LeftBehind;
        Assert.NotNull(left);
        Assert.Equal("Your data moved, but some old files are still in the old folder", left.Title);
        Assert.Contains(@"D:\Old\game-1\install.git", left.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Starting_another_move_clears_the_list_of_what_stayed()
    {
        var rig = await SettingsRig.ShownAsync();
        var job = await StartedMoveAsync(rig);
        rig.Backend.Jobs.Complete(job.JobId, new StorageRelocationResult(MockupScenario.StorageRoot, _newRoot, 10, [new ClearFailure(@"D:\Old\game-1\install.git", ErrorInfo.From(new GameRunningException("game-1", "Skyrim")))]));
        Assert.NotNull(Storage(rig).LeftBehind);

        rig.Dialogs.AnswerFolder(@"F:\Other");
        await Storage(rig).ChangeCommand.ExecuteAsync(null);

        Assert.Null(Storage(rig).LeftBehind);
    }

    [Fact]
    public async Task A_failed_move_shows_why_and_keeps_the_old_folder()
    {
        var rig = await SettingsRig.ShownAsync();
        var job = await StartedMoveAsync(rig);

        rig.Backend.Jobs.Fail(job.JobId, MockupScenario.SavesOutOfRoomError);

        Assert.False(Storage(rig).IsRelocating);
        var problem = Storage(rig).Problem;
        Assert.NotNull(problem);
        Assert.Equal("There isn't enough room", problem.Title);
        Assert.Equal(MockupScenario.StorageRoot, Storage(rig).StoragePath);
    }

    [Fact]
    public async Task A_move_that_cannot_start_shows_why()
    {
        var rig = await SettingsRig.ShownAsync();
        rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.RelocateStorageAsync), MockupScenario.SavesOutOfRoomError);
        rig.Dialogs.AnswerFolder(_newRoot).AnswerConfirm(true);

        await Storage(rig).ChangeCommand.ExecuteAsync(null);

        Assert.False(Storage(rig).IsRelocating);
        Assert.NotNull(Storage(rig).Problem);
    }

    [Fact]
    public async Task A_move_in_progress_when_the_screen_shows_is_picked_up_with_its_progress()
    {
        var backend = FakeBackend.Mockup();
        FakeAssert.Ok(await backend.Settings.RelocateStorageAsync(_newRoot));
        backend.Jobs.ReportProgress(backend.Jobs.LastStarted(JobKind.RelocateStorage).JobId, new JobProgress(BytesDone: 1_000_000_000, BytesTotal: 4_000_000_000));
        var rig = await SettingsRig.StartAsync(backend);

        rig.Show();

        Assert.True(Storage(rig).IsRelocating);
        Assert.Equal(0.25, Storage(rig).RelocationFraction);
    }

    [Fact]
    public async Task Starting_another_attempt_clears_the_last_problem()
    {
        var rig = await SettingsRig.ShownAsync(WithBlockedMove());
        rig.Dialogs.AnswerFolder(_newRoot);
        await Storage(rig).ChangeCommand.ExecuteAsync(null);
        Assert.NotNull(Storage(rig).Problem);

        rig.Dialogs.AnswerFolder(null);
        await Storage(rig).ChangeCommand.ExecuteAsync(null);

        Assert.Null(Storage(rig).Problem);
    }
}
