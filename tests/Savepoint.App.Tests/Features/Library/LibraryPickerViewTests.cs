using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Features.Library;
using Savepoint.App.Tests.Features.EndToEnd;

namespace Savepoint.App.Tests.Features.Library;

public class LibraryPickerViewTests
{
    [AvaloniaFact]
    public async Task The_checklist_points_to_the_game_list_for_games_the_scan_cannot_see()
    {
        using var app = await AppStates.OpenAsync("library-picker-many");

        Assert.True(app.Find<Button>("TemplateListButton").IsEffectivelyVisible);
        Assert.Equal("Pick from the game list", app.Find<Button>("TemplateListButton").Content);
    }

    [AvaloniaFact]
    public async Task The_first_focus_lands_in_the_search_box_or_the_list_and_not_on_the_pointer_to_the_game_list()
    {
        using var app = await AppStates.OpenAsync("library-picker-many");

        var focused = Assert.IsAssignableFrom<Visual>(app.Window.FocusManager!.GetFocusedElement());

        var search = app.Find<TextBox>("SearchField");
        var list = app.Find<ItemsControl>("GameList");
        Assert.True(
            ReferenceEquals(focused, search) || focused.GetVisualAncestors().Contains(search) || focused.GetVisualAncestors().Contains(list),
            $"The focus is on {focused.GetType().Name} {(focused as Control)?.Name}.");
    }

    [AvaloniaFact]
    public async Task The_pointer_to_the_game_list_comes_after_the_list_in_the_tab_order()
    {
        using var app = await AppStates.OpenAsync("library-picker-many");

        var pointer = app.Find<Button>("TemplateListButton");
        var list = app.Find<ItemsControl>("GameList");
        var order = app.Window.GetVisualDescendants().OfType<Control>().ToList();

        Assert.True(order.IndexOf(pointer) > order.IndexOf(list));
    }

    [AvaloniaFact]
    public async Task A_list_of_two_hundred_games_draws_only_the_rows_that_can_be_seen()
    {
        using var app = await AppStates.OpenAsync("library-picker-many");

        var list = app.Find<ItemsControl>("GameList");
        var drawn = list.GetVisualDescendants().OfType<CheckBox>().Count();

        Assert.Equal(200, ((LibraryPickerDialogViewModel)app.Main.Dialogs.Current!).VisibleRows.Count);
        Assert.InRange(drawn, 1, 60);
    }
}
