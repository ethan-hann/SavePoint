using Savepoint.App.Features.Editor;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Editor;

// A folder that has history and is pointed at a place holding few of its files asks first, with both counts (D31).
public class GameEditorFolderMismatchTests
{
    private static async Task Save(EditorRig rig, GameEditorViewModel editor)
    {
        var running = editor.SaveCommand.ExecuteAsync(null);
        while (!running.IsCompleted && rig.Dialogs.Shown.LastOrDefault() is ConsentDialogViewModel { IsClosed: false } consent)
        {
            consent.ConfirmCommand.Execute(null);
        }

        await running;
    }

    private static GameEditorViewModel Moved(EditorRig rig)
    {
        var editor = rig.Edit();
        editor.SavesPath = @"{appData}\Somewhere Else";
        rig.Backend.FailNext<IGamesApi>("UpdateAsync", ErrorInfo.From(new FolderMismatchException(RepoRole.External, @"C:\Users\Test\AppData\Roaming\Somewhere Else", 3, 212)));
        return editor;
    }

    [Fact]
    public async Task The_question_names_the_folder_and_both_counts_and_saves_nothing_until_it_is_answered_yes()
    {
        var rig = EditorRig.Create();
        rig.Dialogs.AnswerConfirm(false);
        var editor = Moved(rig);

        await Save(rig, editor);

        var asked = Assert.Single(rig.Dialogs.Confirmations);
        Assert.Contains("saves folder", asked.Message);
        Assert.Contains("3", asked.Message);
        Assert.Contains("212", asked.Message);
        Assert.Contains(@"Somewhere Else", asked.Message);
        Assert.True(asked.Destructive);
        Assert.Equal(false, Assert.Single(rig.Calls("UpdateAsync")).Args[1]);
    }

    [Fact]
    public async Task Answering_yes_repeats_the_save_with_the_change_confirmed()
    {
        var rig = EditorRig.Create();
        rig.Dialogs.AnswerConfirm(true);
        var editor = Moved(rig);

        await Save(rig, editor);

        var calls = rig.Calls("UpdateAsync");
        Assert.Equal(2, calls.Count);
        Assert.Equal(false, calls[0].Args[1]);
        Assert.Equal(true, calls[1].Args[1]);
        Assert.Null(editor.SavesCard);
        Assert.Null(editor.FormError);
    }

    [Fact]
    public async Task Answering_no_leaves_the_editor_as_it_was_with_nothing_saved()
    {
        var rig = EditorRig.Create();
        rig.Dialogs.AnswerConfirm(false);
        var editor = Moved(rig);

        await Save(rig, editor);

        Assert.Single(rig.Calls("UpdateAsync"));
        Assert.Null(editor.SavesCard);
        Assert.Null(editor.FormError);
        Assert.False(editor.IsSaving);
    }
}
