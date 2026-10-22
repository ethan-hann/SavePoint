using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Savepoint.App.Features.Shell;

namespace Savepoint.App.Tests.Features.EndToEnd;

// The History filter is a list in a flyout: arrow keys move through it, Enter chooses, and only choosing closes it.
public class FilterFlyoutKeyboardTests
{
    [AvaloniaFact]
    public async Task Arrow_keys_move_through_the_profiles_without_closing_the_list_and_Enter_chooses_and_closes()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.History);
        app.Settle();
        var button = app.Find<Button>("FilterButton");
        button.Flyout!.ShowAt(button);
        app.Settle();
        var list = Assert.IsType<ListBox>(Assert.IsType<Flyout>(button.Flyout).Content);
        list.ContainerFromIndex(0)!.Focus(NavigationMethod.Tab);
        app.Settle();

        app.Press(Key.Down, PhysicalKey.ArrowDown);
        app.Press(Key.Down, PhysicalKey.ArrowDown);

        Assert.True(button.Flyout.IsOpen, "An arrow key closed the list.");
        Assert.Equal(2, list.SelectedIndex);

        app.Press(Key.Enter, PhysicalKey.Enter);

        Assert.Equal(app.History.Filters[2], app.History.SelectedFilter);
        Assert.False(button.Flyout.IsOpen, "Enter chose the profile but left the list open.");
    }

    [AvaloniaFact]
    public async Task Escape_after_arrowing_closes_the_list_and_keeps_the_filter_that_was_in_force()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.History);
        app.Settle();
        var button = app.Find<Button>("FilterButton");
        button.Flyout!.ShowAt(button);
        app.Settle();
        var list = Assert.IsType<ListBox>(Assert.IsType<Flyout>(button.Flyout).Content);
        list.ContainerFromIndex(0)!.Focus(NavigationMethod.Tab);
        app.Settle();
        app.Press(Key.Down, PhysicalKey.ArrowDown);

        app.Press(Key.Escape, PhysicalKey.Escape);

        Assert.False(button.Flyout.IsOpen);
        Assert.Equal(app.History.Filters[0], app.History.SelectedFilter);
    }

    [AvaloniaFact]
    public async Task A_click_on_a_profile_chooses_it_and_closes_the_list()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.History);
        app.Settle();
        var button = app.Find<Button>("FilterButton");
        button.Flyout!.ShowAt(button);
        app.Settle();
        var list = Assert.IsType<ListBox>(Assert.IsType<Flyout>(button.Flyout).Content);
        var third = list.ContainerFromIndex(2)!;
        var point = third.TranslatePoint(new Point(20, 10), app.Window)!.Value;

        app.Window.MouseDown(point, MouseButton.Left);
        app.Window.MouseUp(point, MouseButton.Left);
        app.Settle();

        Assert.Equal(app.History.Filters[2], app.History.SelectedFilter);
        Assert.False(button.Flyout.IsOpen);
    }
}
