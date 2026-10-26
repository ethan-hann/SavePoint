using System.Text.RegularExpressions;
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
    public static TheoryData<string, string> Choices => new()
    {
        { "editor-new", "LaunchChoiceList" },
        { "settings", "ThemeSelector" },
        { "settings", "LayoutCards" },
        { "settings", "AccentSwatches" },
        { "games-library", "Toggle" },
        { "history-cards", "ViewToggle" },
        { "activity-jobs", "ScopeToggle" },
        { "post-play-from-older-snapshot", "ChoiceList" },
    };

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
    [InlineData("editor-new", "LaunchChoiceList")]
    [InlineData("settings", "LayoutCards")]
    [InlineData("settings", "ThemeSelector")]
    public async Task Every_arrow_key_moves_the_choice_forward_or_back_and_the_ends_wrap_around(string state, string name)
    {
        using var app = await AppStates.OpenAsync(state);
        var list = app.Find<ListBox>(name);
        var last = list.ItemCount - 1;
        list.SelectedIndex = 0;
        list.GetVisualDescendants().OfType<ListBoxItem>().First().Focus(NavigationMethod.Tab);
        app.Settle();

        app.Press(Key.Right, PhysicalKey.ArrowRight);
        Assert.Equal(1, list.SelectedIndex);
        Assert.Same(list.ContainerFromIndex(1), app.Focused);

        app.Press(Key.Left, PhysicalKey.ArrowLeft);
        Assert.Equal(0, list.SelectedIndex);

        app.Press(Key.Down, PhysicalKey.ArrowDown);
        Assert.Equal(1, list.SelectedIndex);

        app.Press(Key.Up, PhysicalKey.ArrowUp);
        Assert.Equal(0, list.SelectedIndex);

        app.Press(Key.Left, PhysicalKey.ArrowLeft);
        Assert.Equal(last, list.SelectedIndex);

        app.Press(Key.Right, PhysicalKey.ArrowRight);
        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void No_choice_of_one_in_the_app_is_left_as_a_plain_list()
    {
        var choiceClasses = new Regex(@"<ListBox\b[^>]*\bClasses=""[^""]*\b(choice|segmented|segmented-tray|radio-cards|swatches)\b", RegexOptions.CultureInvariant);
        var source = Path.Combine(ThemeTestSupport.RepoRoot(), "src", "Savepoint.App");

        var plain = Directory.EnumerateFiles(source, "*.axaml", SearchOption.AllDirectories)
            .Where(file => choiceClasses.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(source, file))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(plain);
    }
}
