using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Games;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Games;

public class GamesScreenActionsTests
{
    private static FakeBackend WithDeletePreview(ProfileDeletePreview preview) =>
        new(ScenarioBuilder.Sample().WithDeletePreview(preview));

    // A fourth profile of Skyrim that has no snapshot yet, so its first one is whatever the folders hold.
    private static FakeBackend WithFirstSnapshotProfile() => new(ScenarioBuilder.Sample().WithProfile(
        new Profile("profile-fresh", SampleScenario.SkyrimId, "Fresh profile", "profile/profile-fresh"),
        new ProfileRowStats("profile-fresh", false, [], 0, null, null, null, null, false, SnapshotNowAvailability.FirstSnapshot)));

    private static ProfileDeletePreview Preview(string profileId, int points) =>
        new(profileId, points, [new FolderPointLoss(RepoRole.Install, points, null)], null);

    // ---- Play

    [Fact]
    public async Task Play_starts_the_launch_flow_on_the_profiles_latest_points()
    {
        var rig = await GamesRig.StartAsync();

        await rig.Screen.Profiles[1].PlayCommand.ExecuteAsync(null);

        var begun = Assert.Single(rig.LaunchFlow!.Begun);
        Assert.Equal(SampleScenario.SkyrimId, begun.GameId);
        Assert.Equal(new LaunchTarget.ProfileTip(SampleScenario.VanillaId), begun.Target);
    }

    [Fact]
    public async Task Play_does_nothing_while_the_game_has_a_session()
    {
        var rig = await GamesRig.StartAsync();
        rig.LaunchFlow!.SetActive(SampleScenario.SkyrimId, true);

        await rig.Screen.Profiles[0].PlayCommand.ExecuteAsync(null);

        Assert.Empty(rig.LaunchFlow.Begun);
    }

    // ---- Switch to this profile

    [Fact]
    public async Task Switch_asks_the_launch_flow_to_load_the_profiles_latest_points_without_playing()
    {
        var rig = await GamesRig.StartAsync();

        await rig.Screen.Profiles[1].SwitchCommand.ExecuteAsync(null);

        var begun = Assert.Single(rig.LaunchFlow!.BegunSwitches);
        Assert.Equal(SampleScenario.SkyrimId, begun.GameId);
        Assert.Equal(new LaunchTarget.ProfileTip(SampleScenario.VanillaId), begun.Target);
        Assert.Empty(rig.LaunchFlow.Begun);
    }

    [Fact]
    public async Task Switch_does_nothing_while_the_game_has_a_session()
    {
        var rig = await GamesRig.StartAsync();
        rig.LaunchFlow!.SetActive(SampleScenario.SkyrimId, true);

        await rig.Screen.Profiles[0].SwitchCommand.ExecuteAsync(null);

        Assert.False(rig.Screen.Profiles[0].SwitchCommand.CanExecute(null));
        Assert.Empty(rig.LaunchFlow.BegunSwitches);
    }

    // ---- Take a snapshot

    [Fact]
    public async Task Take_a_snapshot_now_asks_the_api_to_capture_the_loaded_profile_under_a_dated_name()
    {
        var rig = await GamesRig.StartAsync();

        await rig.Screen.Profiles[0].TakeSnapshotCommand.ExecuteAsync(null);

        var call = Assert.Single(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));
        Assert.Equal([SampleScenario.SkyrimId, SampleScenario.ModdedId, "Snapshot - Jul 8, 2026", null], call.Args);
        Assert.Empty(rig.Dialogs.Confirmations);
    }

    [Fact]
    public async Task A_snapshot_the_api_refuses_to_start_is_shown_as_a_toast()
    {
        var rig = await GamesRig.StartAsync();
        rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.CaptureAsync), SampleScenario.SavesOutOfRoomError);

        await rig.Screen.Profiles[0].TakeSnapshotCommand.ExecuteAsync(null);

        Assert.Single(rig.ToastTexts);
    }

    [Fact]
    public async Task A_profile_the_folders_do_not_hold_offers_no_snapshot_and_says_why_in_its_place()
    {
        var rig = await GamesRig.StartAsync();

        var loaded = rig.Screen.Profiles[0];
        var other = rig.Screen.Profiles[1];

        Assert.True(loaded.CanOfferSnapshot);
        Assert.False(loaded.SnapshotBlocked);
        Assert.False(other.CanOfferSnapshot);
        Assert.True(other.SnapshotBlocked);
        Assert.Equal("Take a snapshot (load this profile first)", other.SnapshotBlockedText);
        Assert.Equal("Load this profile first: switch to it, play it, or go back to its latest snapshot.", other.SnapshotBlockedTip);
        Assert.False(other.TakeSnapshotCommand.CanExecute(null));

        await other.TakeSnapshotCommand.ExecuteAsync(null);

        Assert.Empty(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));
    }

    [Fact]
    public async Task A_profile_with_no_snapshot_yet_asks_first_and_says_its_first_snapshot_is_what_the_folders_hold_now()
    {
        var rig = await GamesRig.StartAsync(WithFirstSnapshotProfile());
        var fresh = rig.Screen.Profiles[3];
        rig.Dialogs.AnswerConfirm(true);

        Assert.Equal(SnapshotNowAvailability.FirstSnapshot, fresh.SnapshotNow);
        Assert.True(fresh.CanOfferSnapshot);
        await fresh.TakeSnapshotCommand.ExecuteAsync(null);

        var question = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal("Take the first snapshot of Fresh profile?", question.Title);
        Assert.Equal("A first snapshot copies what the folder holds right now, even if that is another profile's files. The folder then belongs to Fresh profile.", question.Message);
        Assert.Equal("Take the snapshot", question.ConfirmLabel);
        var call = Assert.Single(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));
        Assert.Equal([SampleScenario.SkyrimId, "profile-fresh", "Snapshot - Jul 8, 2026", null], call.Args);
    }

    [Fact]
    public async Task Declining_the_first_snapshot_takes_nothing()
    {
        var rig = await GamesRig.StartAsync(WithFirstSnapshotProfile());
        rig.Dialogs.AnswerConfirm(false);

        await rig.Screen.Profiles[3].TakeSnapshotCommand.ExecuteAsync(null);

        Assert.Single(rig.Dialogs.Confirmations);
        Assert.Empty(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));
    }

    [Fact]
    public async Task A_refusal_for_a_profile_that_is_not_loaded_offers_to_switch_to_it()
    {
        var rig = await GamesRig.StartAsync();
        rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.CaptureAsync), ErrorInfo.From(new ProfileNotLoadedException(SampleScenario.ModdedId, RepoRole.External)));
        rig.Dialogs.AnswerConfirm(true);

        await rig.Screen.Profiles[0].TakeSnapshotCommand.ExecuteAsync(null);

        var offer = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal("This profile isn't loaded", offer.Title);
        Assert.Equal(
            "The saves folder doesn't have this profile's files in it, and a snapshot copies whatever is there now. Switch to this profile, or play it, then take a snapshot.",
            offer.Message);
        Assert.Equal("Switch to this profile", offer.ConfirmLabel);
        Assert.Empty(rig.ToastTexts);
        var begun = Assert.Single(rig.LaunchFlow!.BegunSwitches);
        Assert.Equal(new LaunchTarget.ProfileTip(SampleScenario.ModdedId), begun.Target);
        Assert.Empty(rig.LaunchFlow.Begun);
    }

    [Fact]
    public async Task Declining_the_offer_to_switch_leaves_things_as_they_are()
    {
        var rig = await GamesRig.StartAsync();
        rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.CaptureAsync), ErrorInfo.From(new ProfileNotLoadedException(SampleScenario.ModdedId, RepoRole.External)));
        rig.Dialogs.AnswerConfirm(false);

        await rig.Screen.Profiles[0].TakeSnapshotCommand.ExecuteAsync(null);

        Assert.Single(rig.Dialogs.Confirmations);
        Assert.Empty(rig.LaunchFlow!.BegunSwitches);
    }

    [Fact]
    public async Task Take_a_snapshot_now_does_nothing_while_the_game_has_a_session()
    {
        var rig = await GamesRig.StartAsync();
        rig.LaunchFlow!.SetActive(SampleScenario.SkyrimId, true);

        Assert.False(rig.Screen.Profiles[0].TakeSnapshotCommand.CanExecute(null));
        await rig.Screen.Profiles[0].TakeSnapshotCommand.ExecuteAsync(null);

        Assert.Empty(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));
    }

    // ---- Take a snapshot from the header

    [Fact]
    public async Task The_header_snapshot_button_captures_the_loaded_profile()
    {
        var rig = await GamesRig.StartAsync();

        Assert.True(rig.Screen.HasLoadedProfile);
        Assert.Equal("Take a snapshot", rig.Screen.TakeSnapshotText);
        await rig.Screen.TakeSnapshotCommand.ExecuteAsync(null);

        var call = Assert.Single(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));
        Assert.Equal([SampleScenario.SkyrimId, SampleScenario.ModdedId, "Snapshot - Jul 8, 2026", null], call.Args);
    }

    [Fact]
    public async Task The_header_snapshot_button_is_off_while_the_game_has_a_session_as_the_menu_entry_is()
    {
        var rig = await GamesRig.StartAsync();
        rig.LaunchFlow!.SetActive(SampleScenario.SkyrimId, true);

        Assert.False(rig.Screen.TakeSnapshotCommand.CanExecute(null));
        await rig.Screen.TakeSnapshotCommand.ExecuteAsync(null);
        Assert.Empty(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));

        rig.LaunchFlow.SetActive(SampleScenario.SkyrimId, false);

        Assert.True(rig.Screen.TakeSnapshotCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_header_snapshot_button_is_hidden_when_no_profile_is_loaded()
    {
        var rig = await GamesRig.StartAsync();
        rig.Backend.State.RowStats[SampleScenario.ModdedId] = rig.Backend.State.RowStats[SampleScenario.ModdedId] with { SnapshotNow = SnapshotNowAvailability.NotLoaded };

        await rig.Screen.ReloadAsync();

        Assert.False(rig.Screen.HasLoadedProfile);
        Assert.False(rig.Screen.TakeSnapshotCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_header_snapshot_button_follows_the_loaded_profile_even_when_it_is_not_the_first_row()
    {
        var rig = await GamesRig.StartAsync();
        rig.Backend.State.RowStats[SampleScenario.ModdedId] = rig.Backend.State.RowStats[SampleScenario.ModdedId] with { SnapshotNow = SnapshotNowAvailability.NotLoaded };
        rig.Backend.State.RowStats[SampleScenario.VanillaId] = rig.Backend.State.RowStats[SampleScenario.VanillaId] with { SnapshotNow = SnapshotNowAvailability.Available };
        await rig.Screen.ReloadAsync();

        await rig.Screen.TakeSnapshotCommand.ExecuteAsync(null);

        var call = Assert.Single(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));
        Assert.Equal(SampleScenario.VanillaId, call.Args[1]);
    }

    [Fact]
    public async Task The_header_snapshot_button_on_a_profile_with_no_snapshot_yet_asks_first_as_the_menu_entry_does()
    {
        var rig = await GamesRig.StartAsync(WithFirstSnapshotProfile());
        rig.Backend.State.RowStats[SampleScenario.ModdedId] = rig.Backend.State.RowStats[SampleScenario.ModdedId] with { SnapshotNow = SnapshotNowAvailability.NotLoaded };
        await rig.Screen.ReloadAsync();
        rig.Dialogs.AnswerConfirm(false);

        Assert.True(rig.Screen.HasLoadedProfile);
        await rig.Screen.TakeSnapshotCommand.ExecuteAsync(null);

        var asked = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal("Take the first snapshot of Fresh profile?", asked.Title);
        Assert.Empty(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));

        rig.Dialogs.AnswerConfirm(true);
        await rig.Screen.TakeSnapshotCommand.ExecuteAsync(null);

        var call = Assert.Single(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));
        Assert.Equal("profile-fresh", call.Args[1]);
    }

    [Fact]
    public async Task The_header_snapshot_button_stays_hidden_when_two_profiles_have_no_snapshot_yet_and_none_is_loaded()
    {
        var other = new Profile("profile-other", SampleScenario.SkyrimId, "Other profile", "profile/profile-other");
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithProfile(
            other,
            new ProfileRowStats(other.Id, false, [], 0, null, null, null, null, false, SnapshotNowAvailability.FirstSnapshot)));
        var rig = await GamesRig.StartAsync(backend);
        rig.Backend.State.RowStats[SampleScenario.ModdedId] = rig.Backend.State.RowStats[SampleScenario.ModdedId] with { SnapshotNow = SnapshotNowAvailability.FirstSnapshot };

        await rig.Screen.ReloadAsync();

        Assert.False(rig.Screen.HasLoadedProfile);
    }

    [Fact]
    public async Task A_snapshot_still_running_for_the_game_turns_off_the_header_button_and_the_menu_entry_until_it_ends()
    {
        var rig = await GamesRig.StartAsync();

        await rig.Screen.TakeSnapshotCommand.ExecuteAsync(null);

        Assert.False(rig.Screen.TakeSnapshotCommand.CanExecute(null));
        Assert.False(rig.Screen.Profiles[0].TakeSnapshotCommand.CanExecute(null));
        await rig.Screen.TakeSnapshotCommand.ExecuteAsync(null);
        await rig.Screen.Profiles[0].TakeSnapshotCommand.ExecuteAsync(null);
        Assert.Single(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));

        rig.Backend.Jobs.Complete(rig.Backend.Jobs.LastStarted(JobKind.Capture).JobId);

        Assert.True(rig.Screen.TakeSnapshotCommand.CanExecute(null));
        Assert.True(rig.Screen.Profiles[0].TakeSnapshotCommand.CanExecute(null));
    }

    [Fact]
    public async Task Starting_the_rows_snapshot_turns_off_the_header_button_until_the_snapshot_ends()
    {
        var rig = await GamesRig.StartAsync();

        await rig.Screen.Profiles[0].TakeSnapshotCommand.ExecuteAsync(null);

        Assert.False(rig.Screen.TakeSnapshotCommand.CanExecute(null));
        await rig.Screen.TakeSnapshotCommand.ExecuteAsync(null);
        Assert.Single(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));

        rig.Backend.Jobs.Complete(rig.Backend.Jobs.LastStarted(JobKind.Capture).JobId);

        Assert.True(rig.Screen.TakeSnapshotCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_header_button_is_off_and_says_so_from_the_moment_the_rows_snapshot_is_asked_for()
    {
        var backend = FakeBackend.Sample();
        var gated = new GatedProfilesApi(backend.Profiles);
        var rig = await GamesRig.StartAsync(backend, gated);
        var release = gated.HoldCapture();
        var changed = 0;
        rig.Screen.TakeSnapshotCommand.CanExecuteChanged += (_, _) => changed++;

        var running = rig.Screen.Profiles[0].TakeSnapshotCommand.ExecuteAsync(null);

        Assert.False(rig.Screen.TakeSnapshotCommand.CanExecute(null));
        Assert.True(changed > 0);
        release();
        await running;
        Assert.False(rig.Screen.TakeSnapshotCommand.CanExecute(null));

        rig.Backend.Jobs.Complete(rig.Backend.Jobs.LastStarted(JobKind.Capture).JobId);

        Assert.True(rig.Screen.TakeSnapshotCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_snapshot_running_for_another_game_leaves_this_games_snapshot_button_on()
    {
        var rig = await GamesRig.StartAsync();
        rig.Backend.Jobs.Start(JobKind.Capture, SampleScenario.SatisfactoryId, null);

        Assert.True(rig.Screen.TakeSnapshotCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_header_button_tells_the_screen_when_it_turns_on_or_off()
    {
        var rig = await GamesRig.StartAsync();
        var changed = 0;
        rig.Screen.TakeSnapshotCommand.CanExecuteChanged += (_, _) => changed++;

        rig.LaunchFlow!.SetActive(SampleScenario.SkyrimId, true);

        Assert.True(changed > 0);
    }

    [Fact]
    public async Task The_screen_says_when_a_profile_becomes_loaded_or_stops_being()
    {
        var rig = await GamesRig.StartAsync();
        var raised = new List<string?>();
        rig.Screen.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        rig.Backend.State.RowStats[SampleScenario.ModdedId] = rig.Backend.State.RowStats[SampleScenario.ModdedId] with { SnapshotNow = SnapshotNowAvailability.NotLoaded };

        await rig.Screen.ReloadAsync();

        Assert.Contains(nameof(GamesScreenViewModel.HasLoadedProfile), raised);
    }

    // ---- New profile

    [Fact]
    public async Task New_profile_opens_the_wizard_for_the_selected_game_with_its_preview_loading()
    {
        var rig = await GamesRig.StartAsync();

        var shown = rig.Screen.NewProfileCommand.ExecuteAsync(null);

        var wizard = Assert.IsType<NewProfileWizardViewModel>(Assert.Single(rig.Dialogs.Shown));
        Assert.Equal("New profile for Skyrim Special Edition", wizard.Title);
        Assert.False(wizard.Preview.IsLoading);
        Assert.True(wizard.Preview.HasRows);
        wizard.CancelCommand.Execute(null);
        await shown.Soon();
        Assert.Equal(3, rig.Screen.Profiles.Count);
    }

    [Fact]
    public async Task A_profile_saved_through_the_wizard_joins_the_table()
    {
        var rig = await GamesRig.StartAsync();

        var shown = rig.Screen.NewProfileCommand.ExecuteAsync(null);
        var wizard = Assert.IsType<NewProfileWizardViewModel>(Assert.Single(rig.Dialogs.Shown));
        wizard.Name = "Fresh profile";
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        rig.Backend.Jobs.Complete(rig.Backend.Jobs.LastStarted(JobKind.Capture).JobId);
        wizard.CloseCommand.Execute(null);
        await shown.Soon();

        Assert.Equal(["Modded playthrough - 7-2-26", "Vanilla - latest update", "Survival test - 5-14-26", "Fresh profile"], rig.Screen.Profiles.Select(row => row.Name));
    }

    [Fact]
    public async Task Closing_the_wizard_during_the_save_still_refreshes_the_table()
    {
        var rig = await GamesRig.StartAsync();

        var shown = rig.Screen.NewProfileCommand.ExecuteAsync(null);
        var wizard = Assert.IsType<NewProfileWizardViewModel>(Assert.Single(rig.Dialogs.Shown));
        wizard.Name = "Still saving";
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        wizard.RequestDismiss();
        await shown.Soon();

        Assert.Contains("Still saving", rig.Screen.Profiles.Select(row => row.Name));
    }

    [Fact]
    public async Task A_snapshot_left_running_with_keep_working_says_when_it_is_done_and_the_profile_stays_in_the_table()
    {
        var rig = await GamesRig.StartAsync();
        var shown = rig.Screen.NewProfileCommand.ExecuteAsync(null);
        var wizard = Assert.IsType<NewProfileWizardViewModel>(Assert.Single(rig.Dialogs.Shown));
        wizard.Name = "Big game profile";
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        wizard.KeepWorkingCommand.Execute(null);
        await shown.Soon();
        Assert.Empty(rig.ToastTexts);

        rig.Backend.Jobs.Complete(rig.Backend.Jobs.LastStarted(JobKind.Capture).JobId);

        Assert.Equal(["Took a snapshot of Skyrim Special Edition."], rig.ToastTexts);
        Assert.Contains("Big game profile", rig.Screen.Profiles.Select(row => row.Name));
    }

    [Fact]
    public async Task A_snapshot_left_running_with_keep_working_that_fails_says_why()
    {
        var rig = await GamesRig.StartAsync();
        var shown = rig.Screen.NewProfileCommand.ExecuteAsync(null);
        var wizard = Assert.IsType<NewProfileWizardViewModel>(Assert.Single(rig.Dialogs.Shown));
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        wizard.KeepWorkingCommand.Execute(null);
        await shown.Soon();

        rig.Backend.Jobs.Fail(rig.Backend.Jobs.LastStarted(JobKind.Capture).JobId, SampleScenario.SavesOutOfRoomError);

        Assert.Equal(["Something went wrong taking a snapshot of Skyrim Special Edition."], rig.ToastTexts);
        Assert.True(Assert.Single(rig.Toasts.Toasts).HasDetails);
    }

    [Fact]
    public async Task New_profile_does_nothing_without_a_game()
    {
        var rig = await GamesRig.StartAsync(FakeBackend.Empty());

        await rig.Screen.NewProfileCommand.ExecuteAsync(null);

        Assert.Empty(rig.Dialogs.Shown);
    }

    // ---- Edit a profile

    [Fact]
    public async Task Editing_a_profile_saves_through_the_api_and_the_table_shows_it()
    {
        var rig = await GamesRig.StartAsync();

        var shown = rig.Screen.Profiles[1].EditCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<EditProfileDialogViewModel>(Assert.Single(rig.Dialogs.Shown));
        Assert.Equal("Vanilla - latest update", dialog.Name);
        Assert.Equal("1.6.1170", dialog.VersionPlaceholder);
        dialog.Name = "Vanilla, tidied";
        dialog.Notes = "Before the survival patch";
        dialog.VersionOverride = "1.6.1170-patched";
        await dialog.SaveCommand.ExecuteAsync(null);
        await shown.Soon();

        var call = Assert.Single(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.UpdateAsync)));
        Assert.Equal([SampleScenario.SkyrimId, SampleScenario.VanillaId, new ProfileChanges("Vanilla, tidied", "Before the survival patch", "1.6.1170-patched")], call.Args);
        Assert.Equal("Vanilla, tidied", rig.Screen.Profiles[1].Name);
        Assert.Equal("1.6.1170-patched", rig.Screen.Profiles[1].VersionText);
        Assert.Equal("Before the survival patch", rig.Screen.Profiles[1].Notes);
    }

    [Fact]
    public async Task Cancelling_the_edit_changes_nothing()
    {
        var rig = await GamesRig.StartAsync();

        var shown = rig.Screen.Profiles[1].EditCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<EditProfileDialogViewModel>(Assert.Single(rig.Dialogs.Shown));
        dialog.Name = "Not saved";
        dialog.CancelCommand.Execute(null);
        await shown.Soon();

        Assert.Empty(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.UpdateAsync)));
        Assert.Equal("Vanilla - latest update", rig.Screen.Profiles[1].Name);
    }

    [Fact]
    public async Task A_refused_edit_leaves_the_dialog_open_and_the_table_alone()
    {
        var rig = await GamesRig.StartAsync();
        rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.UpdateAsync), SampleScenario.SavesOutOfRoomError);

        var shown = rig.Screen.Profiles[1].EditCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<EditProfileDialogViewModel>(Assert.Single(rig.Dialogs.Shown));
        dialog.Name = "Whatever";
        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.False(dialog.IsClosed);
        Assert.True(dialog.HasError);
        Assert.Equal("Vanilla - latest update", rig.Screen.Profiles[1].Name);

        dialog.CancelCommand.Execute(null);
        await shown.Soon();
    }

    // ---- Duplicate a profile

    [Fact]
    public async Task Duplicating_a_profile_adds_the_copy_to_the_table()
    {
        var rig = await GamesRig.StartAsync();

        var shown = rig.Screen.Profiles[1].DuplicateCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<DuplicateProfileDialogViewModel>(Assert.Single(rig.Dialogs.Shown));
        Assert.Equal("Vanilla - latest update copy", dialog.Name);
        dialog.Name = "Vanilla for testing";
        await dialog.SaveCommand.ExecuteAsync(null);
        await shown.Soon();

        var call = Assert.Single(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.DuplicateAsync)));
        Assert.Equal([SampleScenario.SkyrimId, SampleScenario.VanillaId, "Vanilla for testing"], call.Args);
        Assert.Equal(["Modded playthrough - 7-2-26", "Vanilla - latest update", "Survival test - 5-14-26", "Vanilla for testing"], rig.Screen.Profiles.Select(row => row.Name));
        Assert.Equal("1.6.1170", rig.Screen.Profiles[3].VersionText);
    }

    [Fact]
    public async Task Cancelling_a_duplicate_adds_nothing()
    {
        var rig = await GamesRig.StartAsync();

        var shown = rig.Screen.Profiles[1].DuplicateCommand.ExecuteAsync(null);
        Assert.IsType<DuplicateProfileDialogViewModel>(Assert.Single(rig.Dialogs.Shown)).CancelCommand.Execute(null);
        await shown.Soon();

        Assert.Empty(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.DuplicateAsync)));
        Assert.Equal(3, rig.Screen.Profiles.Count);
    }

    // ---- Delete a profile

    [Fact]
    public async Task Deleting_a_profile_with_nothing_only_it_holds_asks_a_plain_question()
    {
        var rig = await GamesRig.StartAsync(WithDeletePreview(Preview(SampleScenario.VanillaId, 0)));

        await rig.Screen.Profiles[1].DeleteCommand.ExecuteAsync(null);

        var request = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal("Delete Vanilla - latest update?", request.Title);
        Assert.Equal("This deletes the Vanilla - latest update profile and all its snapshots. This can't be undone.", request.Message);
        Assert.Equal("Delete profile", request.ConfirmLabel);
        Assert.True(request.Destructive);
        Assert.Equal(["Modded playthrough - 7-2-26", "Survival test - 5-14-26"], rig.Screen.Profiles.Select(row => row.Name));
        Assert.Single(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.DeleteAsync)));
    }

    [Theory]
    [InlineData(1, "1 snapshot is only in this profile and deleted for good. This can't be undone.")]
    [InlineData(4, "4 snapshots are only in this profile and deleted for good. This can't be undone.")]
    public async Task Deleting_a_profile_names_how_many_points_only_it_holds(int points, string message)
    {
        var rig = await GamesRig.StartAsync(WithDeletePreview(Preview(SampleScenario.VanillaId, points)));

        await rig.Screen.Profiles[1].DeleteCommand.ExecuteAsync(null);

        Assert.Equal(message, Assert.Single(rig.Dialogs.Confirmations).Message);
    }

    [Theory]
    [InlineData(2, 1, "2 snapshots are only in this profile and deleted for good. 1 more snapshot goes to Recently removed. This can't be undone.")]
    [InlineData(1, 3, "1 snapshot is only in this profile and deleted for good. 3 more snapshots go to Recently removed. This can't be undone.")]
    [InlineData(0, 1, "1 snapshot is only in this profile. It goes to Recently removed until you free up space. Nothing is deleted for good.")]
    [InlineData(0, 4, "4 snapshots are only in this profile. They go to Recently removed until you free up space. Nothing is deleted for good.")]
    public async Task Deleting_a_profile_whose_snapshots_go_back_to_Recently_removed_says_which_are_deleted_for_good_and_which_are_not(int forGood, int back, string message)
    {
        var preview = new ProfileDeletePreview(SampleScenario.VanillaId, forGood, [new FolderPointLoss(RepoRole.Install, forGood, null)], null, back);
        var rig = await GamesRig.StartAsync(WithDeletePreview(preview));

        await rig.Screen.Profiles[1].DeleteCommand.ExecuteAsync(null);

        Assert.Equal(message, Assert.Single(rig.Dialogs.Confirmations).Message);
    }

    [Fact]
    public async Task Declining_the_question_deletes_nothing()
    {
        var rig = await GamesRig.StartAsync(WithDeletePreview(Preview(SampleScenario.VanillaId, 2)));
        rig.Dialogs.AnswerConfirm(false);

        await rig.Screen.Profiles[1].DeleteCommand.ExecuteAsync(null);

        Assert.Empty(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.DeleteAsync)));
        Assert.Equal(3, rig.Screen.Profiles.Count);
    }

    [Fact]
    public async Task The_active_profile_is_refused_with_its_copy_and_offers_to_switch_to_another_profile()
    {
        var rig = await GamesRig.StartAsync();
        rig.Dialogs.AnswerConfirm(true);

        await rig.Screen.Profiles[0].DeleteCommand.ExecuteAsync(null);

        var offer = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal("This profile is loaded now", offer.Title);
        Assert.Equal("Switch to another profile or play one, then delete this one.", offer.Message);
        Assert.Equal("Switch to Vanilla - latest update", offer.ConfirmLabel);
        Assert.Empty(rig.ToastTexts);
        Assert.Empty(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.DeleteAsync)));
        var begun = Assert.Single(rig.LaunchFlow!.BegunSwitches);
        Assert.Equal(new LaunchTarget.ProfileTip(SampleScenario.VanillaId), begun.Target);
    }

    [Fact]
    public async Task A_running_game_refuses_the_delete_with_the_running_game_copy()
    {
        var rig = await GamesRig.StartAsync();
        FakeAssert.Ok(await rig.Backend.Launch.StartAsync(SampleScenario.SkyrimId, new LaunchTarget.ProfileTip(SampleScenario.ModdedId)));

        await rig.Screen.Profiles[1].DeleteCommand.ExecuteAsync(null);

        Assert.Empty(rig.Dialogs.Confirmations);
        Assert.Equal(["Skyrim Special Edition is running. Close the game, then try again."], rig.ToastTexts);
    }

    [Fact]
    public async Task A_delete_the_api_refuses_after_the_question_says_why_and_keeps_the_row()
    {
        var rig = await GamesRig.StartAsync(WithDeletePreview(Preview(SampleScenario.VanillaId, 0)));
        rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.DeleteAsync), SampleScenario.FileInUseError);

        await rig.Screen.Profiles[1].DeleteCommand.ExecuteAsync(null);

        Assert.Single(rig.Dialogs.Confirmations);
        Assert.Single(rig.ToastTexts);
        Assert.Equal(3, rig.Screen.Profiles.Count);
    }

    [Fact]
    public async Task A_preview_that_cannot_be_read_says_so_and_asks_nothing()
    {
        var rig = await GamesRig.StartAsync();
        rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.GetDeletePreviewAsync), SampleScenario.FileInUseError);

        await rig.Screen.Profiles[1].DeleteCommand.ExecuteAsync(null);

        Assert.Empty(rig.Dialogs.Confirmations);
        Assert.Single(rig.ToastTexts);
        Assert.Empty(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.DeleteAsync)));
    }

    // ---- Delete a game

    [Fact]
    public async Task Deleting_a_game_asks_once_for_its_name_then_removes_it_and_shows_the_next_one()
    {
        var rig = await GamesRig.StartAsync();

        await rig.Screen.DeleteGameCommand.ExecuteAsync(null);

        var asked = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal("Delete Skyrim Special Edition?", asked.Title);
        Assert.Equal(
            "This removes Skyrim Special Edition, its 3 profiles, all their snapshots and its recently removed snapshots. This can't be undone. Your game and saves folders stay as they are.",
            asked.Message);
        Assert.Equal("Delete game", asked.ConfirmLabel);
        Assert.True(asked.Destructive);
        Assert.Equal(new TypedConfirmation("Skyrim Special Edition", "Type Skyrim Special Edition to confirm."), asked.Typed);
        Assert.Single(rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.DeleteAsync)));
        Assert.Equal(["Satisfactory", "The Last Caretaker"], rig.Shell.Games.Select(row => row.Name));
        Assert.Equal("Satisfactory", rig.Screen.GameName);
    }

    [Fact]
    public async Task A_game_with_no_profiles_says_so_in_the_question()
    {
        var rig = await GamesRig.StartAsync(new FakeBackend(ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings).WithGame(SampleScenario.Caretaker)));

        await rig.Screen.DeleteGameCommand.ExecuteAsync(null);

        Assert.Equal(
            "The Last Caretaker has no profiles, but every snapshot kept for it is deleted for good. This can't be undone. Your game and saves folders stay as they are.",
            Assert.Single(rig.Dialogs.Confirmations).Message);
    }

    [Fact]
    public async Task Declining_the_name_question_deletes_no_game()
    {
        var rig = await GamesRig.StartAsync();
        rig.Dialogs.AnswerConfirm(false);

        await rig.Screen.DeleteGameCommand.ExecuteAsync(null);

        Assert.Single(rig.Dialogs.Confirmations);
        Assert.Empty(rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.DeleteAsync)));
        Assert.Equal(3, rig.Shell.Games.Count);
    }
    [Fact]
    public async Task A_game_with_an_active_session_cannot_be_deleted_and_nothing_is_asked()
    {
        var rig = await GamesRig.StartAsync();
        rig.LaunchFlow!.SetActive(SampleScenario.SkyrimId, true);

        await rig.Screen.DeleteGameCommand.ExecuteAsync(null);

        Assert.Empty(rig.Dialogs.Confirmations);
        Assert.Equal(["Skyrim Special Edition is running. Close the game, then try again."], rig.ToastTexts);
        Assert.Empty(rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.DeleteAsync)));
    }

    [Fact]
    public async Task A_delete_the_api_refuses_shows_its_copy_and_keeps_the_game()
    {
        var rig = await GamesRig.StartAsync();
        FakeAssert.Ok(await rig.Backend.Launch.StartAsync(SampleScenario.SkyrimId, new LaunchTarget.ProfileTip(SampleScenario.ModdedId)));

        await rig.Screen.DeleteGameCommand.ExecuteAsync(null);

        Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal(["Skyrim Special Edition is running. Close the game, then try again."], rig.ToastTexts);
        Assert.Equal(3, rig.Shell.Games.Count);
    }

    [Fact]
    public async Task Deleting_the_only_game_leaves_the_screen_without_a_game()
    {
        var rig = await GamesRig.StartAsync(new FakeBackend(ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings).WithGame(SampleScenario.Caretaker, 1)));

        await rig.Screen.DeleteGameCommand.ExecuteAsync(null);

        Assert.False(rig.Screen.HasGame);
        Assert.Empty(rig.Screen.Profiles);
    }
}
