using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

// The profile table has rounded corners; its header and rows paint their own backgrounds and must stay inside them.
public class GamesCornerClippingTests
{
    [AvaloniaTheory]
    [InlineData(ThemePreference.Dark, true)]
    [InlineData(ThemePreference.Dark, false)]
    [InlineData(ThemePreference.Light, true)]
    [InlineData(ThemePreference.Light, false)]
    public async Task The_table_keeps_its_header_and_rows_inside_its_rounded_corners(ThemePreference theme, bool lastRowSelected)
    {
        using var harness = await GamesViewHarness.StartAsync(theme: theme);
        var table = harness.Find<Border>("Table");
        var rows = table.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("table-row")).ToList();
        foreach (var row in rows)
        {
            row.Classes.Set("selected", false);
        }

        var chosen = lastRowSelected ? rows[^1] : rows[0];
        chosen.Classes.Set("selected", true);
        harness.Settle();

        var saved = Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", "corners", $"games-table-{theme}-{(lastRowSelected ? "last" : "first")}.png");
        Assert.Empty(CornerProbe.RingFailures(harness.Window, table, saved));
    }

    [AvaloniaTheory]
    [InlineData(ThemePreference.Dark)]
    [InlineData(ThemePreference.Light)]
    public async Task A_library_card_keeps_its_art_inside_its_rounded_corners(ThemePreference theme)
    {
        using var harness = await GamesViewHarness.StartAsync(theme: theme, layout: ShellLayout.Library);
        var card = harness.Find<ListBox>("ShelfGames").GetVisualDescendants().OfType<ListBoxItem>().First();

        var saved = Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", "corners", $"games-shelf-card-{theme}.png");
        Assert.Empty(CornerProbe.RingFailures(harness.Window, card, saved));
    }
}
