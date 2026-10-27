using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.Editor;

namespace Savepoint.App.Tests.Features.Editor;

// Editing a chip in place, driven through the real editor pane.
public class ChipListEditViewTests
{
    private const string RulesList = "Files to leave out of both folders";

    private static readonly string[] _sampleRules = ["**/*.log", "SKSE/Plugins/*.log", "**/Crash Logs/**"];

    private static void Press(EditorViewHarness harness, Key key, PhysicalKey physical)
    {
        harness.Window.KeyPress(key, RawInputModifiers.None, physical, null);
        harness.Window.KeyRelease(key, RawInputModifiers.None, physical, null);
        harness.Settle();
    }

    private static List<Border> Chips(EditorViewHarness harness) =>
        [.. harness.Find<ContentControl>("IgnoreRulesHost").GetVisualDescendants().OfType<Border>().Where(border => border.Name == "ChipText")];

    private static ChipEntryBox OpenField(EditorViewHarness harness) =>
        harness.Find<ContentControl>("IgnoreRulesHost").GetVisualDescendants().OfType<ChipEntryBox>()
            .Single(box => box.Name == "ChipEdit" && box.IsEffectivelyVisible);

    private static string? FocusedName(EditorViewHarness harness) =>
        harness.Window.FocusManager!.GetFocusedElement() is Control focused ? AutomationProperties.GetName(focused) : null;

    private static void FocusChip(EditorViewHarness harness, int index)
    {
        Chips(harness)[index].Focus(NavigationMethod.Tab);
        harness.Settle();
    }

    [AvaloniaFact]
    public async Task A_chip_is_focusable_and_named_for_editing_and_says_how()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        var chips = Chips(harness);

        Assert.Equal(3, chips.Count);
        Assert.All(chips, chip => Assert.True(chip.Focusable));
        Assert.Equal($"Edit **/*.log in {RulesList}", AutomationProperties.GetName(chips[0]));
        Assert.Contains("F2", AutomationProperties.GetHelpText(chips[0]), StringComparison.Ordinal);

        // What a screen reader is handed: a named button that can take focus.
        var peer = ControlAutomationPeer.CreatePeerForElement(chips[0]);
        Assert.Equal($"Edit **/*.log in {RulesList}", peer.GetName());
        Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
        Assert.True(peer.IsKeyboardFocusable());
    }

    [AvaloniaFact]
    public async Task F2_on_a_focused_chip_opens_its_text_in_a_focused_field_and_Enter_keeps_the_change()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.Edit();
        FocusChip(harness, 1);

        Press(harness, Key.F2, PhysicalKey.F2);
        var field = OpenField(harness);

        Assert.True(field.IsFocused);
        Assert.Equal("SKSE/Plugins/*.log", field.Text);
        Assert.Equal($"Change SKSE/Plugins/*.log in {RulesList}", AutomationProperties.GetName(field));
        Assert.False(Chips(harness)[1].IsEffectivelyVisible);

        harness.Window.KeyTextInput("**/*.tmp");
        Press(harness, Key.Enter, PhysicalKey.Enter);

        Assert.Equal(["**/*.log", "**/*.tmp", "**/Crash Logs/**"], editor.IgnoreRules.Values);
        Assert.Equal($"Edit **/*.tmp in {RulesList}", FocusedName(harness));
    }

    [AvaloniaFact]
    public async Task Enter_on_a_focused_chip_opens_it_and_Escape_drops_the_change_and_returns_to_the_chip()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.Edit();
        FocusChip(harness, 2);

        Press(harness, Key.Enter, PhysicalKey.Enter);
        Assert.True(OpenField(harness).IsFocused);
        harness.Window.KeyTextInput("zzz");
        Press(harness, Key.Escape, PhysicalKey.Escape);

        Assert.Equal(_sampleRules, editor.IgnoreRules.Values);
        Assert.Equal($"Edit **/Crash Logs/** in {RulesList}", FocusedName(harness));
        Assert.All(editor.IgnoreRules.Items, chip => Assert.False(chip.IsEditing));
    }

    [AvaloniaFact]
    public async Task Entering_a_blank_or_repeated_value_leaves_the_chip_as_it_was()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.Edit();

        FocusChip(harness, 1);
        Press(harness, Key.F2, PhysicalKey.F2);
        harness.Window.KeyTextInput("**/*.log");
        Press(harness, Key.Enter, PhysicalKey.Enter);
        Assert.Equal(_sampleRules, editor.IgnoreRules.Values);

        FocusChip(harness, 1);
        Press(harness, Key.F2, PhysicalKey.F2);
        harness.Window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
        harness.Settle();
        Press(harness, Key.Enter, PhysicalKey.Enter);

        Assert.Equal(_sampleRules, editor.IgnoreRules.Values);
    }

    [AvaloniaFact]
    public async Task Double_clicking_a_chip_opens_it_for_editing()
    {
        using var harness = await EditorViewHarness.StartAsync(height: 1600);
        harness.Edit();
        var chip = Chips(harness)[0];
        var point = chip.TranslatePoint(new Point(chip.Bounds.Width / 2, chip.Bounds.Height / 2), harness.Window)!.Value;

        for (var click = 0; click < 2; click++)
        {
            harness.Window.MouseDown(point, MouseButton.Left);
            harness.Window.MouseUp(point, MouseButton.Left);
        }

        harness.Settle();

        Assert.True(OpenField(harness).IsFocused);
        Assert.Equal("**/*.log", OpenField(harness).Text);
    }

    [AvaloniaFact]
    public async Task Leaving_the_field_keeps_what_was_typed()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.Edit();
        FocusChip(harness, 0);

        Press(harness, Key.F2, PhysicalKey.F2);
        harness.Window.KeyTextInput("**/*.bak");
        harness.Find<TextBox>("NameField").Focus();
        harness.Settle();

        Assert.Equal(["**/*.bak", "SKSE/Plugins/*.log", "**/Crash Logs/**"], editor.IgnoreRules.Values);
        Assert.Equal("NameField", (harness.Window.FocusManager!.GetFocusedElement() as Control)?.Name);
    }
}
