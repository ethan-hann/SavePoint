namespace Savepoint.App.Tests.Features.Editor;

public class GameEditorBackTests
{
    [Fact]
    public async Task Going_back_with_nothing_changed_closes_the_pane_without_asking()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();

        await editor.BackCommand.ExecuteAsync(null);

        Assert.Equal(["close"], rig.Shell.Calls);
        Assert.Empty(rig.Dialogs.Confirmations);
    }

    [Fact]
    public async Task Going_back_from_an_untouched_new_game_closes_the_pane_without_asking()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();

        await editor.BackCommand.ExecuteAsync(null);

        Assert.Equal(["close"], rig.Shell.Calls);
        Assert.Empty(rig.Dialogs.Confirmations);
    }

    [Fact]
    public async Task Going_back_with_edits_asks_whether_to_discard_them_and_keeps_the_editor_when_the_answer_is_no()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();
        editor.Name = "Skyrim SE";
        rig.Dialogs.AnswerConfirm(false);

        await editor.BackCommand.ExecuteAsync(null);

        var ask = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Equal("Discard your changes?", ask.Title);
        Assert.Equal("Going back now throws away the changes you haven't saved.", ask.Message);
        Assert.Equal("Discard changes", ask.ConfirmLabel);
        Assert.Equal("Keep editing", ask.CancelLabel);
        Assert.True(ask.Destructive);
        Assert.Empty(rig.Shell.Calls);
        Assert.Equal("Skyrim SE", editor.Name);
    }

    [Fact]
    public async Task Discarding_closes_the_pane_and_saves_nothing()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();
        editor.IgnoreRules.Items[0].Remove.Execute(null);
        rig.Dialogs.AnswerConfirm(true);

        await editor.BackCommand.ExecuteAsync(null);

        Assert.Equal(["close"], rig.Shell.Calls);
        Assert.Empty(rig.Calls("UpdateAsync"));
    }

    [Fact]
    public async Task A_new_game_with_something_typed_in_asks_too()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();
        editor.Name = "Hollow Knight";
        rig.Dialogs.AnswerConfirm(false);

        await editor.BackCommand.ExecuteAsync(null);

        Assert.Single(rig.Dialogs.Confirmations);
        Assert.Empty(rig.Shell.Calls);
    }
}
