using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Games;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

public class GamesScreenActionsTests
{
    private static FakeBackend WithDeletePreview(ProfileDeletePreview preview) =>
        new(ScenarioBuilder.Mockup().WithDeletePreview(preview));

    private static ProfileDeletePreview Preview(string profileId, int points) =>
        new(profileId, points, [new FolderPointLoss(RepoRole.Install, points, null)], null);

    // ---- Play

    [Fact]
    public async Task Play_starts_the_launch_flow_on_the_profiles_latest_points()
    {
        var rig = await GamesRig.StartAsync();

        await rig.Screen.Profiles[1].PlayCommand.ExecuteAsync(null);

        var begun = Assert.Single(rig.LaunchFlow!.Begun);
        Assert.Equal(MockupScenario.SkyrimId, begun.GameId);
        Assert.Equal(new LaunchTarget.ProfileTip(MockupScenario.VanillaId), begun.Target);
    }

    [Fact]
    public async Task Play_does_nothing_while_the_game_has_a_session()
    {
        var rig = await GamesRig.StartAsync();
        rig.LaunchFlow!.SetActive(MockupScenario.SkyrimId, true);

        await rig.Screen.Profiles[0].PlayCommand.ExecuteAsync(null);

        Assert.Empty(rig.LaunchFlow.Begun);
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
        Assert.Equal([MockupScenario.SkyrimId, MockupScenario.VanillaId, new ProfileChanges("Vanilla, tidied", "Before the survival patch", "1.6.1170-patched")], call.Args);
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
        rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.UpdateAsync), MockupScenario.SavesOutOfRoomError);

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
        Assert.Equal([MockupScenario.SkyrimId, MockupScenario.VanillaId, "Vanilla for testing"], call.Args);
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
        var rig = await GamesRig.StartAsync(WithDeletePreview(Preview(MockupScenario.VanillaId, 0)));

        await rig.Screen.Profiles[1].DeleteCommand.ExecuteAsync(null);

        var request = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal("Delete Vanilla - latest update?", request.Title);
        Assert.Equal("This removes the Vanilla - latest update profile and all its history. This can't be undone.", request.Message);
        Assert.Equal("Delete profile", request.ConfirmLabel);
        Assert.True(request.Destructive);
        Assert.Equal(["Modded playthrough - 7-2-26", "Survival test - 5-14-26"], rig.Screen.Profiles.Select(row => row.Name));
        Assert.Single(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.DeleteAsync)));
    }

    [Theory]
    [InlineData(1, "1 point exists only in this profile. Deleting it loses that point for good, and the space is freed soon after. This can't be undone.")]
    [InlineData(4, "4 points exist only in this profile. Deleting it loses those points for good, and the space is freed soon after. This can't be undone.")]
    public async Task Deleting_a_profile_names_how_many_points_only_it_holds(int points, string message)
    {
        var rig = await GamesRig.StartAsync(WithDeletePreview(Preview(MockupScenario.VanillaId, points)));

        await rig.Screen.Profiles[1].DeleteCommand.ExecuteAsync(null);

        Assert.Equal(message, Assert.Single(rig.Dialogs.Confirmations).Message);
    }

    [Fact]
    public async Task Declining_the_question_deletes_nothing()
    {
        var rig = await GamesRig.StartAsync(WithDeletePreview(Preview(MockupScenario.VanillaId, 2)));
        rig.Dialogs.AnswerConfirm(false);

        await rig.Screen.Profiles[1].DeleteCommand.ExecuteAsync(null);

        Assert.Empty(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.DeleteAsync)));
        Assert.Equal(3, rig.Screen.Profiles.Count);
    }

    [Fact]
    public async Task The_active_profile_is_refused_with_its_copy_and_nothing_is_asked()
    {
        var rig = await GamesRig.StartAsync();

        await rig.Screen.Profiles[0].DeleteCommand.ExecuteAsync(null);

        Assert.Empty(rig.Dialogs.Confirmations);
        Assert.Equal(["This is the currently active profile. Switch to a different one before deleting it."], rig.ToastTexts);
        Assert.Empty(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.DeleteAsync)));
    }

    [Fact]
    public async Task A_running_game_refuses_the_delete_with_the_running_game_copy()
    {
        var rig = await GamesRig.StartAsync();
        FakeAssert.Ok(await rig.Backend.Launch.StartAsync(MockupScenario.SkyrimId, new LaunchTarget.ProfileTip(MockupScenario.ModdedId)));

        await rig.Screen.Profiles[1].DeleteCommand.ExecuteAsync(null);

        Assert.Empty(rig.Dialogs.Confirmations);
        Assert.Equal(["Skyrim Special Edition is running right now, so SavePoint can't do that yet. Close the game, then try again."], rig.ToastTexts);
    }

    [Fact]
    public async Task A_delete_the_api_refuses_after_the_question_says_why_and_keeps_the_row()
    {
        var rig = await GamesRig.StartAsync(WithDeletePreview(Preview(MockupScenario.VanillaId, 0)));
        rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.DeleteAsync), MockupScenario.FileInUseError);

        await rig.Screen.Profiles[1].DeleteCommand.ExecuteAsync(null);

        Assert.Single(rig.Dialogs.Confirmations);
        Assert.Single(rig.ToastTexts);
        Assert.Equal(3, rig.Screen.Profiles.Count);
    }

    [Fact]
    public async Task A_preview_that_cannot_be_read_says_so_and_asks_nothing()
    {
        var rig = await GamesRig.StartAsync();
        rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.GetDeletePreviewAsync), MockupScenario.FileInUseError);

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
            "This removes Skyrim Special Edition from SavePoint, along with its 3 profiles and all its history. This can't be undone. The files in your game folder and your saves folder are not touched.",
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
        var rig = await GamesRig.StartAsync(new FakeBackend(ScenarioBuilder.Empty().WithSettings(MockupScenario.Settings).WithGame(MockupScenario.Caretaker)));

        await rig.Screen.DeleteGameCommand.ExecuteAsync(null);

        Assert.Equal(
            "The Last Caretaker has no profiles yet, but everything SavePoint saved for it will be gone for good. This can't be undone. The files in your game folder and your saves folder are not touched.",
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
        rig.LaunchFlow!.SetActive(MockupScenario.SkyrimId, true);

        await rig.Screen.DeleteGameCommand.ExecuteAsync(null);

        Assert.Empty(rig.Dialogs.Confirmations);
        Assert.Equal(["Skyrim Special Edition is running right now, so SavePoint can't do that yet. Close the game, then try again."], rig.ToastTexts);
        Assert.Empty(rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.DeleteAsync)));
    }

    [Fact]
    public async Task A_delete_the_api_refuses_shows_its_copy_and_keeps_the_game()
    {
        var rig = await GamesRig.StartAsync();
        FakeAssert.Ok(await rig.Backend.Launch.StartAsync(MockupScenario.SkyrimId, new LaunchTarget.ProfileTip(MockupScenario.ModdedId)));

        await rig.Screen.DeleteGameCommand.ExecuteAsync(null);

        Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal(["Skyrim Special Edition is running right now, so SavePoint can't do that yet. Close the game, then try again."], rig.ToastTexts);
        Assert.Equal(3, rig.Shell.Games.Count);
    }

    [Fact]
    public async Task Deleting_the_only_game_leaves_the_screen_without_a_game()
    {
        var rig = await GamesRig.StartAsync(new FakeBackend(ScenarioBuilder.Empty().WithSettings(MockupScenario.Settings).WithGame(MockupScenario.Caretaker, 1)));

        await rig.Screen.DeleteGameCommand.ExecuteAsync(null);

        Assert.False(rig.Screen.HasGame);
        Assert.Empty(rig.Screen.Profiles);
    }
}
