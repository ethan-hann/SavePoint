using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Launch;

public class LaunchDialogViewModelTests : IDisposable
{
    private const string _survivalName = "Survival test - 5-14-26";

    private readonly LaunchRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private static LaunchPreview Clean(LaunchPreview preview) => preview with
    {
        Folders = [.. preview.Folders.Select(folder => folder with { Dirty = false, DirtyFileCount = 0, DirtyOwnerProfileId = null })],
    };

    private static LaunchPreview Unclaimed(LaunchPreview preview) =>
        preview.WithFolder(RepoRole.External, saves => saves with { DirtyOwnerProfileId = null });

    private static LaunchPreview BothDirty(LaunchPreview preview) =>
        preview.WithFolder(RepoRole.Install, install => install with { Dirty = true, DirtyFileCount = 12, DirtyOwnerProfileId = SampleScenario.SurvivalId });

    private static LaunchPreview WithInstallSwitch(LaunchPreview preview, SwitchSummary? summary) =>
        preview.WithFolder(RepoRole.Install, install => install with { Switch = summary });

    private Guid LastCaptureJob() => _rig.Backend.Jobs.LastStarted(JobKind.Capture).JobId;

    [Fact]
    public async Task The_title_names_the_profile_and_the_subtitle_says_what_follows()
    {
        var dialog = await _rig.DialogAsync();

        Assert.Equal($"Switch to \"{_survivalName}\"?", dialog.Title);
        Assert.Equal("SavePoint will change this before the game starts.", dialog.Subtitle);
        Assert.Equal(600, dialog.DialogWidth);
    }

    [Fact]
    public async Task The_version_card_names_both_versions_the_mod_files_the_size_and_the_time()
    {
        var dialog = await _rig.DialogAsync();

        Assert.True(dialog.HasVersionSection);
        Assert.Equal("Going from 1.6.1170 with no mods, back to ", dialog.VersionBefore);
        Assert.Equal("1.6.640 with 189 mod files", dialog.VersionTarget);
        Assert.Equal(". About 6.1 GB of files will be replaced. Takes roughly 2 minutes.", dialog.VersionAfter);
    }

    [Fact]
    public async Task The_version_card_is_left_out_when_the_install_already_matches()
    {
        var dialog = await _rig.DialogAsync(LaunchRig.ClearedBlackreachPoint);

        Assert.False(dialog.HasVersionSection);
    }

    [Fact]
    public async Task Moving_to_a_newer_version_says_to_and_not_back_to()
    {
        var dialog = await _rig.DialogAsync(change: p => p with { CurrentInstall = new InstallState("1.6.640", 212), TargetInstall = new InstallState("1.6.1170", 0) });

        Assert.Equal("Going from 1.6.640 with 212 mod files, to ", dialog.VersionBefore);
        Assert.Equal("1.6.1170 with no mods", dialog.VersionTarget);
    }

    [Fact]
    public async Task A_version_that_does_not_change_is_not_named_twice_and_only_the_mods_that_change_are_said()
    {
        var dialog = await _rig.DialogAsync(change: p => p with { CurrentInstall = new InstallState("1.6.640", 212), TargetInstall = new InstallState("1.6.640", 0) });

        Assert.Equal("Going from 212 mod files to ", dialog.VersionBefore);
        Assert.Equal("no mods", dialog.VersionTarget);
        Assert.DoesNotContain("1.6.640", dialog.VersionBefore + dialog.VersionTarget + dialog.VersionAfter, StringComparison.Ordinal);
        Assert.StartsWith(". About ", dialog.VersionAfter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_version_and_mods_that_both_stay_are_said_once_as_staying()
    {
        var dialog = await _rig.DialogAsync(change: p => p with { CurrentInstall = new InstallState("1.6.640", 212), TargetInstall = new InstallState("1.6.640", 212) });

        Assert.Equal("Staying on ", dialog.VersionBefore);
        Assert.Equal("1.6.640 with 212 mod files", dialog.VersionTarget);
        Assert.StartsWith(". About ", dialog.VersionAfter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_version_that_stays_with_no_mod_folders_to_count_is_said_once_as_staying()
    {
        var dialog = await _rig.DialogAsync(change: p => p with { CurrentInstall = new InstallState("++Game+release.1.4.7", null), TargetInstall = new InstallState("++Game+release.1.4.7", null) });

        Assert.Equal("Staying on ", dialog.VersionBefore);
        Assert.Equal("++Game+release.1.4.7", dialog.VersionTarget);
    }

    [Fact]
    public async Task Versions_that_cannot_be_compared_are_described_without_a_direction()
    {
        var dialog = await _rig.DialogAsync(change: p => p with { CurrentInstall = new InstallState(null, null), TargetInstall = new InstallState("beta", null) });

        Assert.Equal("Going from an unknown version, to ", dialog.VersionBefore);
        Assert.Equal("beta", dialog.VersionTarget);
    }

    [Fact]
    public async Task Without_an_estimate_from_the_service_the_time_comes_from_the_assumed_throughput()
    {
        var dialog = await _rig.DialogAsync(change: p => WithInstallSwitch(p, new SwitchSummary(1_311, 189, 6_100_000_000, null)));

        Assert.EndsWith("Takes roughly 2 minutes.", dialog.VersionAfter);
    }

    [Fact]
    public async Task An_estimate_from_the_service_wins_over_the_assumption()
    {
        var dialog = await _rig.DialogAsync(change: p => WithInstallSwitch(p, new SwitchSummary(1_311, 189, 6_100_000_000, TimeSpan.FromMinutes(7))));

        Assert.EndsWith("Takes roughly 7 minutes.", dialog.VersionAfter);
    }

    [Fact]
    public async Task A_quick_switch_says_it_takes_less_than_a_minute()
    {
        var dialog = await _rig.DialogAsync(change: p => WithInstallSwitch(p, new SwitchSummary(40, 0, 1_000_000_000, null)));

        Assert.Equal(". About 1 GB of files will be replaced. Takes less than a minute.", dialog.VersionAfter);
    }

    [Fact]
    public async Task Files_the_switch_removes_are_sized_in_their_own_sentence()
    {
        var dialog = await _rig.DialogAsync(change: p => WithInstallSwitch(p, new SwitchSummary(41, 0, 3, null, 600_000_000)));

        Assert.Equal(". About 3 bytes of files will be replaced. About 600 MB of files will be removed. Takes less than a minute.", dialog.VersionAfter);
    }

    [Fact]
    public async Task A_switch_that_only_removes_files_says_so_without_a_replaced_amount()
    {
        var dialog = await _rig.DialogAsync(change: p => WithInstallSwitch(p, new SwitchSummary(1, 0, 0, null, 600_000_000)));

        Assert.Equal(". About 600 MB of files will be removed. Takes less than a minute.", dialog.VersionAfter);
    }

    [Fact]
    public async Task With_no_summary_the_sentence_ends_after_the_versions()
    {
        var dialog = await _rig.DialogAsync(change: p => WithInstallSwitch(p, null));

        Assert.Equal(".", dialog.VersionAfter);
    }

    [Fact]
    public async Task Several_saves_are_offered_in_a_picker_with_the_tip_selected()
    {
        var dialog = await _rig.DialogAsync();

        Assert.True(dialog.HasSaveSection);
        Assert.True(dialog.HasSaveChoice);
        Assert.Collection(
            dialog.SaveOptions,
            first =>
            {
                Assert.Equal("Deep cave run \u00B7 May 30, 2026, 11:42 PM", first.Title);
                Assert.Equal("Latest · 2 save snapshots to choose from", first.Subtitle);
            },
            second =>
            {
                Assert.Equal("First night \u00B7 May 14, 2026, 9:04 PM", second.Title);
                Assert.Equal("2 save snapshots to choose from", second.Subtitle);
            });
        Assert.Same(dialog.SaveOptions[0], dialog.SelectedSave);
        Assert.Equal("This replaces what's in your saves folder now.", dialog.SaveCaption);
    }

    [Fact]
    public async Task One_save_is_shown_as_text_with_no_picker()
    {
        var dialog = await _rig.DialogAsync(change: p => p with { AvailableSaves = [p.AvailableSaves[0]] });

        Assert.True(dialog.HasSaveSection);
        Assert.False(dialog.HasSaveChoice);
        var only = Assert.Single(dialog.SaveOptions);
        Assert.Equal("Latest · 1 save snapshot to choose from", only.Subtitle);
        Assert.Same(only, dialog.SelectedSave);
    }

    [Fact]
    public async Task A_profile_with_no_saves_has_no_save_card()
    {
        var dialog = await _rig.DialogAsync(LaunchRig.VanillaTip);

        Assert.False(dialog.HasSaveSection);
        Assert.Null(dialog.SelectedSave);
    }

    [Fact]
    public async Task A_game_with_no_saves_folder_has_no_save_card()
    {
        var dialog = await _rig.DialogAsync(change: p => p.WithoutFolder(RepoRole.External));

        Assert.False(dialog.HasSaveSection);
    }

    [Fact]
    public async Task The_selection_starts_on_the_save_the_preview_names_even_when_it_is_not_the_newest()
    {
        var dialog = await _rig.DialogAsync(LaunchRig.ClearedBlackreachPoint);

        Assert.Equal(SampleScenario.ClearedBlackreach, dialog.SelectedSave!.Entry.Id);
    }

    [Fact]
    public async Task A_dirty_saves_folder_gets_a_block_that_says_how_many_files_and_offers_both_choices()
    {
        var dialog = await _rig.DialogAsync();

        var block = Assert.Single(dialog.UnsavedBlocks);
        Assert.Equal(RepoRole.External, block.Role);
        Assert.Equal("New changes since the last snapshot", block.Title);
        Assert.Equal("3 files in your saves folder changed since the last snapshot of \"Survival test - 5-14-26\". Take a snapshot to keep them there, or discard them.", block.Body);
        Assert.True(block.CanSaveFirst);
        Assert.True(dialog.HasUnsaved);
        Assert.False(dialog.CanSwitch);
        Assert.False(dialog.ShowKeepCallout);
    }

    [Fact]
    public async Task A_large_count_of_changed_files_is_written_with_group_separators()
    {
        var dialog = await _rig.DialogAsync(change: preview => preview.WithFolder(RepoRole.External, saves => saves with { DirtyFileCount = 200_000 }));

        var block = Assert.Single(dialog.UnsavedBlocks);
        Assert.StartsWith("200,000 files in your saves folder changed", block.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_changes_name_the_profile_that_owns_them_when_it_is_not_the_one_being_switched_to()
    {
        var dialog = await _rig.DialogAsync(change: preview => preview.WithFolder(RepoRole.External, saves => saves with { DirtyOwnerProfileId = SampleScenario.ModdedId }));

        var block = Assert.Single(dialog.UnsavedBlocks);
        Assert.Equal("3 files in your saves folder changed since the last snapshot of \"Modded playthrough - 7-2-26\". Take a snapshot to keep them there, or discard them.", block.Body);
        Assert.True(block.CanSaveFirst);
    }

    [Fact]
    public async Task New_changes_that_belong_to_no_profile_point_at_keeping_them_as_a_new_profile()
    {
        var dialog = await _rig.DialogAsync(change: Unclaimed);

        var block = Assert.Single(dialog.UnsavedBlocks);
        Assert.Equal("3 files in your saves folder changed since the last snapshot. Keep them as a new profile, or discard them.", block.Body);
        Assert.False(block.CanSaveFirst);
    }

    [Fact]
    public async Task Two_dirty_folders_get_two_blocks_with_the_game_files_first()
    {
        var dialog = await _rig.DialogAsync(change: BothDirty);

        Assert.Equal([RepoRole.Install, RepoRole.External], dialog.UnsavedBlocks.Select(b => b.Role));
        Assert.StartsWith("12 files in the game's folder changed", dialog.UnsavedBlocks[0].Body);
    }

    [Fact]
    public async Task Nothing_dirty_means_no_blocks_and_switching_is_allowed()
    {
        var dialog = await _rig.DialogAsync(change: Clean);

        Assert.Empty(dialog.UnsavedBlocks);
        Assert.False(dialog.HasUnsaved);
        Assert.True(dialog.CanSwitch);
    }

    [Fact]
    public async Task Save_them_first_saves_that_folder_into_its_profile_and_clears_the_block_when_the_job_succeeds()
    {
        var dialog = await _rig.DialogAsync();
        var block = dialog.UnsavedBlocks[0];

        var run = block.SaveFirstCommand.ExecuteAsync(null);

        var call = Assert.Single(_rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));
        Assert.Equal(SampleScenario.SkyrimId, call.Args[0]);
        Assert.Equal(SampleScenario.SurvivalId, call.Args[1]);
        Assert.Equal($"Before switching to {_survivalName}", call.Args[2]);
        Assert.Equal([RepoRole.External], Assert.IsAssignableFrom<IEnumerable<RepoRole>>(call.Args[3]));
        Assert.True(block.IsBusy);
        Assert.False(dialog.CanSwitch);
        Assert.False(dialog.CanDismiss);
        Assert.False(block.SaveFirstCommand.CanExecute(null));
        Assert.False(block.DiscardCommand.CanExecute(null));

        _rig.Backend.Jobs.Complete(LastCaptureJob());
        await run;

        Assert.Empty(dialog.UnsavedBlocks);
        Assert.True(dialog.CanSwitch);
        Assert.True(dialog.CanDismiss);
    }

    [Fact]
    public async Task Save_them_first_shows_the_save_in_the_block_with_its_progress_and_announces_each_quarter()
    {
        var dialog = await _rig.DialogAsync();
        var block = dialog.UnsavedBlocks[0];
        Assert.True(block.ShowActions);
        Assert.False(block.IsSaving);

        var run = block.SaveFirstCommand.ExecuteAsync(null);

        Assert.True(block.IsSaving);
        Assert.False(block.ShowActions);
        Assert.True(block.IsProgressIndeterminate);
        Assert.Equal("Taking a snapshot of these changes…", block.ProgressText);
        Assert.Equal("Taking a snapshot of these changes…", block.Announcement);

        _rig.Backend.Jobs.ReportProgress(LastCaptureJob(), new JobProgress(FilesDone: 30, FilesTotal: 100));

        Assert.False(block.IsProgressIndeterminate);
        Assert.Equal(30, block.ProgressPercent);
        Assert.Equal("30 of 100 files", block.ProgressText);
        Assert.Equal("25% copied.", block.Announcement);

        _rig.Backend.Jobs.ReportProgress(LastCaptureJob(), new JobProgress(FilesDone: 40, FilesTotal: 100));
        Assert.Equal("25% copied.", block.Announcement);

        _rig.Backend.Jobs.ReportProgress(LastCaptureJob(), new JobProgress(FilesDone: 80, FilesTotal: 100));
        Assert.Equal("75% copied.", block.Announcement);

        _rig.Backend.Jobs.Complete(LastCaptureJob());
        await run;
        Assert.False(block.IsSaving);
    }

    [Fact]
    public async Task A_save_that_has_not_counted_its_files_yet_keeps_the_bar_moving_and_says_so()
    {
        var dialog = await _rig.DialogAsync();
        var block = dialog.UnsavedBlocks[0];
        var run = block.SaveFirstCommand.ExecuteAsync(null);

        _rig.Backend.Jobs.ReportProgress(LastCaptureJob(), new JobProgress(FilesDone: 12));

        Assert.True(block.IsProgressIndeterminate);
        Assert.Equal("Working out how much there is to copy…", block.ProgressText);

        _rig.Backend.Jobs.Complete(LastCaptureJob());
        await run;
    }

    [Fact]
    public async Task A_save_that_reports_bytes_fills_the_bar_by_bytes_and_says_so()
    {
        var dialog = await _rig.DialogAsync();
        var block = dialog.UnsavedBlocks[0];
        var run = block.SaveFirstCommand.ExecuteAsync(null);

        _rig.Backend.Jobs.ReportProgress(LastCaptureJob(), new JobProgress(FilesDone: 0, FilesTotal: 3, BytesDone: 35_000_000_000, BytesTotal: 70_000_000_000));

        Assert.False(block.IsProgressIndeterminate);
        Assert.Equal(50, block.ProgressPercent);
        Assert.Equal("35 GB of 70 GB", block.ProgressText);

        _rig.Backend.Jobs.Complete(LastCaptureJob());
        await run;
    }

    [Fact]
    public async Task A_save_that_fails_gives_the_buttons_back()
    {
        var dialog = await _rig.DialogAsync();
        var block = dialog.UnsavedBlocks[0];
        var run = block.SaveFirstCommand.ExecuteAsync(null);
        Assert.False(block.ShowActions);

        _rig.Backend.Jobs.Fail(LastCaptureJob(), SampleScenario.SavesOutOfRoomError);
        await run;

        Assert.False(block.IsSaving);
        Assert.True(block.ShowActions);
    }

    [Fact]
    public async Task A_save_that_does_not_fully_work_leaves_the_block_with_the_reason()
    {
        var dialog = await _rig.DialogAsync();
        var block = dialog.UnsavedBlocks[0];
        var run = block.SaveFirstCommand.ExecuteAsync(null);

        _rig.Backend.Jobs.Fail(LastCaptureJob(), SampleScenario.SavesOutOfRoomError);
        await run;

        Assert.Single(dialog.UnsavedBlocks);
        Assert.Equal("SavePoint couldn't take a snapshot of these changes. Not enough free disk space to take this snapshot. Free up about 1.2 GB on drive D: and try again.", block.Error);
        Assert.True(block.HasError);
        Assert.False(block.IsBusy);
        Assert.False(dialog.CanSwitch);
        Assert.True(block.SaveFirstCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_partial_save_is_not_treated_as_success()
    {
        var dialog = await _rig.DialogAsync();
        var block = dialog.UnsavedBlocks[0];
        var run = block.SaveFirstCommand.ExecuteAsync(null);

        var partial = new CaptureResult(SampleScenario.SurvivalId, null, [new FolderOutcome(RepoRole.External, SampleScenario.SavesOutOfRoomError), new FolderOutcome(RepoRole.Install)]);
        _rig.Backend.Jobs.Complete(LastCaptureJob(), partial);
        await run;

        Assert.Single(dialog.UnsavedBlocks);
        Assert.True(block.HasError);
    }

    [Fact]
    public async Task A_save_that_cannot_start_shows_the_reason_in_the_block()
    {
        var dialog = await _rig.DialogAsync();
        _rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.CaptureAsync), SampleScenario.SavesOutOfRoomError);

        await dialog.UnsavedBlocks[0].SaveFirstCommand.ExecuteAsync(null);

        Assert.StartsWith("SavePoint couldn't take a snapshot of these changes.", dialog.UnsavedBlocks[0].Error);
        Assert.False(dialog.UnsavedBlocks[0].IsBusy);
    }

    [Fact]
    public async Task Discard_them_asks_a_second_time_in_the_destructive_style()
    {
        var dialog = await _rig.DialogAsync();
        _rig.Dialogs.AnswerConfirm(false);

        await dialog.UnsavedBlocks[0].DiscardCommand.ExecuteAsync(null);

        var request = Assert.Single(_rig.Dialogs.Confirmations);
        Assert.Equal("Discard these changes?", request.Title);
        Assert.Equal(
            $"The saves folder ({SampleScenario.SkyrimSaves}) goes back to its last snapshot. Progress you saved in the game since then is lost. 2 new files are deleted. 1 changed file is put back as it was. This can't be undone.",
            request.Message);
        Assert.Equal("Discard them, permanently", request.ConfirmLabel);
        Assert.True(request.Destructive);
        Assert.Empty(_rig.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.DiscardChangesAsync)));
        Assert.Single(dialog.UnsavedBlocks);
    }

    [Fact]
    public async Task Confirming_the_discard_throws_the_changes_away_and_frees_the_switch()
    {
        var dialog = await _rig.DialogAsync();
        _rig.Dialogs.AnswerConfirm(true);

        await dialog.UnsavedBlocks[0].DiscardCommand.ExecuteAsync(null);

        var call = Assert.Single(_rig.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.DiscardChangesAsync)));
        Assert.Equal([SampleScenario.SkyrimId, RepoRole.External], call.Args);
        Assert.Empty(dialog.UnsavedBlocks);
        Assert.True(dialog.CanSwitch);
    }

    [Fact]
    public async Task A_discard_that_fails_keeps_the_block_and_says_why()
    {
        var dialog = await _rig.DialogAsync();
        _rig.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.DiscardChangesAsync), SampleScenario.FileInUseError);

        await dialog.UnsavedBlocks[0].DiscardCommand.ExecuteAsync(null);

        var block = Assert.Single(dialog.UnsavedBlocks);
        Assert.StartsWith("SavePoint couldn't discard these changes.", block.Error);
        Assert.False(block.IsBusy);
    }

    [Fact]
    public async Task Changes_that_belong_to_no_profile_get_a_block_without_save_first_and_the_keep_callout()
    {
        var dialog = await _rig.DialogAsync(change: Unclaimed);

        Assert.False(dialog.UnsavedBlocks[0].CanSaveFirst);
        Assert.True(dialog.ShowKeepCallout);
        Assert.Equal(
            "These changes belong to no profile. They came from an older snapshot or a deleted profile.",
            dialog.KeepText);
    }

    [Fact]
    public async Task One_callout_serves_several_unclaimed_folders()
    {
        var dialog = await _rig.DialogAsync(change: p => Unclaimed(BothDirty(p)).WithFolder(p.Install with { Dirty = true, DirtyFileCount = 4, DirtyOwnerProfileId = null }));

        Assert.Equal(2, dialog.UnsavedBlocks.Count);
        Assert.True(dialog.ShowKeepCallout);
    }

    [Fact]
    public async Task Discarding_the_only_unclaimed_folder_hides_the_callout()
    {
        var dialog = await _rig.DialogAsync(change: Unclaimed);
        _rig.Dialogs.AnswerConfirm(true);

        await dialog.UnsavedBlocks[0].DiscardCommand.ExecuteAsync(null);

        Assert.False(dialog.ShowKeepCallout);
    }

    [Fact]
    public async Task Keeping_as_a_new_profile_asks_for_a_name_starting_from_a_dated_one()
    {
        var dialog = await _rig.DialogAsync(change: Unclaimed);

        var open = dialog.KeepCommand.ExecuteAsync(null);

        var keep = Assert.IsType<KeepAsNewProfileDialogViewModel>(Assert.Single(_rig.Dialogs.Shown));
        Assert.Equal("Keep as a new profile", keep.Title);
        Assert.Equal("SavePoint takes a snapshot of your game folders as they are now and makes it a new profile.", keep.Body);
        Assert.Equal("Kept changes - Jul 8, 2026", keep.Name);
        keep.CancelCommand.Execute(null);
        await open;
    }

    [Fact]
    public async Task A_name_made_only_of_spaces_cannot_be_created()
    {
        var dialog = await _rig.DialogAsync(change: Unclaimed);
        var open = dialog.KeepCommand.ExecuteAsync(null);
        var keep = Assert.IsType<KeepAsNewProfileDialogViewModel>(Assert.Single(_rig.Dialogs.Shown));

        keep.Name = "   ";

        Assert.False(keep.CreateCommand.CanExecute(null));
        keep.CancelCommand.Execute(null);
        await open;
    }

    [Fact]
    public async Task Creating_the_profile_saves_both_folders_and_clears_every_block_once_the_job_succeeds()
    {
        var dialog = await _rig.DialogAsync(change: p => Unclaimed(BothDirty(p)));
        var open = dialog.KeepCommand.ExecuteAsync(null);
        var keep = Assert.IsType<KeepAsNewProfileDialogViewModel>(Assert.Single(_rig.Dialogs.Shown));
        keep.Name = "  Rescued session  ";

        var create = keep.CreateCommand.ExecuteAsync(null);

        var call = Assert.Single(_rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CreateAsync)));
        Assert.Equal([SampleScenario.SkyrimId, "Rescued session", null, "Rescued session"], call.Args.Take(4));
        // A profile kept from a session starts no folder empty.
        Assert.Empty((IEnumerable<RepoRole>?)call.Args[4] ?? []);
        Assert.True(keep.IsWorking);
        Assert.False(keep.CanDismiss);
        Assert.False(keep.CreateCommand.CanExecute(null));

        _rig.Backend.Jobs.Complete(LastCaptureJob());
        await create;
        await open;

        Assert.True(keep.IsClosed);
        Assert.Empty(dialog.UnsavedBlocks);
        Assert.False(dialog.ShowKeepCallout);
        Assert.True(dialog.CanSwitch);
    }

    [Fact]
    public async Task A_failed_keep_stays_in_the_dialog_with_the_reason_and_leaves_the_blocks_alone()
    {
        var dialog = await _rig.DialogAsync(change: Unclaimed);
        var open = dialog.KeepCommand.ExecuteAsync(null);
        var keep = Assert.IsType<KeepAsNewProfileDialogViewModel>(Assert.Single(_rig.Dialogs.Shown));
        var create = keep.CreateCommand.ExecuteAsync(null);

        _rig.Backend.Jobs.Fail(LastCaptureJob(), SampleScenario.SavesOutOfRoomError);
        await create;

        Assert.False(keep.IsClosed);
        Assert.False(keep.IsWorking);
        Assert.True(keep.CanDismiss);
        Assert.Equal("SavePoint couldn't create the profile. Not enough free disk space to take this snapshot. Free up about 1.2 GB on drive D: and try again.", keep.Error);
        Assert.Single(dialog.UnsavedBlocks);

        keep.CancelCommand.Execute(null);
        await open;
        Assert.True(dialog.ShowKeepCallout);
    }

    [Fact]
    public async Task A_keep_that_only_half_worked_says_so_and_keeps_the_blocks()
    {
        var dialog = await _rig.DialogAsync(change: Unclaimed);
        var open = dialog.KeepCommand.ExecuteAsync(null);
        var keep = Assert.IsType<KeepAsNewProfileDialogViewModel>(Assert.Single(_rig.Dialogs.Shown));
        var create = keep.CreateCommand.ExecuteAsync(null);

        var partial = new CaptureResult("profile", null, [new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.External, SampleScenario.SavesOutOfRoomError)]);
        _rig.Backend.Jobs.Complete(LastCaptureJob(), partial);
        await create;

        Assert.False(keep.IsClosed);
        Assert.StartsWith("Only part of that was kept.", keep.Error);
        Assert.Single(dialog.UnsavedBlocks);

        keep.CancelCommand.Execute(null);
        await open;
    }

    [Fact]
    public async Task A_keep_that_cannot_start_shows_the_reason()
    {
        var dialog = await _rig.DialogAsync(change: Unclaimed);
        var open = dialog.KeepCommand.ExecuteAsync(null);
        var keep = Assert.IsType<KeepAsNewProfileDialogViewModel>(Assert.Single(_rig.Dialogs.Shown));
        _rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.CreateAsync), SampleScenario.FolderGoneError);

        await keep.CreateCommand.ExecuteAsync(null);

        Assert.StartsWith("SavePoint couldn't create the profile.", keep.Error);
        Assert.False(keep.IsWorking);
        keep.CancelCommand.Execute(null);
        await open;
    }

    [Fact]
    public async Task Backing_out_of_the_name_prompt_changes_nothing()
    {
        var dialog = await _rig.DialogAsync(change: Unclaimed);
        var open = dialog.KeepCommand.ExecuteAsync(null);
        var keep = Assert.IsType<KeepAsNewProfileDialogViewModel>(Assert.Single(_rig.Dialogs.Shown));

        keep.RequestDismiss();
        await open;

        Assert.True(keep.IsClosed);
        Assert.Empty(_rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CreateAsync)));
        Assert.Single(dialog.UnsavedBlocks);
    }

    [Fact]
    public async Task Switch_and_play_answers_with_the_original_target_when_the_selection_is_left_alone()
    {
        var dialog = await _rig.DialogAsync(change: Clean);

        dialog.SwitchCommand.Execute(null);

        Assert.True(dialog.IsClosed);
        Assert.True(dialog.IsClosed);
        Assert.Equal(LaunchRig.SurvivalTip, await dialog.Result);
    }

    [Fact]
    public async Task Choosing_an_older_save_answers_with_that_save()
    {
        var dialog = await _rig.DialogAsync(change: Clean);
        dialog.SelectedSave = dialog.SaveOptions[1];

        dialog.SwitchCommand.Execute(null);

        Assert.True(dialog.IsClosed);

        Assert.Equal(new LaunchTarget.Save(SampleScenario.SurvivalId, SampleScenario.FirstNight), await dialog.Result);
    }

    [Fact]
    public async Task Choosing_the_newest_save_again_answers_with_the_tip_so_the_profile_stays_on_its_latest_point()
    {
        var dialog = await _rig.DialogAsync(change: Clean);
        dialog.SelectedSave = dialog.SaveOptions[1];
        dialog.SelectedSave = dialog.SaveOptions[0];

        dialog.SwitchCommand.Execute(null);

        Assert.True(dialog.IsClosed);

        Assert.Equal(LaunchRig.SurvivalTip, await dialog.Result);
    }

    [Fact]
    public async Task A_go_back_target_keeps_its_point_unless_another_save_is_chosen()
    {
        var kept = await _rig.DialogAsync(LaunchRig.ClearedBlackreachPoint);
        kept.SwitchCommand.Execute(null);
        Assert.True(kept.IsClosed);
        Assert.Equal(LaunchRig.ClearedBlackreachPoint, await kept.Result);

        var changed = await _rig.DialogAsync(LaunchRig.ClearedBlackreachPoint);
        changed.SelectedSave = changed.SaveOptions[2];
        changed.SwitchCommand.Execute(null);
        Assert.True(changed.IsClosed);
        Assert.Equal(new LaunchTarget.Save(SampleScenario.ModdedId, SampleScenario.FreshStart), await changed.Result);
    }

    [Fact]
    public async Task Switch_and_play_does_nothing_while_something_unsaved_remains()
    {
        var dialog = await _rig.DialogAsync();

        Assert.False(dialog.SwitchCommand.CanExecute(null));
        dialog.SwitchCommand.Execute(null);

        Assert.False(dialog.IsClosed);
    }

    [Fact]
    public async Task Switch_and_play_turns_on_once_the_last_block_is_resolved()
    {
        var dialog = await _rig.DialogAsync();
        var changes = new List<string?>();
        dialog.SwitchCommand.CanExecuteChanged += (_, _) => changes.Add("changed");
        _rig.Dialogs.AnswerConfirm(true);

        await dialog.UnsavedBlocks[0].DiscardCommand.ExecuteAsync(null);

        Assert.NotEmpty(changes);
        Assert.True(dialog.SwitchCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancel_and_escape_answer_with_nothing()
    {
        var cancelled = await _rig.DialogAsync();
        cancelled.CancelCommand.Execute(null);
        Assert.True(cancelled.IsClosed);
        Assert.Null(await cancelled.Result);

        var escaped = await _rig.DialogAsync();
        escaped.RequestDismiss();
        Assert.True(escaped.IsClosed);
        Assert.Null(await escaped.Result);
    }

    [Fact]
    public async Task The_dont_ask_again_box_starts_from_the_saved_preference()
    {
        Assert.False((await _rig.DialogAsync(skipConfirm: false)).DontAskAgain);
        Assert.True((await _rig.DialogAsync(skipConfirm: true)).DontAskAgain);
    }

    [Fact]
    public async Task Ticking_the_dont_ask_again_box_saves_the_preference_and_keeps_the_other_settings()
    {
        var dialog = await _rig.DialogAsync();
        var saved = new List<AppSettings>();
        dialog.SettingsSaved += (_, settings) => saved.Add(settings);

        dialog.DontAskAgain = true;

        var settings = await _rig.SettingsAsync();
        Assert.True(settings.SkipConfirmWhenOnlySaveChanges);
        Assert.Equal(SampleScenario.Settings.Theme, settings.Theme);
        Assert.Equal(SampleScenario.Settings.StorageRoot, settings.StorageRoot);
        Assert.Equal([settings], saved);

        dialog.DontAskAgain = false;
        Assert.False((await _rig.SettingsAsync()).SkipConfirmWhenOnlySaveChanges);
    }

    [Fact]
    public async Task A_preference_that_cannot_be_saved_puts_the_box_back_and_tells_the_user()
    {
        var dialog = await _rig.DialogAsync();
        _rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.SaveAsync), new ErrorInfo("error.unknown", new Dictionary<string, object?>(), string.Empty));

        dialog.DontAskAgain = true;

        Assert.False(dialog.DontAskAgain);
        Assert.False((await _rig.SettingsAsync()).SkipConfirmWhenOnlySaveChanges);
        var toast = Assert.Single(_rig.Toasts.Toasts);
        Assert.Equal("Something went wrong. Try again.", toast.Text);
        Assert.True(toast.IsError);
        Assert.Equal(ToastKind.Error, toast.Message.Kind);
    }

    [Fact]
    public async Task A_preference_that_cannot_be_read_puts_the_box_back_and_tells_the_user()
    {
        var dialog = await _rig.DialogAsync();
        _rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetAsync), new ErrorInfo("error.unknown", new Dictionary<string, object?>(), string.Empty));

        dialog.DontAskAgain = true;

        Assert.False(dialog.DontAskAgain);
        Assert.Single(_rig.Toasts.Toasts);
    }

    [Fact]
    public async Task Going_back_to_an_older_point_says_so()
    {
        var older = await _rig.DialogAsync(LaunchRig.ClearedBlackreachPoint);
        var current = await _rig.DialogAsync();

        Assert.True(older.IsOlderPoint);
        Assert.Equal(
            "This is an older snapshot of \"Modded playthrough - 7-2-26\". After you play, keep the session as a new profile, or continue \"Modded playthrough - 7-2-26\" from it, which removes its newer snapshots.",
            older.OlderPointText);
        Assert.False(current.IsOlderPoint);
    }

    private static LaunchPreview StoppedSwitchInInstall(LaunchPreview preview, Func<FolderLaunchState, FolderLaunchState> change) =>
        Clean(preview).WithFolder(change(preview.Install with { Dirty = false, UnfinishedSwitch = true, UnfinishedSwitchTarget = LaunchRig.ModdedTip }));

    [Fact]
    public async Task A_stopped_switch_that_unsaved_changes_stand_in_the_way_of_says_why_and_offers_only_discarding()
    {
        var dialog = await _rig.DialogAsync(change: preview => StoppedSwitchInInstall(preview, install => install with
        {
            Dirty = true,
            DirtyFileCount = 3,
            DirtyNewFileCount = 3,
            UnfinishedSwitchTarget = null,
            ChangesOutsideSwitchCount = 3,
        }));

        var block = Assert.Single(dialog.UnsavedBlocks);
        Assert.True(block.IsUnfinishedSwitch);
        Assert.False(block.CanSaveFirst);
        Assert.False(block.CanFinishSwitch);
        Assert.True(block.DiscardCommand.CanExecute(null));
        Assert.Equal("The last switch didn't finish", block.Title);
        Assert.Equal(
            "The game install folder has files from two snapshots, and 3 files that are new or changed and not in any snapshot. SavePoint can't tell them apart from the last switch's own files, so it won't finish the switch until they're gone. Discard puts the folder back to where the switch started, and removes new files too.",
            block.Body);
        Assert.False(dialog.ShowKeepCallout);
    }

    [Fact]
    public async Task A_stopped_switch_whose_folder_could_not_be_checked_says_so_and_offers_only_discarding()
    {
        var dialog = await _rig.DialogAsync(change: preview => StoppedSwitchInInstall(preview, install => install with
        {
            Dirty = true,
            DirtyFileCount = 0,
            UnfinishedSwitchTarget = null,
            StoppedSwitchUnchecked = true,
        }));

        var block = Assert.Single(dialog.UnsavedBlocks);
        Assert.True(block.IsUnfinishedSwitch);
        Assert.False(block.CanFinishSwitch);
        Assert.True(block.DiscardCommand.CanExecute(null));
        Assert.Equal(
            "The game install folder has files from two snapshots, and SavePoint couldn't check it for changes that aren't in a snapshot, so it won't finish the last switch. Discard puts the folder back to where the switch started, and removes new files too.",
            block.Body);
    }

    [Fact]
    public async Task One_change_in_the_way_of_a_stopped_switch_is_said_in_the_singular()
    {
        var dialog = await _rig.DialogAsync(change: preview => StoppedSwitchInInstall(preview, install => install with
        {
            Dirty = true,
            DirtyFileCount = 1,
            UnfinishedSwitchTarget = null,
            ChangesOutsideSwitchCount = 1,
        }));

        Assert.Equal(
            "The game install folder has files from two snapshots, and 1 file that's new or changed and not in any snapshot. SavePoint can't tell it apart from the last switch's own files, so it won't finish the switch until it's gone. Discard puts the folder back to where the switch started, and removes new files too.",
            Assert.Single(dialog.UnsavedBlocks).Body);
    }

    [Fact]
    public async Task A_stopped_switch_that_replaces_files_says_how_many_and_that_it_includes_changes_made_since()
    {
        var dialog = await _rig.DialogAsync(change: preview => StoppedSwitchInInstall(preview, install => install with { ReplacedFileCount = 12 }));

        Assert.Empty(dialog.UnsavedBlocks);
        Assert.True(dialog.HasReplacedNote);
        Assert.Equal(
            "12 files in the game's folder that the last switch was changing hold something that's in neither snapshot. Finishing the last switch replaces them with the copies from the snapshot it was heading to, including any change you made since.",
            dialog.ReplacedNote);
    }

    [Fact]
    public async Task One_replaced_file_is_said_in_the_singular()
    {
        var dialog = await _rig.DialogAsync(change: preview => StoppedSwitchInInstall(preview, install => install with { ReplacedFileCount = 1 }));

        Assert.Equal(
            "1 file in the game's folder that the last switch was changing holds something that's in neither snapshot. Finishing the last switch replaces it with the copy from the snapshot it was heading to, including any change you made since.",
            dialog.ReplacedNote);
    }

    [Fact]
    public async Task The_saves_folder_names_the_files_it_replaces_and_counts_the_rest()
    {
        var dialog = await _rig.DialogAsync(change: preview => Clean(preview).WithFolder(preview.Folder(RepoRole.External)! with
        {
            Dirty = false,
            UnfinishedSwitch = true,
            UnfinishedSwitchTarget = LaunchRig.ModdedTip,
            ReplacedFileCount = 7,
            ReplacedFileNames = ["slot1.sav", "slot2.sav", "slot3.sav", "slot4.sav", "slot5.sav"],
        }));

        Assert.Equal(
            "7 files in your saves folder that the last switch was changing (slot1.sav, slot2.sav, slot3.sav, slot4.sav, slot5.sav and 2 more) hold something that's in neither snapshot. Finishing the last switch replaces them with the copies from the snapshot it was heading to, including any change you made since.",
            dialog.ReplacedNote);
    }

    [Fact]
    public async Task Every_replaced_save_is_named_when_there_are_no_more_than_the_names_carried()
    {
        var dialog = await _rig.DialogAsync(change: preview => Clean(preview).WithFolder(preview.Folder(RepoRole.External)! with
        {
            Dirty = false,
            UnfinishedSwitch = true,
            UnfinishedSwitchTarget = LaunchRig.ModdedTip,
            ReplacedFileCount = 2,
            ReplacedFileNames = ["slot1.sav", "slot2.sav"],
        }));

        Assert.Contains("that the last switch was changing (slot1.sav, slot2.sav) hold something", dialog.ReplacedNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Both_folders_replacing_files_get_a_line_each_and_nothing_replaced_leaves_no_note()
    {
        var both = await _rig.DialogAsync(change: preview => Clean(preview)
            .WithFolder(preview.Install with { Dirty = false, UnfinishedSwitch = true, UnfinishedSwitchTarget = LaunchRig.ModdedTip, ReplacedFileCount = 2 })
            .WithFolder(preview.Folder(RepoRole.External)! with { Dirty = false, UnfinishedSwitch = true, UnfinishedSwitchTarget = LaunchRig.ModdedTip, ReplacedFileCount = 1, ReplacedFileNames = ["slot1.sav"] }));
        var none = await _rig.DialogAsync(change: preview => StoppedSwitchInInstall(preview, install => install));

        Assert.Equal(2, both.ReplacedNote.Split('\n').Length);
        Assert.False(none.HasReplacedNote);
        Assert.Empty(none.ReplacedNote);
    }

    [Fact]
    public async Task A_folder_with_no_stopped_switch_gets_no_replacement_note()
    {
        var dialog = await _rig.DialogAsync(change: preview => Clean(preview).WithFolder(preview.Install with { ReplacedFileCount = 4 }));

        Assert.False(dialog.HasReplacedNote);
    }
}
