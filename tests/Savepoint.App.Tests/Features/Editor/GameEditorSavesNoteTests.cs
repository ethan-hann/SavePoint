namespace Savepoint.App.Tests.Features.Editor;

// A game added from a store library may have no saves folder yet; the editor says what that means instead of leaving the field bare.
public class GameEditorSavesNoteTests
{
    [Fact]
    public void A_game_with_no_saves_folder_gets_a_note_that_explains_what_is_missing()
    {
        var rig = EditorRig.Create();

        var editor = rig.New();

        Assert.True(editor.HasNoSavesFolder);
    }

    [Fact]
    public void The_note_goes_away_while_a_saves_folder_is_filled_in_and_comes_back_when_it_is_cleared()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();
        var changes = new List<string?>();
        editor.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        editor.SavesPath = @"{documents}\My Games\Alpha";
        var whileFilled = editor.HasNoSavesFolder;
        editor.SavesPath = "   ";

        Assert.False(whileFilled);
        Assert.True(editor.HasNoSavesFolder);
        Assert.Contains(nameof(editor.HasNoSavesFolder), changes);
    }

    [Fact]
    public void A_game_that_has_a_saves_folder_gets_no_note()
    {
        var rig = EditorRig.Create();

        var editor = rig.Edit();

        Assert.False(editor.HasNoSavesFolder);
    }
}
