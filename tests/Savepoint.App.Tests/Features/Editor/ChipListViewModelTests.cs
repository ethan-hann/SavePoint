using CommunityToolkit.Mvvm.Input;
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

        list.Items[1].RemoveCommand.Execute(null);

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

        list.Items[0].RemoveCommand.Execute(null);
        Assert.Equal([list.Items[0], list.Items[1], list], list.Entries);

        list.SetValues(["x"]);
        Assert.Equal([list.Items[0], list], list.Entries);
    }

    [Fact]
    public void Each_chip_is_named_for_editing_and_so_is_the_field_it_becomes()
    {
        var list = List("**/*.log");

        Assert.Equal("Edit **/*.log in Files to leave out", list.Items[0].EditLabel);
        Assert.Equal("Change **/*.log in Files to leave out", list.Items[0].EditFieldLabel);
    }

    [Fact]
    public void Editing_a_chip_opens_its_text_as_a_field()
    {
        var list = List("a", "b");

        list.Items[1].BeginEditCommand.Execute(null);

        Assert.True(list.Items[1].IsEditing);
        Assert.Equal("b", list.Items[1].EditText);
        Assert.False(list.Items[0].IsEditing);
    }

    [Fact]
    public void Committing_an_edit_changes_that_chip_in_place_and_says_so_once()
    {
        var list = List("a", "b", "c");
        var changes = 0;
        list.Changed += (_, _) => changes++;

        list.Items[1].BeginEditCommand.Execute(null);
        list.Items[1].EditText = "  b2  ";
        list.Items[1].CommitEditCommand.Execute(null);

        Assert.Equal(["a", "b2", "c"], list.Values);
        Assert.Equal(1, changes);
        Assert.All(list.Items, chip => Assert.False(chip.IsEditing));
        Assert.Equal([list.Items[0], list.Items[1], list.Items[2], list], list.Entries);
        Assert.Equal("Remove b2 from Files to leave out", list.Items[1].RemoveLabel);
    }

    [Fact]
    public void An_edited_chip_still_removes_itself()
    {
        var list = List("a", "b");
        list.Items[1].BeginEditCommand.Execute(null);
        list.Items[1].EditText = "b2";
        list.Items[1].CommitEditCommand.Execute(null);

        list.Items[1].RemoveCommand.Execute(null);

        Assert.Equal(["a"], list.Values);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    public void A_blank_or_repeated_edit_is_dropped_and_the_chip_keeps_its_value(string typed)
    {
        var list = List("a", "b");
        var changes = 0;
        list.Changed += (_, _) => changes++;

        list.Items[1].BeginEditCommand.Execute(null);
        list.Items[1].EditText = typed;
        list.Items[1].CommitEditCommand.Execute(null);

        Assert.Equal(["a", "b"], list.Values);
        Assert.False(list.Items[1].IsEditing);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void An_edit_that_leaves_the_value_as_it_was_changes_nothing()
    {
        var list = List("a", "b");
        var changes = 0;
        list.Changed += (_, _) => changes++;

        list.Items[0].BeginEditCommand.Execute(null);
        list.Items[0].EditText = " a ";
        list.Items[0].CommitEditCommand.Execute(null);

        Assert.Equal(["a", "b"], list.Values);
        Assert.False(list.Items[0].IsEditing);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Escape_cancels_the_edit_and_a_later_blur_changes_nothing()
    {
        var list = List("a", "b");
        var changes = 0;
        list.Changed += (_, _) => changes++;
        var chip = list.Items[0];

        chip.BeginEditCommand.Execute(null);
        chip.EditText = "z";
        chip.CancelEditCommand.Execute(null);
        chip.CommitEditCommand.Execute(null);

        Assert.Equal(["a", "b"], list.Values);
        Assert.False(chip.IsEditing);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Committing_when_no_edit_is_open_does_nothing()
    {
        var list = List("a", "b");
        list.Items[0].EditText = "stale";

        list.Items[0].CommitEditCommand.Execute(null);

        Assert.Equal(["a", "b"], list.Values);
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

    [Fact]
    public void Adding_values_appends_new_ones_in_order_skips_blank_and_repeated_ones_and_says_changed_once()
    {
        var list = List("a");
        var changes = 0;
        list.Changed += (_, _) => changes++;

        var added = list.AddValues(["b", " a ", "", "c", "b"]);

        Assert.Equal(["a", "b", "c"], list.Values);
        Assert.Equal(2, added);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Adding_only_values_already_there_says_nothing_changed()
    {
        var list = List("a");
        var changes = 0;
        list.Changed += (_, _) => changes++;

        var added = list.AddValues(["a"]);

        Assert.Equal(0, added);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Extra_actions_come_after_the_add_chip_in_the_same_row()
    {
        var action = new ChipListAction("Choose files…", "Choose files", new RelayCommand(() => { }));
        var list = new ChipListViewModel(_text, "Mod files", "Add a folder or file", "Data", actions: [action]);
        list.SetValues(["a", "b"]);

        Assert.Equal(["a", "b"], list.Entries.OfType<ChipItem>().Select(chip => chip.Text));
        Assert.Equal(2, list.Entries.IndexOf(list));
        Assert.Same(action, list.Entries[^1]);
        Assert.Equal(4, list.Entries.Count);
    }

    // A view keeps focus on a button only while that button stays in the row, so adding a chip must not rebuild the row.
    [Fact]
    public void Adding_values_slots_the_new_chips_in_before_the_add_chip_without_rebuilding_the_row()
    {
        var action = new ChipListAction("Choose files…", "Choose files", new RelayCommand(() => { }));
        var list = new ChipListViewModel(_text, "Mod files", "Add a folder or file", "Data", actions: [action]);
        list.SetValues(["a"]);
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        list.Entries.CollectionChanged += (_, e) => actions.Add(e.Action);

        list.AddValues(["b", "c"]);

        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, actions);
        Assert.Equal(["a", "b", "c"], list.Entries.OfType<ChipItem>().Select(chip => chip.Text));
        Assert.Same(list, list.Entries[3]);
        Assert.Same(action, list.Entries[4]);
    }
}
