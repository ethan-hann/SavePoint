using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Editor;

namespace Savepoint.App.Tests.Features.Editor;

// The Name field's match list used with the keyboard and a screen reader.
public class GameEditorTemplateViewTests
{
    private const string _skyrimName = "The Elder Scrolls V: Skyrim Special Edition";

    private static void Press(EditorViewHarness harness, Key key, PhysicalKey physical)
    {
        harness.Window.KeyPress(key, RawInputModifiers.None, physical, null);
        harness.Window.KeyRelease(key, RawInputModifiers.None, physical, null);
        harness.Settle();
    }

    private static string? FocusedName(EditorViewHarness harness) =>
        harness.Window.FocusManager!.GetFocusedElement() is Control focused ? AutomationProperties.GetName(focused) : null;

    private static async Task<(EditorViewHarness Harness, GameEditorViewModel Editor)> TypeAsync(string typed)
    {
        var harness = await EditorViewHarness.StartAsync();
        var editor = harness.New();
        harness.Settle();
        harness.Find<TextBox>("NameField").Focus();
        editor.Name = typed;
        harness.Settle();
        return (harness, editor);
    }

    [AvaloniaFact]
    public async Task Escape_in_the_name_field_closes_the_list()
    {
        var (harness, editor) = await TypeAsync("skyrim special");
        using var _ = harness;
        Assert.True(harness.Find<ItemsControl>("NameMatchList").IsEffectivelyVisible);

        Press(harness, Key.Escape, PhysicalKey.Escape);

        Assert.False(editor.HasNameMatches);
        Assert.False(harness.Find<ItemsControl>("NameMatchList").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Enter_in_the_name_field_takes_the_top_match_and_the_focus_stays_on_the_name()
    {
        var (harness, editor) = await TypeAsync("skyrim special");
        using var _ = harness;

        Press(harness, Key.Enter, PhysicalKey.Enter);

        Assert.Equal(_skyrimName, editor.Name);
        Assert.Equal("Name", FocusedName(harness));
    }

    [AvaloniaFact]
    public async Task The_down_arrow_moves_from_the_name_into_the_list()
    {
        var (harness, _) = await TypeAsync("skyrim special");
        using var _ = harness;

        Press(harness, Key.Down, PhysicalKey.ArrowDown);

        Assert.Equal($"Fill in from {_skyrimName}", FocusedName(harness));
    }

    [AvaloniaFact]
    public async Task Choosing_a_match_from_the_list_with_the_keyboard_returns_the_focus_to_the_name()
    {
        var (harness, editor) = await TypeAsync("skyrim special");
        using var _ = harness;
        Press(harness, Key.Down, PhysicalKey.ArrowDown);

        Press(harness, Key.Enter, PhysicalKey.Enter);

        Assert.Equal(_skyrimName, editor.Name);
        Assert.Equal("Name", FocusedName(harness));
    }

    [AvaloniaFact]
    public async Task The_list_stays_open_while_the_focus_is_in_it_and_closes_when_the_focus_goes_elsewhere()
    {
        var (harness, editor) = await TypeAsync("skyrim special");
        using var _ = harness;
        Press(harness, Key.Down, PhysicalKey.ArrowDown);
        Assert.True(editor.HasNameMatches);

        harness.Find<TextBox>("InstallField").Focus();
        harness.Settle();

        Assert.False(editor.HasNameMatches);
    }

    [AvaloniaFact]
    public async Task The_matches_are_announced_and_each_match_says_it_fills_the_form()
    {
        var (harness, editor) = await TypeAsync("skyrim special");
        using var _ = harness;

        var announcer = harness.Find<AnnouncementRegion>("NameMatchesAnnouncer");
        var match = harness.Find<ItemsControl>("NameMatchList").GetVisualDescendants().OfType<Button>().Single();

        Assert.Equal("1 game found. Choose one to fill in the details.", announcer.Message);
        Assert.Equal($"Fill in from {_skyrimName}", AutomationProperties.GetName(match));
        Assert.Equal(editor.NameMatchesAnnouncement, announcer.Message);
    }
}
