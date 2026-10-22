using Avalonia.Controls;
using Avalonia.LogicalTree;
using Savepoint.App.Tests.Theme;
using Avalonia.Headless.XUnit;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.EndToEnd;

// The game's name is what tells a person which game the screen shows, so the header gives up its buttons' words before it gives
// up the name.
public class GamesHeaderTests
{
    private const double NameRoom = 240;

    // The default 1040 px window keeps the buttons' words, which leaves the name about 200 px.
    private const double NameRoomWithWords = 200;

    // Enough for about twenty characters of a profile name before the ellipsis.
    private const double ProfileNameRoom = 150;

    [AvaloniaTheory]
    [InlineData(640)]
    [InlineData(700)]
    [InlineData(760)]
    [InlineData(820)]
    [InlineData(900)]
    [InlineData(910)]
    [InlineData(960)]
    [InlineData(1040)]
    [InlineData(1100)]
    [InlineData(1200)]
    [InlineData(1400)]
    public async Task The_game_name_keeps_its_room_in_the_sidebar_layout(int width)
    {
        using var app = await ComposedApp.StartWithViewAsync(width: width, height: 660);
        app.Settle();
        app.Render("games", $"header-sidebar-{width}");

        var name = app.Find<TextBlock>("GameNameText");
        var room = width >= 1040 ? NameRoomWithWords : NameRoom;
        Assert.True(name.Bounds.Width >= room, $"At {width}px the name has {name.Bounds.Width:0}px, and needs {room}px.");
    }

    [AvaloniaTheory]
    [InlineData(640)]
    [InlineData(760)]
    [InlineData(900)]
    [InlineData(1400)]
    public async Task The_title_keeps_its_room_in_the_library_layout(int width)
    {
        using var app = await ComposedApp.StartWithViewAsync(width: width, height: 660, layout: ShellLayout.Library);
        app.Settle();
        app.Render("games", $"header-library-{width}");

        var title = app.Find<TextBlock>("LibraryTitleText");
        Assert.True(title.Bounds.Width >= NameRoom, $"At {width}px the title has {title.Bounds.Width:0}px.");
    }

    [AvaloniaFact]
    public async Task The_header_buttons_show_icons_only_when_the_name_needs_the_room_and_keep_their_names()
    {
        using var narrow = await ComposedApp.StartWithViewAsync(width: 900, height: 660);
        narrow.Settle();
        var edit = narrow.Find<Button>("EditGameButton");
        var newProfile = narrow.Find<Button>("NewProfileButton");

        Assert.False(Words(edit).IsEffectivelyVisible);
        Assert.False(Words(newProfile).IsEffectivelyVisible);
        Assert.False(ToggleLabel(narrow).IsEffectivelyVisible);
        Assert.Equal("Edit game", Avalonia.Automation.AutomationProperties.GetName(edit));
        Assert.Equal("New profile", Avalonia.Automation.AutomationProperties.GetName(newProfile));
        Assert.Equal("Edit game", ToolTip.GetTip(edit));
        Assert.Equal("New profile", ToolTip.GetTip(newProfile));
    }

    [AvaloniaFact]
    public async Task A_wide_header_keeps_the_words_and_needs_no_tooltips()
    {
        using var wide = await ComposedApp.StartWithViewAsync(width: 1400, height: 660);
        wide.Settle();
        var edit = wide.Find<Button>("EditGameButton");

        Assert.True(Words(edit).IsEffectivelyVisible);
        Assert.True(ToggleLabel(wide).IsEffectivelyVisible);
        Assert.Null(ToolTip.GetTip(edit));
    }

    private static TextBlock ToggleLabel(ComposedApp app) =>
        app.Find<ListBox>("Toggle").GetLogicalDescendants().OfType<TextBlock>().Single(text => text.Text == "Sidebar");

    private static TextBlock Words(Button button) =>
        button.GetLogicalDescendants().OfType<TextBlock>().Single(text => !text.Classes.Contains("icon"));

    [AvaloniaTheory]
    [InlineData(640)]
    [InlineData(700)]
    [InlineData(760)]
    [InlineData(900)]
    [InlineData(960)]
    [InlineData(1040)]
    [InlineData(1400)]
    public async Task A_long_profile_name_is_cut_short_instead_of_running_into_the_next_column_and_keeps_room_to_read(int width)
    {
        using var app = await ComposedApp.StartWithViewAsync(width: width, height: 660);
        app.Settle();
        app.Render("games", $"profile-names-{width}");

        var rows = app.Find<ItemsControl>("Rows");
        foreach (var row in rows.GetLogicalDescendants().OfType<Border>().Where(border => border.Classes.Contains("table-row")))
        {
            var cells = row.GetLogicalDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && !text.Classes.Contains("icon")).ToList();
            var title = cells.First(text => text.Classes.Contains("row-title"));
            var next = cells.Where(text => text != title && LayoutAudit.BoundsIn(app.Window, text).Left > LayoutAudit.BoundsIn(app.Window, title).Left)
                .OrderBy(text => LayoutAudit.BoundsIn(app.Window, text).Left)
                .First(text => text.Text is not null && text.Margin.Left == 14);
            var gap = LayoutAudit.BoundsIn(app.Window, next).Left - LayoutAudit.BoundsIn(app.Window, title).Right;
            Assert.True(gap >= 0, $"At {width}px \"{title.Text}\" runs {-gap:0}px into \"{next.Text}\".");
            Assert.True(title.Bounds.Width >= ProfileNameRoom, $"At {width}px the profile name has {title.Bounds.Width:0}px.");
        }
    }
}
