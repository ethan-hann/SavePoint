using Savepoint.App.Features.Editor;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Editor;

public class GameEditorConsentTests
{
    private static GameEditorViewModel NewGame(EditorRig rig)
    {
        var editor = rig.New();
        editor.Name = "Hollow Knight";
        editor.InstallPath = @"C:\Games\Hollow Knight";
        editor.SavesPath = @"{appData}\Hollow Knight";
        editor.ExePath = "hollow_knight.exe";
        return editor;
    }

    // Starts a save and hands back the confirmation it stops at; the save finishes when the test answers it.
    private static (Task Saving, ConsentDialogViewModel Dialog) SaveUntilAsked(EditorRig rig, GameEditorViewModel editor)
    {
        var shownBefore = rig.Dialogs.Shown.Count;
        var saving = editor.SaveCommand.ExecuteAsync(null);
        Assert.False(saving.IsCompleted, "The save should be waiting for the confirmation.");
        return (saving, Assert.IsType<ConsentDialogViewModel>(rig.Dialogs.Shown[shownBefore]));
    }

    private static void AssertNoQuestion(EditorRig rig, Task saving)
    {
        Assert.True(saving.IsCompleted);
        Assert.Empty(rig.Dialogs.Shown);
    }

    [Fact]
    public async Task A_new_game_names_each_folder_by_its_fully_resolved_path_before_saving_anything()
    {
        var rig = EditorRig.Create();
        var editor = NewGame(rig);

        var (saving, dialog) = SaveUntilAsked(rig, editor);

        Assert.Equal(
            [
                new ConsentFolder("Game install folder", @"C:\Games\Hollow Knight"),
                new ConsentFolder("Saves folder", @"C:\Users\you\AppData\Roaming\Hollow Knight"),
            ],
            dialog.Folders);
        Assert.Empty(rig.Calls("AddAsync"));

        dialog.ConfirmCommand.Execute(null);
        await saving;

        Assert.Single(rig.Calls("AddAsync"));
    }

    [Fact]
    public async Task Cancelling_the_confirmation_saves_nothing_and_keeps_the_editor_open()
    {
        var rig = EditorRig.Create();
        var editor = NewGame(rig);

        var (saving, dialog) = SaveUntilAsked(rig, editor);
        dialog.CancelCommand.Execute(null);
        await saving;

        Assert.Empty(rig.Calls("AddAsync"));
        Assert.Empty(rig.Shell.Calls);
        Assert.False(editor.IsSaving);
        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Dismissing_the_confirmation_with_Escape_counts_as_no()
    {
        var rig = EditorRig.Create();
        var editor = NewGame(rig);

        var (saving, dialog) = SaveUntilAsked(rig, editor);
        dialog.RequestDismiss();
        await saving;

        Assert.Empty(rig.Calls("AddAsync"));
    }

    [Fact]
    public async Task A_game_with_no_saves_folder_confirms_only_the_install_folder()
    {
        var rig = EditorRig.Create();
        var editor = NewGame(rig);
        editor.SavesPath = "";

        var (saving, dialog) = SaveUntilAsked(rig, editor);
        dialog.ConfirmCommand.Execute(null);
        await saving;

        Assert.Equal([new ConsentFolder("Game install folder", @"C:\Games\Hollow Knight")], dialog.Folders);
    }

    [Fact]
    public async Task Editing_something_else_asks_nothing_about_folders_that_are_confirmed_and_unchanged()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();
        await editor.LoadAsync();
        editor.Name = "Skyrim SE";

        var saving = editor.SaveCommand.ExecuteAsync(null);

        AssertNoQuestion(rig, saving);
        Assert.Single(rig.Calls("UpdateAsync"));
        await saving;
    }

    [Fact]
    public async Task Pointing_the_saves_at_a_new_place_confirms_that_folder_only()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();
        await editor.LoadAsync();
        editor.SavesPath = @"{documents}\My Games\Skyrim Special Edition\Backup";

        var (saving, dialog) = SaveUntilAsked(rig, editor);

        Assert.Equal([new ConsentFolder("Saves folder", @"C:\Users\you\Documents\My Games\Skyrim Special Edition\Backup")], dialog.Folders);
        Assert.Empty(rig.Calls("UpdateAsync"));

        dialog.ConfirmCommand.Execute(null);
        await saving;

        Assert.Equal(@"{documents}\My Games\Skyrim Special Edition\Backup", rig.Saved("UpdateAsync").External?.Path);
    }

    [Fact]
    public async Task Pointing_the_install_at_a_new_place_confirms_that_folder_only()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();
        await editor.LoadAsync();
        editor.InstallPath = @"D:\Games\SkyrimSE";

        var (saving, dialog) = SaveUntilAsked(rig, editor);
        dialog.ConfirmCommand.Execute(null);
        await saving;

        Assert.Equal([new ConsentFolder("Game install folder", @"D:\Games\SkyrimSE")], dialog.Folders);
    }

    [Fact]
    public async Task Adding_a_saves_folder_to_a_game_that_had_none_confirms_it()
    {
        var withoutSaves = SampleScenario.Caretaker with { External = null };
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty().WithGame(withoutSaves)));
        var editor = rig.Edit(withoutSaves.Id);
        await editor.LoadAsync();
        editor.SavesPath = @"{localAppData}\TheLastCaretaker\Saved\SaveGames";

        var (saving, dialog) = SaveUntilAsked(rig, editor);
        dialog.ConfirmCommand.Execute(null);
        await saving;

        Assert.Equal([new ConsentFolder("Saves folder", @"C:\Users\you\AppData\Local\TheLastCaretaker\Saved\SaveGames")], dialog.Folders);
    }

    [Fact]
    public async Task A_folder_the_Api_reports_as_not_confirmed_is_asked_about_even_when_unchanged()
    {
        var unconfirmed = new FolderStatus(RepoRole.External, SampleScenario.SkyrimSavesRaw, SampleScenario.SkyrimSaves, true, new ConsentStatus(false, null));
        var confirmed = new FolderStatus(RepoRole.Install, SampleScenario.SkyrimInstall, SampleScenario.SkyrimInstall, true, new ConsentStatus(true, null));
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty().WithGame(SampleScenario.Skyrim).WithFolderStatus(SampleScenario.SkyrimId, confirmed, unconfirmed)));
        var editor = rig.Edit();
        await editor.LoadAsync();

        var (saving, dialog) = SaveUntilAsked(rig, editor);
        dialog.ConfirmCommand.Execute(null);
        await saving;

        Assert.Equal([new ConsentFolder("Saves folder", SampleScenario.SkyrimSaves)], dialog.Folders);
    }

    [Fact]
    public async Task A_retry_does_not_ask_again_about_folders_already_confirmed_in_this_editor()
    {
        var rig = EditorRig.Create();
        var editor = NewGame(rig);
        rig.Backend.FailNext<IGamesApi>("AddAsync", new ErrorInfo("error.unknown", new Dictionary<string, object?>(), string.Empty));

        var (first, dialog) = SaveUntilAsked(rig, editor);
        dialog.ConfirmCommand.Execute(null);
        await first;
        var second = editor.SaveCommand.ExecuteAsync(null);

        Assert.Single(rig.Dialogs.Shown);
        await second;
        Assert.Equal(2, rig.Calls("AddAsync").Count);
    }

    [Fact]
    public async Task A_changed_folder_is_asked_about_again_after_a_failed_attempt()
    {
        var rig = EditorRig.Create();
        var editor = NewGame(rig);
        rig.Backend.FailNext<IGamesApi>("AddAsync", new ErrorInfo("error.unknown", new Dictionary<string, object?>(), string.Empty));
        var (first, dialog) = SaveUntilAsked(rig, editor);
        dialog.ConfirmCommand.Execute(null);
        await first;

        editor.InstallPath = @"D:\Games\Hollow Knight";
        var (second, again) = SaveUntilAsked(rig, editor);
        again.ConfirmCommand.Execute(null);
        await second;

        Assert.Equal([new ConsentFolder("Game install folder", @"D:\Games\Hollow Knight")], again.Folders);
    }

    [Fact]
    public async Task A_missing_confirmation_reported_by_the_Api_is_asked_for_again_on_the_next_save()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();
        await editor.LoadAsync();
        editor.Name = "Skyrim SE";
        rig.Backend.FailNext<IGamesApi>("UpdateAsync", ErrorInfo.From(new ConsentMarkerException(ConsentFailure.Missing, SampleScenario.SkyrimInstall, RepoRole.Install, null, null)));
        var first = editor.SaveCommand.ExecuteAsync(null);
        Assert.True(first.IsCompleted, "The first save should not ask anything: the folders are confirmed and unchanged.");
        await first;

        Assert.NotNull(editor.InstallCard);
        Assert.Equal("This folder isn't confirmed yet", editor.InstallCard.Title);

        var (second, dialog) = SaveUntilAsked(rig, editor);
        dialog.ConfirmCommand.Execute(null);
        await second;

        Assert.Equal([new ConsentFolder("Game install folder", SampleScenario.SkyrimInstall)], dialog.Folders);
        Assert.Equal(2, rig.Calls("UpdateAsync").Count);
    }
}
