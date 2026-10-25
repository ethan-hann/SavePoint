using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Editor;
using Savepoint.App.Localization;

namespace Savepoint.App.Tests.Features.Editor;

public class ChipListViewModelTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    private static ChipListViewModel List(params string[] values)
    {
        var list = new ChipListViewModel(_text, "Files to leave out", "Add a rule", "**/*.log");
        list.SetValues(values);
        return list;
    }

    [Fact]
    public void Values_show_as_chips_in_order()
    {
        var list = List("**/*.log", "SKSE/Plugins/*.log");

        Assert.Equal(["**/*.log", "SKSE/Plugins/*.log"], list.Values);
        Assert.Equal(["**/*.log", "SKSE/Plugins/*.log"], list.Items.Select(chip => chip.Text));
    }

    [Fact]
    public void Setting_values_trims_and_drops_blank_and_repeated_ones()
    {
        var list = List("  **/*.log ", "", "   ", "**/*.log", "Data");

        Assert.Equal(["**/*.log", "Data"], list.Values);
    }

    [Fact]
    public void Removing_a_chip_takes_that_value_out_and_says_so()
    {
        var list = List("a", "b", "c");
        var changes = 0;
        list.Changed += (_, _) => changes++;

        list.Items[1].Remove.Execute(null);

        Assert.Equal(["a", "c"], list.Values);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Each_remove_button_is_named_after_its_value_and_its_list()
    {
        var list = List("**/*.log");

        Assert.Equal("Remove **/*.log from Files to leave out", list.Items[0].RemoveLabel);
    }

    [Fact]
    public void The_add_chip_and_field_take_their_own_accessible_name_and_the_list_its_help_text()
    {
        var plain = List();
        var named = new ChipListViewModel(_text, "Files to leave out", "Add a rule", "**/*.log", "Add a rule for the game's own folder", "Only logs.");

        Assert.Equal("Add a rule", plain.AddAutomationName);
        Assert.Null(plain.HelpText);
        Assert.Equal("Add a rule for the game's own folder", named.AddAutomationName);
        Assert.Equal("Add a rule", named.AddLabel);
        Assert.Equal("Only logs.", named.HelpText);
    }

    [Fact]
    public void The_add_chip_opens_an_empty_field_and_enter_commits_the_trimmed_value()
    {
        var list = List("a");
        var changes = 0;
        list.Changed += (_, _) => changes++;

        list.BeginAddCommand.Execute(null);
        Assert.True(list.IsAdding);
        Assert.Equal(string.Empty, list.NewText);

        list.NewText = "  **/Crash Logs/**  ";
        list.CommitAddCommand.Execute(null);

        Assert.Equal(["a", "**/Crash Logs/**"], list.Values);
        Assert.False(list.IsAdding);
        Assert.Equal(string.Empty, list.NewText);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_blank_or_repeated_value_is_dropped_without_a_change()
    {
        var list = List("a");
        var changes = 0;
        list.Changed += (_, _) => changes++;

        list.BeginAddCommand.Execute(null);
        list.NewText = "   ";
        list.CommitAddCommand.Execute(null);
        list.BeginAddCommand.Execute(null);
        list.NewText = "a";
        list.CommitAddCommand.Execute(null);

        Assert.Equal(["a"], list.Values);
        Assert.False(list.IsAdding);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Escape_cancels_the_add_and_a_later_blur_adds_nothing()
    {
        var list = List("a");
        list.BeginAddCommand.Execute(null);
        list.NewText = "b";

        list.CancelAddCommand.Execute(null);
        list.CommitAddCommand.Execute(null);

        Assert.Equal(["a"], list.Values);
        Assert.False(list.IsAdding);
        Assert.Equal(string.Empty, list.NewText);
    }

    [Fact]
    public void Committing_when_no_add_is_open_does_nothing()
    {
        var list = List("a");
        list.NewText = "stale";

        list.CommitAddCommand.Execute(null);

        Assert.Equal(["a"], list.Values);
    }

    [Fact]
    public void The_entries_are_the_chips_in_order_then_the_add_chip_last_however_the_list_changes()
    {
        var list = List("a", "b");
        Assert.Equal([list.Items[0], list.Items[1], list], list.Entries);

        list.BeginAddCommand.Execute(null);
        list.NewText = "c";
        list.CommitAddCommand.Execute(null);
        Assert.Equal([list.Items[0], list.Items[1], list.Items[2], list], list.Entries);

        list.Items[0].Remove.Execute(null);
        Assert.Equal([list.Items[0], list.Items[1], list], list.Entries);

        list.SetValues(["x"]);
        Assert.Equal([list.Items[0], list], list.Entries);
    }

    [Fact]
    public void Setting_values_replaces_the_list_without_raising_changed()
    {
        var list = List("a");
        var changes = 0;
        list.Changed += (_, _) => changes++;

        list.SetValues(["x", "y"]);

        Assert.Equal(["x", "y"], list.Values);
        Assert.Equal(0, changes);
    }
}
