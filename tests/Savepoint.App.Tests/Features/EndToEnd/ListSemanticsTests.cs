using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Shell;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Assistive technology gets a table for the profiles and lists for the history, with every row or entry named, so a person can
// move through them by item instead of reading the page as loose text.
public class ListSemanticsTests
{
    private static AutomationPeer PeerOf(Control control) =>
        ControlAutomationPeer.CreatePeerForElement(control) ?? throw new InvalidOperationException($"{control.Name} has no automation peer.");

    // The peers of a role under a control, outermost first; the toolkit puts plain layout panels between a list and its items.
    private static List<AutomationPeer> Items(Control control, AutomationControlType role)
    {
        var found = new List<AutomationPeer>();
        Collect(PeerOf(control), role, found);
        return found;
    }

    private static void Collect(AutomationPeer peer, AutomationControlType role, List<AutomationPeer> found)
    {
        foreach (var child in peer.GetChildren())
        {
            if (child.GetAutomationControlType() == role)
            {
                found.Add(child);
            }
            else
            {
                Collect(child, role, found);
            }
        }
    }

    [AvaloniaFact]
    public async Task The_profile_table_is_a_named_table_with_a_header_and_a_named_row_for_each_profile()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var table = app.Find<Border>("Table");

        Assert.Equal(AutomationControlType.Table, PeerOf(table).GetAutomationControlType());
        Assert.Equal("Profiles", PeerOf(table).GetName());
        Assert.Equal(AutomationControlType.Header, PeerOf(app.Find<Border>("TableHeader")).GetAutomationControlType());
        var rows = Items(table, AutomationControlType.DataItem);
        Assert.Equal(3, rows.Count);
        Assert.StartsWith("Modded playthrough - 7-2-26.", rows[0].GetName());
    }

    [AvaloniaFact]
    public async Task The_timeline_is_a_named_list_with_a_named_item_for_each_entry()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.History);
        app.History.SelectedViewOption = app.History.ViewOptions[1];
        app.Settle();
        var timeline = app.Find<ItemsControl>("TimelineList");

        Assert.Equal(AutomationControlType.List, PeerOf(timeline).GetAutomationControlType());
        Assert.Equal("Versions & mods", PeerOf(timeline).GetName());
        var items = Items(timeline, AutomationControlType.ListItem);
        Assert.Equal(app.History.Timeline.Count, items.Count);
        Assert.Equal("14 mod files updated, Modded playthrough - 7-2-26 · version 1.6.640, Jul 2, 2026 · 6:03 PM · in use now", items[0].GetName());
    }

    [AvaloniaFact]
    public async Task The_saves_are_a_named_list_and_the_loaded_one_says_so()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.History);
        app.History.SelectedViewOption = app.History.ViewOptions[1];
        app.Settle();
        var saves = app.Find<ItemsControl>("SavesList");

        Assert.Equal(AutomationControlType.List, PeerOf(saves).GetAutomationControlType());
        Assert.Equal("Saves", PeerOf(saves).GetName());
        var items = Items(saves, AutomationControlType.ListItem);
        Assert.Equal(app.History.Saves.Count, items.Count);
        Assert.Equal("Before the final boss, Jul 5, 11:42 PM · Modded playthrough - 7-2-26, Loaded", items[0].GetName());
        Assert.Equal("Cleared Blackreach, Jul 4, 8:15 PM · Modded playthrough - 7-2-26", items[1].GetName());
    }

    [AvaloniaFact]
    public async Task The_cards_are_lists_named_by_their_group_with_a_named_item_for_each_card()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.History);
        app.Settle();
        var groups = app.All<ItemsControl>().Where(list => list.DataContext is Savepoint.App.Features.History.PointGroupViewModel).ToList();

        Assert.Equal(["This week", "Earlier"], groups.Select(group => PeerOf(group).GetName()));
        Assert.All(groups, group => Assert.Equal(AutomationControlType.List, PeerOf(group).GetAutomationControlType()));
        var thisWeek = Items(groups[0], AutomationControlType.ListItem);
        Assert.Equal(app.History.Groups[0].Cards.Count, thisWeek.Count);
        Assert.Equal("Before the final boss, Save only · 1.6.640 with 212 mod files, Sun Jul 5, 11:42 PM, Loaded now", thisWeek[0].GetName());
        Assert.Equal("Cleared Blackreach, Save only · 1.6.640 with 212 mod files, Sat Jul 4, 8:15 PM", thisWeek[1].GetName());
    }
}
