using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.Common;

// A choice of one among a few options (the launch choice, the theme, the layout toggles) is a radio group to assistive technology:
// a named group, each option a radio button that says whether it is selected, and the arrow keys move between the options.
public class ChoiceListSemanticsTests
{
    private static readonly (string State, string Name)[] _choices =
    [
        ("editor-new", "LaunchChoiceList"),
        ("settings", "ThemeSelector"),
        ("settings", "ScaleSelector"),
        ("settings", "LayoutCards"),
        ("settings", "AccentSwatches"),
        ("games-library", "Toggle"),
        ("history-cards", "ViewToggle"),
        ("activity-jobs", "ScopeToggle"),
        ("post-play-from-older-snapshot", "ChoiceList"),
    ];

    public static TheoryData<string, string> Choices => [.. _choices.Select(choice => (choice.State, choice.Name))];

    // The interface size stops at its ends: one key at 200% must not drop the window to 100%.
    public static TheoryData<string, string> WrappingChoices => [.. _choices.Where(choice => choice.Name != "ScaleSelector").Select(choice => (choice.State, choice.Name))];

    // The Sidebar/Library switch exists once in each layout's chrome; the one on screen is the one a key press reaches. After a key
    // switches the layout, the other copy is the one on screen, and it holds focus.
    private static ListBox Shown(Features.EndToEnd.ComposedApp app, string name) =>
        app.All<ListBox>().First(list => list.Name == name && list.IsEffectivelyVisible);

    private static AutomationPeer PeerOf(Control control) =>
        ControlAutomationPeer.CreatePeerForElement(control) ?? throw new InvalidOperationException($"{control.Name} has no automation peer.");

    // The options as assistive technology finds them: every peer under the list that can say whether it is selected.
    private static List<AutomationPeer> Options(AutomationPeer peer)
    {
        var found = new List<AutomationPeer>();
        foreach (var child in peer.GetChildren())
        {
            if (child is ISelectionItemProvider)
            {
                found.Add(child);
            }
            else
            {
                found.AddRange(Options(child));
            }
        }

        return found;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Choices))]
    public async Task A_choice_of_one_is_a_named_group_of_radio_buttons(string state, string name)
    {
        using var app = await AppStates.OpenAsync(state);
        var list = app.Find<ListBox>(name);

        var group = PeerOf(list);
        var options = Options(group);

        Assert.Equal(AutomationControlType.Group, group.GetAutomationControlType());
        Assert.False(string.IsNullOrWhiteSpace(group.GetName()));
        Assert.True(options.Count >= 2, $"{name} has {options.Count} options.");
        Assert.Equal(list.ItemCount, options.Count);
        Assert.All(options, option => Assert.Equal(AutomationControlType.RadioButton, option.GetAutomationControlType()));
        Assert.All(options, option => Assert.False(string.IsNullOrWhiteSpace(option.GetName())));
    }

    [AvaloniaTheory]
    [MemberData(nameof(Choices))]
    public async Task Each_option_says_whether_it_is_the_selected_one_and_follows_the_choice(string state, string name)
    {
        using var app = await AppStates.OpenAsync(state);
        var list = app.Find<ListBox>(name);
        var options = Options(PeerOf(list));
        Assert.Equal(list.ItemCount, options.Count);
        Assert.True(list.SelectedIndex >= 0);

        Assert.Equal(
            Enumerable.Range(0, options.Count).Select(index => index == list.SelectedIndex),
            options.Select(option => ((ISelectionItemProvider)option).IsSelected));

        var other = list.SelectedIndex == 0 ? 1 : 0;
        list.SelectedIndex = other;
        app.Settle();

        Assert.Equal(
            Enumerable.Range(0, options.Count).Select(index => index == other),
            options.Select(option => ((ISelectionItemProvider)option).IsSelected));
    }

    [AvaloniaTheory]
    [MemberData(nameof(Choices))]
    public async Task Each_option_is_a_toggle_that_is_on_for_the_selected_one_and_says_so_when_the_choice_changes(string state, string name)
    {
        using var app = await AppStates.OpenAsync(state);
        var list = app.Find<ListBox>(name);
        var options = Options(PeerOf(list));
        Assert.Equal(list.ItemCount, options.Count);
        var toggles = options.Select(option => Assert.IsAssignableFrom<IToggleProvider>(option)).ToList();
        var other = list.SelectedIndex == 0 ? 1 : 0;
        var raised = new List<(int Index, object? NewValue)>();
        foreach (var (option, index) in options.Select((option, index) => (option, index)))
        {
            option.PropertyChanged += (_, e) =>
            {
                if (e.Property == TogglePatternIdentifiers.ToggleStateProperty)
                {
                    raised.Add((index, e.NewValue));
                }
            };
        }

        Assert.Equal(
            Enumerable.Range(0, toggles.Count).Select(index => index == list.SelectedIndex ? ToggleState.On : ToggleState.Off),
            toggles.Select(toggle => toggle.ToggleState));

        list.SelectedIndex = other;
        app.Settle();

        Assert.Equal(ToggleState.On, toggles[other].ToggleState);
        Assert.Contains(raised, change => change.Index == other && Equals(change.NewValue, ToggleState.On));
        Assert.Equal(1, toggles.Count(toggle => toggle.ToggleState == ToggleState.On));
    }

    [AvaloniaTheory]
    [MemberData(nameof(Choices))]
    public async Task Toggling_an_option_chooses_it(string state, string name)
    {
        using var app = await AppStates.OpenAsync(state);
        var list = app.Find<ListBox>(name);
        var toggles = Options(PeerOf(list)).Select(option => Assert.IsAssignableFrom<IToggleProvider>(option)).ToList();
        var other = list.SelectedIndex == 0 ? 1 : 0;

        toggles[other].Toggle();
        app.Settle();

        Assert.Equal(other, list.SelectedIndex);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Choices))]
    public async Task The_group_keeps_the_selection_pattern_and_is_the_container_of_each_option(string state, string name)
    {
        using var app = await AppStates.OpenAsync(state);
        var list = app.Find<ListBox>(name);
        var group = PeerOf(list);

        var selection = Assert.IsAssignableFrom<ISelectionProvider>(group);
        Assert.False(selection.CanSelectMultiple);
        Assert.Equal(list.SelectedIndex, Options(group).FindIndex(option => selection.GetSelection().Contains(option)));
        Assert.All(Options(group), option => Assert.Same(group, Assert.IsAssignableFrom<ISelectionItemProvider>(option).SelectionContainer));
    }

    [AvaloniaTheory]
    [InlineData("settings", "ThemeSelector")]
    [InlineData("settings", "AccentSwatches")]
    public async Task Tab_enters_the_group_at_the_selected_option_and_leaves_it_in_one_press(string state, string name)
    {
        using var app = await AppStates.OpenAsync(state);
        var list = app.Find<ListBox>(name);
        list.SelectedIndex = 1;
        app.Settle();
        ((Control)list.ContainerFromIndex(1)!).Focus(NavigationMethod.Tab);
        app.Settle();

        app.Press(Key.Tab, PhysicalKey.Tab);
        var after = app.Focused;
        Assert.False(after is Visual left && (ReferenceEquals(left, list) || left.GetVisualAncestors().Contains(list)), "One Tab press stayed in the group.");

        app.Press(Key.Tab, PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Same(list.ContainerFromIndex(1), app.Focused);
    }

    [AvaloniaFact]
    public async Task A_disabled_option_is_skipped_by_the_arrow_keys()
    {
        using var app = await AppStates.OpenAsync("settings");
        var list = app.Find<ListBox>("ThemeSelector");
        list.SelectedIndex = 0;
        app.Settle();
        ((Control)list.ContainerFromIndex(1)!).IsEnabled = false;
        ((Control)list.ContainerFromIndex(0)!).Focus(NavigationMethod.Tab);
        app.Settle();

        app.Press(Key.Right, PhysicalKey.ArrowRight);

        Assert.Equal(2, list.SelectedIndex);
        Assert.Same(list.ContainerFromIndex(2), app.Focused);
    }

    [AvaloniaFact]
    public async Task In_a_right_to_left_layout_Left_goes_forward_and_Right_goes_back_while_Down_still_goes_forward()
    {
        using var app = await AppStates.OpenAsync("settings");
        var list = app.Find<ListBox>("ThemeSelector");
        list.FlowDirection = Avalonia.Media.FlowDirection.RightToLeft;
        list.SelectedIndex = 1;
        app.Settle();
        ((Control)list.ContainerFromIndex(1)!).Focus(NavigationMethod.Tab);
        app.Settle();

        app.Press(Key.Left, PhysicalKey.ArrowLeft);
        Assert.Equal(2, list.SelectedIndex);

        app.Press(Key.Right, PhysicalKey.ArrowRight);
        Assert.Equal(1, list.SelectedIndex);

        app.Press(Key.Down, PhysicalKey.ArrowDown);
        Assert.Equal(2, list.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task An_arrow_key_pressed_in_the_name_field_of_a_post_play_card_stays_in_the_field()
    {
        using var app = await AppStates.OpenAsync("launch-post-play");
        var list = app.Find<ListBox>("ChoiceList");
        var field = list.GetVisualDescendants().OfType<TextBox>().First(box => box.IsEffectivelyVisible);
        field.Focus(NavigationMethod.Tab);
        app.Settle();
        var chosen = list.SelectedIndex;

        app.Press(Key.Down, PhysicalKey.ArrowDown);
        app.Press(Key.Right, PhysicalKey.ArrowRight);

        Assert.Equal(chosen, list.SelectedIndex);
        Assert.Same(field, app.Focused);
    }

    [AvaloniaFact]
    public async Task The_size_choice_stays_on_the_last_size_it_can_reach_instead_of_wrapping_to_the_first()
    {
        using var app = await AppStates.OpenAsync("settings");
        var list = app.Find<ListBox>("ScaleSelector");
        list.SelectedIndex = 2;
        app.Settle();
        ((Control)list.ContainerFromIndex(3)!).IsEnabled = false;
        ((Control)list.ContainerFromIndex(4)!).IsEnabled = false;
        ((Control)list.ContainerFromIndex(2)!).Focus(NavigationMethod.Tab);
        app.Settle();

        app.Press(Key.Right, PhysicalKey.ArrowRight);
        app.Press(Key.Down, PhysicalKey.ArrowDown);

        Assert.Equal(2, list.SelectedIndex);
        Assert.Same(list.ContainerFromIndex(2), app.Focused);
    }

    [AvaloniaFact]
    public async Task The_size_choice_stays_on_the_smallest_size_when_Left_is_pressed_there_and_still_steps_forward()
    {
        using var app = await AppStates.OpenAsync("settings");
        var list = app.Find<ListBox>("ScaleSelector");
        list.SelectedIndex = 0;
        app.Settle();
        ((Control)list.ContainerFromIndex(0)!).Focus(NavigationMethod.Tab);
        app.Settle();

        app.Press(Key.Left, PhysicalKey.ArrowLeft);
        Assert.Equal(0, list.SelectedIndex);
        Assert.Same(list.ContainerFromIndex(0), app.Focused);

        app.Press(Key.Right, PhysicalKey.ArrowRight);
        Assert.Equal(1, list.SelectedIndex);
    }

    [AvaloniaTheory]
    [MemberData(nameof(WrappingChoices))]
    public async Task Every_arrow_key_moves_the_choice_forward_or_back_and_the_ends_wrap_around(string state, string name)
    {
        using var app = await AppStates.OpenAsync(state);
        var first = Shown(app, name);
        var last = first.ItemCount - 1;
        first.SelectedIndex = 0;
        app.Settle();

        // The layout switch swaps the whole view when its choice changes, so the list is found again and given focus before each key.
        ListBox Press(Key key, PhysicalKey physical)
        {
            var current = Shown(app, name);
            ((Control)current.ContainerFromIndex(current.SelectedIndex)!).Focus(NavigationMethod.Tab);
            app.Settle();
            app.Press(key, physical);
            return Shown(app, name);
        }

        Assert.Equal(1, Press(Key.Right, PhysicalKey.ArrowRight).SelectedIndex);
        Assert.Equal(0, Press(Key.Left, PhysicalKey.ArrowLeft).SelectedIndex);
        Assert.Equal(1, Press(Key.Down, PhysicalKey.ArrowDown).SelectedIndex);
        Assert.Equal(0, Press(Key.Up, PhysicalKey.ArrowUp).SelectedIndex);
        Assert.Equal(last, Press(Key.Left, PhysicalKey.ArrowLeft).SelectedIndex);
        Assert.Equal(0, Press(Key.Right, PhysicalKey.ArrowRight).SelectedIndex);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Choices))]
    public async Task An_arrow_key_leaves_focus_on_the_option_it_chose(string state, string name)
    {
        using var app = await AppStates.OpenAsync(state);
        Shown(app, name).SelectedIndex = 0;
        app.Settle();
        // Choosing the first option can swap the layout's chrome (the layout toggle), so the list to focus is found again.
        var shown = Shown(app, name);
        ((Control)shown.ContainerFromIndex(0)!).Focus(NavigationMethod.Tab);
        app.Settle();

        app.Press(Key.Right, PhysicalKey.ArrowRight);

        var list = Shown(app, name);
        Assert.Same(list.ContainerFromIndex(1), app.Focused);
    }

    [AvaloniaFact]
    public async Task Arrow_keys_on_the_layout_toggle_keep_switching_the_layout_without_the_player_touching_the_mouse()
    {
        using var app = await AppStates.OpenAsync("games-library");
        // The state opens in the library layout; choosing Sidebar swaps the chrome, so the toggle to focus is found again afterwards.
        Shown(app, "Toggle").SelectedIndex = 0;
        app.Settle();
        var start = Shown(app, "Toggle");
        ((Control)start.ContainerFromIndex(0)!).Focus(NavigationMethod.Tab);
        app.Settle();

        // Each press is sent with no re-focusing in between: focus has to be where the last one left it.
        app.Press(Key.Right, PhysicalKey.ArrowRight);
        var library = Shown(app, "Toggle");
        Assert.Equal(1, library.SelectedIndex);
        Assert.Same(library.ContainerFromIndex(1), app.Focused);

        app.Press(Key.Left, PhysicalKey.ArrowLeft);
        var sidebar = Shown(app, "Toggle");
        Assert.Equal(0, sidebar.SelectedIndex);
        Assert.Same(sidebar.ContainerFromIndex(0), app.Focused);
        Assert.NotSame(library, sidebar);
    }

    private static readonly Regex _plainChoiceList = new(@"<ListBox\b[^>]*\bClasses=""[^""]*\b(choice|segmented|segmented-tray|radio-cards|swatches)\b", RegexOptions.CultureInvariant);

    // True when a view declares a choice of one the plain way: a ListBox styled as a choice, a plain ListBoxItem inside a ChoiceListBox,
    // or a plain ListBox inside a flyout (a flyout that holds a list is a choice of one; a list of commands is a MenuFlyout).
    private static bool DeclaresPlainChoice(string axaml) => _plainChoiceList.IsMatch(axaml) || _plainOption.IsMatch(axaml) || _plainListInFlyout.IsMatch(axaml);

    // A <ListBox declared between a Flyout's start tag and its end tag.
    private static readonly Regex _plainListInFlyout = new(@"<(?:\w+:)?Flyout\b[^>]*(?<!/)>(?:(?!</(?:\w+:)?Flyout>)[\s\S])*?<ListBox\b", RegexOptions.CultureInvariant);

    // A <ListBoxItem declared between a ChoiceListBox's start tag and its end tag.
    private static readonly Regex _plainOption = new(@"<(?:\w+:)?ChoiceListBox\b[^>]*(?<!/)>(?:(?!</(?:\w+:)?ChoiceListBox>)[\s\S])*?<ListBoxItem\b", RegexOptions.CultureInvariant);

    [Theory]
    [InlineData("<ListBox x:Name=\"A\" Classes=\"segmented\"></ListBox>", true)]
    [InlineData("<ListBox Classes=\"radio-cards layout-cards\" />", true)]
    [InlineData("<a11y:ChoiceListBox Classes=\"choice\"><ListBoxItem x:Name=\"A\" /></a11y:ChoiceListBox>", true)]
    [InlineData("<a11y:ChoiceListBox Classes=\"choice\"><a11y:ChoiceListBox.Styles><Style Selector=\"ListBoxItem\" /></a11y:ChoiceListBox.Styles><a11y:ChoiceListBoxItem /></a11y:ChoiceListBox>", false)]
    [InlineData("<a11y:ChoiceListBox Classes=\"segmented\" /><ListBox Classes=\"rows\"><ListBoxItem /></ListBox>", false)]
    [InlineData("<Button.Flyout><Flyout Placement=\"Bottom\"><ListBox x:Name=\"FilterList\" Classes=\"rows\" /></Flyout></Button.Flyout>", true)]
    [InlineData("<Flyout>\n  <!-- text -->\n  <ListBox>\n    <ListBoxItem />\n  </ListBox>\n</Flyout>", true)]
    [InlineData("<Flyout><a11y:ChoiceListBox Classes=\"rows\" /></Flyout>", false)]
    [InlineData("<Flyout><StackPanel /></Flyout><ListBox Classes=\"rows\" />", false)]
    [InlineData("<MenuFlyout><MenuItem Header=\"Edit\" /></MenuFlyout><ListBox Classes=\"rows\" />", false)]
    public void The_lint_finds_a_plain_choice_list_or_a_plain_option_in_a_choice_list(string axaml, bool expected)
    {
        Assert.Equal(expected, DeclaresPlainChoice(axaml));
    }

    [Fact]
    public void No_choice_of_one_in_the_app_is_left_as_a_plain_list()
    {
        var source = Path.Combine(ThemeTestSupport.RepoRoot(), "src", "Savepoint.App");

        var plain = Directory.EnumerateFiles(source, "*.axaml", SearchOption.AllDirectories)
            .Where(file => DeclaresPlainChoice(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(source, file))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(plain);
    }
}
