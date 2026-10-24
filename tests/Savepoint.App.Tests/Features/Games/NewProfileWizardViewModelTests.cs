using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Games;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

public class NewProfileWizardViewModelTests
{
    private const string _dot = " \u00b7 ";

    private sealed class Rig
    {
        public required FakeBackend Backend { get; init; }

        public required NewProfileWizardViewModel Wizard { get; init; }

        public Guid JobId => Backend.Jobs.LastStarted(JobKind.Capture).JobId;

        public static async Task<Rig> StartAsync(
            FakeBackend? backend = null,
            string gameId = MockupScenario.SkyrimId,
            IUiDispatcher? dispatcher = null,
            bool loadPreview = true,
            Func<FakeBackend, IProfilesApi>? profiles = null)
        {
            backend ??= FakeBackend.Mockup();
            var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, "windows", TimeZoneInfo.Utc);
            var game = FakeAssert.Ok(await backend.Games.ListAsync()).Single(candidate => candidate.Id == gameId);
            var wizard = new NewProfileWizardViewModel(game, backend.Games, profiles?.Invoke(backend) ?? backend.Profiles, backend.Jobs, text, dispatcher ?? new ImmediateUiDispatcher(), backend.Clock);
            if (loadPreview)
            {
                await wizard.LoadPreviewAsync();
            }

            return new Rig { Backend = backend, Wizard = wizard };
        }

        // The wizard on step three with the capture job running.
        public async Task SaveAsync(string name = "My new profile", string notes = "")
        {
            Wizard.Name = name;
            Wizard.Notes = notes;
            await Wizard.ContinueCommand.ExecuteAsync(null);
            await Wizard.ContinueCommand.ExecuteAsync(null);
        }

        public string CreatedProfileId(string name) =>
            FakeAssert.Ok(Backend.Profiles.ListAsync(MockupScenario.SkyrimId).Result).Single(profile => profile.Name == name).Id;
    }

    // An error card's buttons are async commands; running one waits for the work it starts.
    private static Task Run(ErrorCardAction action) => ((IAsyncRelayCommand)action.Command).ExecuteAsync(null);

    // Ends the capture job before CreateAsync answers.
    private sealed class CompletingProfilesApi(FakeBackend backend, bool reportProgress = false) : IProfilesApi
    {
        public async Task<OperationResult<JobStarted>> CreateAsync(string gameId, string name, string? notes, string pointName)
        {
            var started = await backend.Profiles.CreateAsync(gameId, name, notes, pointName);
            if (reportProgress)
            {
                backend.Jobs.ReportProgress(started.Value!.JobId, new JobProgress(5, 10));
            }

            backend.Jobs.Complete(started.Value!.JobId);
            return started;
        }

        public Task<OperationResult<IReadOnlyList<Profile>>> ListAsync(string? gameId = null, CancellationToken cancellationToken = default) =>
            backend.Profiles.ListAsync(gameId, cancellationToken);

        public Task<OperationResult<IReadOnlyList<ProfileRowStats>>> GetRowStatsAsync(string gameId, CancellationToken cancellationToken = default) =>
            backend.Profiles.GetRowStatsAsync(gameId, cancellationToken);

        public Task<OperationResult<Profile>> UpdateAsync(string gameId, string profileId, ProfileChanges changes) =>
            backend.Profiles.UpdateAsync(gameId, profileId, changes);

        public Task<OperationResult<Profile>> DuplicateAsync(string gameId, string sourceProfileId, string name) =>
            backend.Profiles.DuplicateAsync(gameId, sourceProfileId, name);

        public Task<OperationResult<Profile>> BringBackAsync(string gameId, HistoryPointId snapshotId, string name) =>
            backend.Profiles.BringBackAsync(gameId, snapshotId, name);

        public Task<OperationResult<ProfileDeletePreview>> GetDeletePreviewAsync(string gameId, string profileId, CancellationToken cancellationToken = default) =>
            backend.Profiles.GetDeletePreviewAsync(gameId, profileId, cancellationToken);

        public Task<OperationResult<Unit>> DeleteAsync(string gameId, string profileId) => backend.Profiles.DeleteAsync(gameId, profileId);

        public Task<OperationResult<JobStarted>> CaptureAsync(string gameId, string profileId, string pointName, IReadOnlyCollection<RepoRole>? folders = null) =>
            backend.Profiles.CaptureAsync(gameId, profileId, pointName, folders);
    }

    private static CaptureResult Half(string profileId) =>
        new(profileId, new HistoryPointId("pt"), [new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.External, MockupScenario.SavesOutOfRoomError)]);

    // ---- Step one

    [Fact]
    public async Task The_wizard_is_named_after_the_game_and_starts_on_step_one()
    {
        var rig = await Rig.StartAsync();
        var wizard = rig.Wizard;

        Assert.Equal("New profile for Skyrim Special Edition", wizard.Title);
        Assert.Equal(620, wizard.DialogWidth);
        Assert.Equal(WizardStep.NameIt, wizard.Step);
        Assert.Equal(1, wizard.StepNumber);
        Assert.True(wizard.IsNameStep);
        Assert.False(wizard.IsCheckStep);
        Assert.False(wizard.IsSaveStep);
        Assert.Equal(["Name it", "Check", "Create"], wizard.Steps.Select(step => step.Label));
    }

    [Fact]
    public async Task The_name_starts_as_a_dated_suggestion_with_the_american_helper()
    {
        var rig = await Rig.StartAsync();

        Assert.Equal("Modded playthrough - 7-8-26", rig.Wizard.Name);
        Assert.Equal(string.Empty, rig.Wizard.Notes);
        Assert.True(rig.Wizard.CanContinue);
        Assert.Equal("Anything that helps you recognize it later.", rig.Wizard.NameHelper);
    }

    [Fact]
    public async Task The_step_dots_show_where_the_user_is()
    {
        var rig = await Rig.StartAsync();
        var steps = rig.Wizard.Steps;

        Assert.Equal([true, false, false], steps.Select(step => step.IsCurrent));
        Assert.Equal([true, false, false], steps.Select(step => step.IsFilled));
        Assert.Equal([false, true, true], steps.Select(step => step.IsUpcoming));
        Assert.Equal(["Step 1 of 3: Name it, current step", "Step 2 of 3: Check", "Step 3 of 3: Create"], steps.Select(step => step.AutomationName));
    }

    [Fact]
    public async Task The_preview_lists_both_folders_with_their_sizes_and_what_saving_will_add()
    {
        var rig = await Rig.StartAsync();

        var preview = rig.Wizard.Preview;

        Assert.False(preview.IsLoading);
        Assert.True(preview.HasRows);
        Assert.Equal(
            [
                new CaptureRowViewModel(Icons.Folder, "Game version and mods", @"C:\Games\SkyrimSE" + _dot + "212 mod files" + _dot + "41.2 GB"),
                new CaptureRowViewModel(Icons.Save, "Current saves", MockupScenario.SkyrimSaves + _dot + "18 files"),
            ],
            preview.Rows);
        Assert.True(preview.HasNote);
        Assert.Equal("SavePoint reuses files it already has, so this will only add about 2.4 GB.", preview.Note);
        Assert.False(preview.HasError);
    }

    [Fact]
    public async Task The_preview_says_it_is_loading_until_it_arrives()
    {
        var rig = await Rig.StartAsync(loadPreview: false);

        Assert.True(rig.Wizard.Preview.IsLoading);
        Assert.False(rig.Wizard.Preview.HasRows);
        Assert.False(rig.Wizard.Preview.HasNote);

        await rig.Wizard.LoadPreviewAsync();

        Assert.False(rig.Wizard.Preview.IsLoading);
    }

    [Fact]
    public async Task One_file_and_no_mods_read_naturally()
    {
        var preview = new CapturePreview(
            MockupScenario.CaretakerId,
            [
                new FolderCapturePreview(RepoRole.Install, @"C:\Games\The Last Caretaker", 1, 0, 9_400_000_000),
                new FolderCapturePreview(RepoRole.External, @"C:\Saves", 1, 0, 2_000),
            ],
            0);
        var rig = await Rig.StartAsync(new FakeBackend(ScenarioBuilder.Mockup().WithCapturePreview(preview)), MockupScenario.CaretakerId);

        Assert.Equal(@"C:\Games\The Last Caretaker" + _dot + "9.4 GB", rig.Wizard.Preview.Rows[0].Subtitle);
        Assert.Equal(@"C:\Saves" + _dot + "1 file", rig.Wizard.Preview.Rows[1].Subtitle);
        Assert.False(rig.Wizard.Preview.HasNote);
    }

    [Fact]
    public async Task A_game_without_a_saves_folder_lists_only_the_install()
    {
        var game = MockupScenario.Caretaker with { External = null };
        var rig = await Rig.StartAsync(new FakeBackend(ScenarioBuilder.Empty().WithSettings(MockupScenario.Settings).WithGame(game, 1)), MockupScenario.CaretakerId);

        Assert.Single(rig.Wizard.Preview.Rows);
    }

    [Fact]
    public async Task A_preview_that_cannot_be_read_says_so_and_does_not_stop_the_user()
    {
        var backend = FakeBackend.Mockup();
        backend.FailNext<IGamesApi>(nameof(IGamesApi.GetCapturePreviewAsync), MockupScenario.FileInUseError);
        var rig = await Rig.StartAsync(backend);

        Assert.False(rig.Wizard.Preview.IsLoading);
        Assert.True(rig.Wizard.Preview.HasError);
        Assert.Equal("SavePoint couldn't check how much will be copied. You can still continue.", rig.Wizard.Preview.Error);
        Assert.Empty(rig.Wizard.Preview.Rows);
        Assert.True(rig.Wizard.CanContinue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Continue_needs_a_name(string name)
    {
        var rig = await Rig.StartAsync();

        rig.Wizard.Name = name;

        Assert.False(rig.Wizard.CanContinue);
        Assert.False(rig.Wizard.ContinueCommand.CanExecute(null));
        await rig.Wizard.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(WizardStep.NameIt, rig.Wizard.Step);
    }

    [Fact]
    public async Task Cancel_on_step_one_closes_without_saving()
    {
        var rig = await Rig.StartAsync();

        rig.Wizard.CancelCommand.Execute(null);

        Assert.False(await rig.Wizard.Result.Soon());
        Assert.Empty(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CreateAsync)));
    }

    [Fact]
    public async Task Step_one_offers_cancel_and_continue_only()
    {
        var rig = await Rig.StartAsync();

        Assert.True(rig.Wizard.ShowCancel);
        Assert.True(rig.Wizard.ShowContinue);
        Assert.False(rig.Wizard.ShowBack);
        Assert.False(rig.Wizard.ShowStopSave);
        Assert.False(rig.Wizard.ShowDone);
        Assert.False(rig.Wizard.ShowClose);
    }

    // ---- Step two

    [Fact]
    public async Task Continue_moves_to_the_check_step_and_back_returns_with_everything_kept()
    {
        var rig = await Rig.StartAsync();
        rig.Wizard.Name = "  Nolvus  ";
        rig.Wizard.Notes = " before the patch ";

        await rig.Wizard.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(WizardStep.Check, rig.Wizard.Step);
        Assert.Equal(2, rig.Wizard.StepNumber);
        Assert.True(rig.Wizard.IsCheckStep);
        Assert.Equal("Nolvus", rig.Wizard.TrimmedName);
        Assert.Equal("before the patch", rig.Wizard.TrimmedNotes);
        Assert.True(rig.Wizard.HasNotes);
        Assert.Equal([false, false, true], rig.Wizard.Steps.Select(step => step.IsUpcoming));
        Assert.Equal([true, true, false], rig.Wizard.Steps.Select(step => step.IsFilled));
        Assert.Equal([false, true, false], rig.Wizard.Steps.Select(step => step.IsCurrent));
        Assert.Equal("Step 1 of 3: Name it, done", rig.Wizard.Steps[0].AutomationName);
        Assert.Equal("Step 2 of 3: Check, current step", rig.Wizard.Steps[1].AutomationName);
        Assert.True(rig.Wizard.ShowBack);
        Assert.True(rig.Wizard.ShowContinue);
        Assert.False(rig.Wizard.ShowCancel);

        rig.Wizard.BackCommand.Execute(null);

        Assert.Equal(WizardStep.NameIt, rig.Wizard.Step);
        Assert.Equal("  Nolvus  ", rig.Wizard.Name);
        Assert.Equal(" before the patch ", rig.Wizard.Notes);
        Assert.Empty(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CreateAsync)));
    }

    [Fact]
    public async Task Blank_notes_are_not_shown_on_the_check_step()
    {
        var rig = await Rig.StartAsync();
        rig.Wizard.Notes = "   ";

        await rig.Wizard.ContinueCommand.ExecuteAsync(null);

        Assert.False(rig.Wizard.HasNotes);
    }

    [Fact]
    public async Task Continue_on_the_check_step_creates_the_profile_from_the_trimmed_name_and_notes()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync("  Nolvus  ", " before the patch ");

        var call = Assert.Single(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CreateAsync)));
        Assert.Equal([MockupScenario.SkyrimId, "Nolvus", "before the patch", "Nolvus"], call.Args);
        Assert.Equal(WizardStep.Save, rig.Wizard.Step);
    }

    [Fact]
    public async Task Blank_notes_are_sent_as_nothing()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync("Nolvus", "  ");

        var call = Assert.Single(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CreateAsync)));
        Assert.Null(call.Args[2]);
    }

    [Fact]
    public async Task A_refused_create_stays_on_the_check_step_and_says_why()
    {
        var rig = await Rig.StartAsync();
        rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.CreateAsync), MockupScenario.SavesOutOfRoomError);
        rig.Wizard.Name = "Nolvus";
        await rig.Wizard.ContinueCommand.ExecuteAsync(null);

        await rig.Wizard.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(WizardStep.Check, rig.Wizard.Step);
        Assert.True(rig.Wizard.HasSubmitError);
        Assert.Equal("There's not enough free disk space to take this snapshot right now. Free up about 1.2 GB on drive D: and try again.", rig.Wizard.SubmitError);
        Assert.Empty(rig.Backend.Jobs.Started);

        await rig.Wizard.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(WizardStep.Save, rig.Wizard.Step);
        Assert.False(rig.Wizard.HasSubmitError);
    }

    // ---- Step three: progress

    [Fact]
    public async Task Saving_starts_indeterminate_with_the_hint_and_a_cancel_button()
    {
        var rig = await Rig.StartAsync();

        await rig.SaveAsync();

        var wizard = rig.Wizard;
        Assert.True(wizard.IsSaveStep);
        Assert.Equal(SaveState.Saving, wizard.State);
        Assert.True(wizard.IsSaving);
        Assert.Equal("Taking the first snapshot\u2026", wizard.StatusText);
        Assert.Equal("This can take a while for a large install, especially the first time.", wizard.ProgressText);
        Assert.True(wizard.IsIndeterminate);
        Assert.False(wizard.HasCurrentFile);
        Assert.True(wizard.ShowStopSave);
        Assert.True(wizard.ShowKeepWorking);
        Assert.False(wizard.ShowContinue);
        Assert.False(wizard.ShowBack);
        Assert.False(wizard.ShowCancel);
        Assert.False(wizard.ShowDone);
        Assert.False(wizard.ShowClose);
        Assert.Equal("Taking the first snapshot\u2026", wizard.Announcement);
        Assert.Equal([true, true, true], wizard.Steps.Select(step => step.IsFilled));
    }

    [Fact]
    public async Task Progress_with_a_total_is_determinate_and_names_the_file()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync();

        rig.Backend.Jobs.ReportProgress(rig.JobId, new JobProgress(1200, 3430, CurrentPath: @"Data\textures\sky.dds"));

        var wizard = rig.Wizard;
        Assert.Equal("1,200 of 3,430 files copied", wizard.ProgressText);
        Assert.False(wizard.IsIndeterminate);
        Assert.Equal(35, wizard.ProgressPercent, 0);
        Assert.True(wizard.HasCurrentFile);
        Assert.Equal(@"Latest file finished: Data\textures\sky.dds", wizard.CurrentFileText);
        Assert.False(wizard.HasFilesText);
    }

    [Fact]
    public async Task Progress_with_only_a_count_says_the_total_is_still_being_worked_out()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync();

        rig.Backend.Jobs.ReportProgress(rig.JobId, new JobProgress(FilesDone: 12));

        Assert.Equal("Working out how much there is to copy…", rig.Wizard.ProgressText);
        Assert.Equal("12 files copied", rig.Wizard.FilesText);
        Assert.True(rig.Wizard.IsIndeterminate);
        Assert.False(rig.Wizard.HasCurrentFile);
    }

    [Fact]
    public async Task One_file_is_not_called_files()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync();

        rig.Backend.Jobs.ReportProgress(rig.JobId, new JobProgress(FilesDone: 1));
        Assert.Equal("1 file copied", rig.Wizard.FilesText);

        rig.Backend.Jobs.ReportProgress(rig.JobId, new JobProgress(1, 1));
        Assert.Equal("1 of 1 file copied", rig.Wizard.ProgressText);
    }

    [Fact]
    public async Task Bytes_move_the_bar_and_the_text_while_no_file_has_finished()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync();

        rig.Backend.Jobs.ReportProgress(rig.JobId, new JobProgress(0, 23, 3_100_000_000, 70_000_000_000, "user.gls"));

        var wizard = rig.Wizard;
        Assert.Equal("3.1 GB of 70 GB copied", wizard.ProgressText);
        Assert.False(wizard.IsIndeterminate);
        Assert.Equal(4.4, wizard.ProgressPercent, 1);
        Assert.Equal("0 of 23 files finished", wizard.FilesText);
        Assert.True(wizard.HasFilesText);
        Assert.Equal("Latest file finished: user.gls", wizard.CurrentFileText);
    }

    [Fact]
    public async Task The_bar_follows_the_bytes_not_the_files_when_both_are_known()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync();

        rig.Backend.Jobs.ReportProgress(rig.JobId, new JobProgress(22, 23, 35_000_000_000, 70_000_000_000));

        Assert.Equal(50, rig.Wizard.ProgressPercent, 1);
        Assert.Equal("35 GB of 70 GB copied", rig.Wizard.ProgressText);
    }

    [Fact]
    public async Task Bytes_without_a_file_count_still_fill_the_bar()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync();

        rig.Backend.Jobs.ReportProgress(rig.JobId, new JobProgress(BytesDone: 1_000_000_000, BytesTotal: 4_000_000_000));

        Assert.Equal(25, rig.Wizard.ProgressPercent, 1);
        Assert.False(rig.Wizard.IsIndeterminate);
        Assert.False(rig.Wizard.HasFilesText);
    }

    [Fact]
    public async Task When_the_store_holds_nearly_everything_the_text_says_it_is_being_made_safe()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync();

        rig.Backend.Jobs.ReportProgress(rig.JobId, new JobProgress(3, 23, 69_300_000_000, 70_000_000_000));

        Assert.Equal("Making sure everything is stored safely…", rig.Wizard.ProgressText);
        Assert.False(rig.Wizard.IsIndeterminate);
    }

    [Fact]
    public async Task Milestones_are_announced_once_each()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync();

        rig.Backend.Jobs.ReportProgress(rig.JobId, new JobProgress(1000, 4000));
        Assert.Equal("25% copied", rig.Wizard.Announcement);

        rig.Backend.Jobs.ReportProgress(rig.JobId, new JobProgress(1100, 4000));
        Assert.Equal("25% copied", rig.Wizard.Announcement);

        rig.Backend.Jobs.ReportProgress(rig.JobId, new JobProgress(3100, 4000));
        Assert.Equal("75% copied", rig.Wizard.Announcement);
    }

    [Fact]
    public async Task Milestones_follow_the_bytes_during_one_large_file()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync();

        rig.Backend.Jobs.ReportProgress(rig.JobId, new JobProgress(0, 3, 520, 1000));

        Assert.Equal("50% copied", rig.Wizard.Announcement);
    }

    // ---- Step three: keep working

    [Fact]
    public async Task Keep_working_closes_the_wizard_and_leaves_the_job_running()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync();

        rig.Wizard.KeepWorkingCommand.Execute(null);

        Assert.True(rig.Wizard.IsClosed);
        Assert.False(await rig.Wizard.Result.Soon());
        Assert.Empty(rig.Backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
        Assert.Equal(JobStatus.Running, rig.Backend.Jobs.LastStarted(JobKind.Capture).Status);
    }

    [Fact]
    public async Task Keep_working_is_offered_only_while_the_snapshot_runs()
    {
        var rig = await Rig.StartAsync();
        Assert.False(rig.Wizard.ShowKeepWorking);
        await rig.SaveAsync();
        Assert.True(rig.Wizard.ShowKeepWorking);

        rig.Backend.Jobs.Complete(rig.JobId);

        Assert.False(rig.Wizard.ShowKeepWorking);
    }

    [Fact]
    public async Task Events_reach_the_wizard_through_the_dispatcher_and_other_jobs_are_ignored()
    {
        var dispatcher = new QueueingDispatcher();
        var rig = await Rig.StartAsync(dispatcher: dispatcher);
        await rig.SaveAsync();
        var other = FakeAssert.Ok(await rig.Backend.Games.OptimizeStorageAsync(MockupScenario.SatisfactoryId));
        dispatcher.RunAll();

        rig.Backend.Jobs.ReportProgress(rig.JobId, new JobProgress(5, 10));
        rig.Backend.Jobs.ReportProgress(other.JobId, new JobProgress(9, 9));

        Assert.True(rig.Wizard.IsIndeterminate);
        dispatcher.RunAll();
        Assert.Equal("5 of 10 files copied", rig.Wizard.ProgressText);
    }

    [Fact]
    public async Task A_job_that_finished_before_the_wizard_knew_its_id_is_still_picked_up()
    {
        // The job ends inside the create call, so its events come and go before the wizard has an id to match them to.
        var rig = await Rig.StartAsync(profiles: backend => new CompletingProfilesApi(backend));

        await rig.SaveAsync();

        Assert.Equal(SaveState.Saved, rig.Wizard.State);
        Assert.True(rig.Wizard.ShowDone);
    }

    [Fact]
    public async Task An_event_older_than_the_snapshot_already_held_is_dropped()
    {
        // Progress and the end both arrive inside the create call; the wizard reads the final snapshot, and the
        // queued progress event that reaches the dispatcher afterwards is older than that.
        var dispatcher = new QueueingDispatcher();
        var rig = await Rig.StartAsync(dispatcher: dispatcher, profiles: backend => new CompletingProfilesApi(backend, reportProgress: true));
        await rig.SaveAsync();
        var shown = rig.Wizard.ProgressText;

        dispatcher.RunAll();

        Assert.Equal(SaveState.Saved, rig.Wizard.State);
        Assert.Equal(shown, rig.Wizard.ProgressText);
        Assert.NotEqual("5 of 10 files copied", rig.Wizard.ProgressText);
    }

    [Fact]
    public async Task Events_queued_before_the_wizard_closed_are_dropped()
    {
        var dispatcher = new QueueingDispatcher();
        var rig = await Rig.StartAsync(dispatcher: dispatcher);
        await rig.SaveAsync();
        rig.Backend.Jobs.ReportProgress(rig.JobId, new JobProgress(5, 10));

        rig.Wizard.RequestDismiss();
        dispatcher.RunAll();

        Assert.True(rig.Wizard.IsIndeterminate);
        Assert.NotEqual("5 of 10 files copied", rig.Wizard.ProgressText);
    }

    // ---- Step three: outcomes

    [Fact]
    public async Task A_complete_save_says_so_and_offers_done()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync();

        rig.Backend.Jobs.Complete(rig.JobId);

        var wizard = rig.Wizard;
        Assert.Equal(SaveState.Saved, wizard.State);
        Assert.True(wizard.IsSaved);
        Assert.False(wizard.IsSaving);
        Assert.Equal("Profile created.", wizard.StatusText);
        Assert.Equal("Profile created.", wizard.Announcement);
        Assert.Null(wizard.ResultCard);
        Assert.False(wizard.HasResultCard);
        Assert.True(wizard.ShowDone);
        Assert.False(wizard.ShowStopSave);
        Assert.False(wizard.ShowClose);

        wizard.CloseCommand.Execute(null);

        Assert.True(await wizard.Result.Soon());
    }

    [Fact]
    public async Task A_half_saved_capture_shows_the_card_with_what_was_and_was_not_saved()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync("My new profile");

        rig.Backend.Jobs.Complete(rig.JobId, Half(rig.CreatedProfileId("My new profile")));

        var wizard = rig.Wizard;
        Assert.Equal(SaveState.Partial, wizard.State);
        Assert.False(wizard.IsSaved);
        var card = Assert.IsType<ErrorCardViewModel>(wizard.ResultCard);
        Assert.Equal("Only part of the snapshot was taken", card.Title);
        Assert.Equal(
            "Your game version and mods are in the snapshot. Your saves aren't yet. There's not enough free disk space to take this snapshot right now. Free up about 1.2 GB on drive D: and try again. Nothing in your game folders changed. Retry just that part.",
            card.Body);
        Assert.Equal(Icons.Warning, card.Icon);
        Assert.True(card.IsWarning);
        Assert.Equal("D:", card.Path);
        var retry = Assert.Single(card.Actions);
        Assert.Equal("Retry the saves", retry.Label);
        Assert.True(retry.IsPrimary);
        var list = Assert.IsType<SaveOutcomeListViewModel>(card.Supplement);
        Assert.Equal(
            [
                new SaveOutcomeRow(Icons.CheckCircle, true, "Game version and mods \u00b7 in the snapshot"),
                new SaveOutcomeRow(Icons.Cancel, false, "Saves \u00b7 not in the snapshot yet"),
            ],
            list.Rows);
        Assert.True(wizard.ShowClose);
        Assert.False(wizard.ShowDone);
        Assert.Equal("Only part of the snapshot was taken", wizard.Announcement);
    }

    [Fact]
    public async Task When_the_install_is_what_failed_the_card_and_retry_say_so()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync("My new profile");
        var pid = rig.CreatedProfileId("My new profile");

        rig.Backend.Jobs.Complete(rig.JobId, new CaptureResult(pid, new HistoryPointId("pt"), [new FolderOutcome(RepoRole.Install, MockupScenario.FileInUseError), new FolderOutcome(RepoRole.External)]));

        var card = rig.Wizard.ResultCard!;
        Assert.StartsWith("Your saves are in the snapshot. Your game version and mods aren't yet.", card.Body);
        Assert.Equal("Retry the game version and mods", Assert.Single(card.Actions).Label);
        Assert.Equal(
            [("Game version and mods \u00b7 not in the snapshot yet", false), ("Saves \u00b7 in the snapshot", true)],
            ((SaveOutcomeListViewModel)card.Supplement!).Rows.Select(row => (row.Text, row.Succeeded)));
        Assert.True(card.HasDetail);
        Assert.Equal(@"C:\Games\SkyrimSE\Data\Skyrim.esm", card.Path);
    }

    [Fact]
    public async Task Retrying_the_saves_captures_only_the_folder_that_failed_and_then_finishes_the_save()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync("My new profile");
        var pid = rig.CreatedProfileId("My new profile");
        rig.Backend.Jobs.Complete(rig.JobId, Half(pid));
        var firstJob = rig.JobId;

        await Run(rig.Wizard.ResultCard!.Actions.Single());

        var call = Assert.Single(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));
        Assert.Equal([MockupScenario.SkyrimId, pid, "My new profile"], call.Args.Take(3));
        Assert.Equal([RepoRole.External], Assert.IsAssignableFrom<IEnumerable<RepoRole>>(call.Args[3]));
        Assert.Equal(SaveState.Saving, rig.Wizard.State);
        Assert.Null(rig.Wizard.ResultCard);
        Assert.True(rig.Wizard.ShowStopSave);
        Assert.NotEqual(firstJob, rig.JobId);

        rig.Backend.Jobs.Complete(rig.JobId, new CaptureResult(pid, new HistoryPointId("pt2"), [new FolderOutcome(RepoRole.External)]));

        Assert.Equal(SaveState.Saved, rig.Wizard.State);
        Assert.True(rig.Wizard.ShowDone);
    }

    [Fact]
    public async Task A_retry_that_fails_again_shows_the_card_again()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync("My new profile");
        var pid = rig.CreatedProfileId("My new profile");
        rig.Backend.Jobs.Complete(rig.JobId, Half(pid));
        await Run(rig.Wizard.ResultCard!.Actions.Single());

        rig.Backend.Jobs.Complete(rig.JobId, new CaptureResult(pid, null, [new FolderOutcome(RepoRole.External, MockupScenario.SavesOutOfRoomError)]));

        Assert.Equal(SaveState.Partial, rig.Wizard.State);
        Assert.Equal("Only part of the snapshot was taken", rig.Wizard.ResultCard!.Title);
        Assert.Equal("Retry the saves", rig.Wizard.ResultCard.Actions.Single().Label);
    }

    [Fact]
    public async Task A_retry_the_api_refuses_says_why_and_keeps_the_card()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync("My new profile");
        rig.Backend.Jobs.Complete(rig.JobId, Half(rig.CreatedProfileId("My new profile")));
        rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.CaptureAsync), MockupScenario.FileInUseError);

        await Run(rig.Wizard.ResultCard!.Actions.Single());

        Assert.Equal(SaveState.Partial, rig.Wizard.State);
        Assert.NotNull(rig.Wizard.ResultCard);
        Assert.True(rig.Wizard.HasSubmitError);
    }

    [Fact]
    public async Task A_failed_save_shows_the_errors_card_and_try_again_starts_over()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync("My new profile");

        rig.Backend.Jobs.Fail(rig.JobId, MockupScenario.SavesOutOfRoomError);

        Assert.Equal(SaveState.Failed, rig.Wizard.State);
        var card = rig.Wizard.ResultCard!;
        Assert.Equal("There isn't enough room", card.Title);
        Assert.Equal("There's not enough free disk space to take this snapshot right now. Free up about 1.2 GB on drive D: and try again.", card.Body);
        Assert.Equal("Try again", Assert.Single(card.Actions).Label);
        Assert.True(rig.Wizard.ShowClose);

        await Run(card.Actions.Single());

        Assert.Equal(2, rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CreateAsync)).Count);
        Assert.Equal(SaveState.Saving, rig.Wizard.State);
        Assert.Null(rig.Wizard.ResultCard);
    }

    [Fact]
    public async Task Cancel_asks_the_job_to_stop_and_a_cancelled_save_says_nothing_was_kept()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync("My new profile");

        await rig.Wizard.StopSaveCommand.ExecuteAsync(null);

        Assert.True(rig.Backend.Jobs.LastStarted(JobKind.Capture).CancelRequested);
        Assert.Equal(SaveState.Saving, rig.Wizard.State);
        Assert.True(rig.Wizard.ShowStopSave);
        Assert.False(rig.Wizard.StopSaveCommand.CanExecute(null));

        rig.Backend.Jobs.CompleteCancelled(rig.JobId);

        Assert.Equal(SaveState.Cancelled, rig.Wizard.State);
        var card = rig.Wizard.ResultCard!;
        Assert.Equal("Canceled", card.Title);
        Assert.Equal("The snapshot was canceled. Your earlier snapshots are untouched.", card.Body);
        Assert.Equal("Try again", Assert.Single(card.Actions).Label);
        Assert.True(rig.Wizard.ShowClose);
    }

    // ---- Closing

    [Fact]
    public async Task The_wizard_can_be_closed_mid_save_and_then_ignores_the_job()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync();
        Assert.True(rig.Wizard.CanDismiss);

        rig.Wizard.RequestDismiss();

        Assert.False(await rig.Wizard.Result.Soon());
        rig.Backend.Jobs.ReportProgress(rig.JobId, new JobProgress(5, 10));
        rig.Backend.Jobs.Complete(rig.JobId);
        Assert.Equal(SaveState.Saving, rig.Wizard.State);
        Assert.True(rig.Wizard.IsIndeterminate);
        Assert.Empty(rig.Backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [Fact]
    public async Task Closing_after_a_half_save_still_reports_that_a_profile_was_made()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync("My new profile");
        rig.Backend.Jobs.Complete(rig.JobId, Half(rig.CreatedProfileId("My new profile")));

        rig.Wizard.CloseCommand.Execute(null);

        Assert.True(await rig.Wizard.Result.Soon());
    }

    [Fact]
    public async Task Closing_after_a_failed_save_reports_that_nothing_was_made()
    {
        var rig = await Rig.StartAsync();
        await rig.SaveAsync("My new profile");
        rig.Backend.Jobs.Fail(rig.JobId, MockupScenario.SavesOutOfRoomError);

        rig.Wizard.CloseCommand.Execute(null);

        Assert.False(await rig.Wizard.Result.Soon());
    }

    [Fact]
    public async Task Escape_before_saving_closes_with_false()
    {
        var rig = await Rig.StartAsync();

        rig.Wizard.RequestDismiss();

        Assert.False(await rig.Wizard.Result.Soon());
    }
}
