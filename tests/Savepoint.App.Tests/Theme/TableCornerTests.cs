using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Theme;

// The shared table styles round the header and the last row off to the table's inner curve, whether the rows are direct children
// of the table's panel, are marked last, or come from an items control.
public class TableCornerTests
{
    public enum Shape
    {
        DirectChildren,
        MarkedLast,
        ItemsControl,
    }

    [AvaloniaTheory]
    [InlineData(ThemePreference.Dark, Shape.DirectChildren, true)]
    [InlineData(ThemePreference.Dark, Shape.DirectChildren, false)]
    [InlineData(ThemePreference.Light, Shape.DirectChildren, true)]
    [InlineData(ThemePreference.Dark, Shape.MarkedLast, true)]
    [InlineData(ThemePreference.Light, Shape.MarkedLast, false)]
    [InlineData(ThemePreference.Dark, Shape.ItemsControl, true)]
    [InlineData(ThemePreference.Light, Shape.ItemsControl, false)]
    public void A_table_keeps_its_header_and_rows_inside_its_rounded_corners(ThemePreference theme, Shape shape, bool lastRowSelected)
    {
        var rows = new[] { Row("First", selected: !lastRowSelected), Row("Second", selected: false), Row("Third", selected: lastRowSelected) };
        var body = new StackPanel();
        body.Children.Add(Header());
        switch (shape)
        {
            case Shape.DirectChildren:
                body.Children.AddRange(rows);
                break;
            case Shape.MarkedLast:
                rows[^1].Classes.Add("last");
                body.Children.AddRange(rows);
                break;
            case Shape.ItemsControl:
                var items = new ItemsControl { ItemsSource = rows };
                body.Children.Add(items);
                break;
        }

        var table = new Border { Classes = { "table" }, Child = body, Margin = new Avalonia.Thickness(20), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        using var host = new ThemeHost(table, theme, width: 300, height: 220);

        var saved = Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", "corners", $"table-{theme}-{shape}-{(lastRowSelected ? "last" : "first")}.png");
        Assert.Empty(CornerProbe.RingFailures(host.Window, table, saved));
    }

    private static Border Header() => new() { Classes = { "table-header" }, Child = new TextBlock { Text = "PROFILE" } };

    private static Border Row(string text, bool selected)
    {
        var row = new Border { Classes = { "table-row" }, Child = new TextBlock { Text = text } };
        row.Classes.Set("selected", selected);
        return row;
    }
}
