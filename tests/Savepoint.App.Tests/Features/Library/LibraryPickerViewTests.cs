using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Features.Library;
using Savepoint.App.Tests.Features.EndToEnd;

namespace Savepoint.App.Tests.Features.Library;

public class LibraryPickerViewTests
{
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
