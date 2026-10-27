using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Savepoint.App.Tests.Features.Editor;

// The pickers sit in the mod files row beside "Add a folder or file", named for what they pick.
public class GameEditorModFilePickViewTests
{
    private static List<Button> RowButtons(EditorViewHarness harness) =>
        harness.Find<ContentControl>("ModFoldersHost").GetVisualDescendants().OfType<Button>().Where(button => AutomationProperties.GetName(button) is "Add a folder or file" or "Choose files…, for the mod files in the game folder" or "Choose folders…, for the mod files in the game folder").ToList();

    [AvaloniaFact]
    public async Task The_row_shows_the_add_chip_then_Choose_files_then_Choose_folders_by_their_words()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();
        harness.Settle();

        var buttons = RowButtons(harness);

        Assert.Equal(["Add a folder or file", "Choose files…, for the mod files in the game folder", "Choose folders…, for the mod files in the game folder"], buttons.Select(button => AutomationProperties.GetName(button)));
        var words = buttons.Skip(1).Select(button => button.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        Assert.Equal(["Choose files…", "Choose folders…"], words);
        Assert.All(buttons, button => Assert.True(button.IsEffectivelyEnabled && button.IsEffectivelyVisible));
    }

    [AvaloniaFact]
    public async Task Without_a_game_folder_the_buttons_are_off_and_the_line_under_the_list_says_why()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.New();
        harness.Settle();

        var note = harness.Find<TextBlock>("ModFilesPickNoteText");
        var pickers = RowButtons(harness).Skip(1).ToList();

        Assert.True(note.IsEffectivelyVisible);
        Assert.Equal("Choose the game's install folder first, then you can pick its mod files here.", note.Text);
        Assert.All(pickers, button => Assert.False(button.IsEffectivelyEnabled));
        Assert.All(pickers, button => Assert.Equal(note.Text, AutomationProperties.GetHelpText(button)));

        editor.InstallPath = Path.GetTempPath();
        harness.Settle();

        Assert.False(note.IsEffectivelyVisible);
        Assert.All(pickers, button => Assert.True(button.IsEffectivelyEnabled));
        Assert.All(pickers, button => Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(button))));
    }

    [AvaloniaFact]
    public async Task A_picker_button_keeps_keyboard_focus_when_the_pick_adds_entries()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.Edit();
        harness.Settle();
        var button = RowButtons(harness)[1];
        button.Focus();
        Assert.Same(button, harness.Window.FocusManager!.GetFocusedElement());

        editor.ModFolders.AddValues(["version.dll", "winmm.dll"]);
        harness.Settle();

        Assert.Same(button, harness.Window.FocusManager!.GetFocusedElement());
        Assert.Contains("version.dll", editor.ModFolders.Values);
    }
}
