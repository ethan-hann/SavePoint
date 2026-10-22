using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Theme;

// The selected segment of a toggle is told apart by more than a fill that is 1.1:1 against its neighbors: a primary line under it, and
// for the tray a stronger fill and a heavier weight.
public class SegmentSelectionTests
{
    private static (ListBox List, ThemeHost Host) Segments(string variant, ThemePreference theme = ThemePreference.Light)
    {
        var list = new ListBox { ItemsSource = new[] { "Sidebar", "Library", "Other" }, SelectedIndex = 1, Margin = new Thickness(20) };
        list.Classes.Add(variant);
        var host = new ThemeHost(list, theme, width: 320, height: 120);
        ((App)Application.Current!).Theme.ReduceMotion = true;
        Dispatcher.UIThread.RunJobs();
        return (list, host);
    }

    private static Border Mark(ListBoxItem item) =>
        item.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_SelectedMark");

    [AvaloniaTheory]
    [InlineData("segmented")]
    [InlineData("segmented-tray")]
    [InlineData("choice")]
    public void Only_the_selected_segment_draws_a_primary_line_under_itself(string variant)
    {
        var (list, host) = Segments(variant);
        using (host)
        {
            var items = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            var app = (App)Application.Current!;
            var primary = ((SolidColorBrush)app.Resources[ThemeKeys.Brush("Primary")]!).Color;

            Assert.Equal([0.0, 1.0, 0.0], items.Select(item => Mark(item).Opacity));
            Assert.Equal(2, Mark(items[1]).Height);
            Assert.Equal(primary, ((ISolidColorBrush)Mark(items[1]).Background!).Color);

            list.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([0.0, 0.0, 1.0], items.Select(item => Mark(item).Opacity));
        }
    }

    [AvaloniaTheory]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public void The_selected_segment_of_a_tray_has_the_secondary_container_fill_and_a_medium_weight(ThemePreference theme)
    {
        var (list, host) = Segments("segmented-tray", theme);
        using (host)
        {
            var items = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            var app = (App)Application.Current!;
            var fill = ((SolidColorBrush)app.Resources[ThemeKeys.Brush("SecondaryContainer")]!).Color;
            var text = ((SolidColorBrush)app.Resources[ThemeKeys.Brush("OnSecondaryContainer")]!).Color;

            var selectedFill = items[1].GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_Selected");
            Assert.Equal(fill, ((ISolidColorBrush)selectedFill.Background!).Color);
            Assert.Equal(text, ((ISolidColorBrush)items[1].Foreground!).Color);
            Assert.Equal(FontWeight.Medium, items[1].FontWeight);
            Assert.NotEqual(FontWeight.Medium, items[0].FontWeight);
        }
    }
}
