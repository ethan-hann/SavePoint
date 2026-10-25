using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Savepoint.App.Tests.Features.Shell;

public class SidebarFooterButtonTests
{
    private const double _tolerance = 1.5;
    private static readonly string[] _buttonNames = ["RailLibrariesButton", "RailAddButton"];

    private static IEnumerable<TextBlock> Labels(Button button) =>
        button.GetVisualDescendants().OfType<TextBlock>().Where(t => !t.Classes.Contains("icon") && t.IsVisible);

    [AvaloniaTheory]
    [InlineData(1040)]
    [InlineData(860)]
    public async Task Each_line_of_a_sidebar_button_label_is_centered_under_the_others(double width)
    {
        using var harness = await ShellViewHarness.StartAsync(width: width);

        foreach (var name in _buttonNames)
        {
            // The compact rail hides the labels, so there is nothing to measure there.
            foreach (var label in Labels(harness.Find<Button>(name)))
            {
                var lines = label.TextLayout.TextLines;
                foreach (var line in lines)
                {
                    var offset = label.Bounds.Width / 2 - (line.Start + line.Width / 2);
                    Assert.True(Math.Abs(offset) <= _tolerance, $"{name} at width {width}: a line of {lines.Count} is {offset} px off the label's center.");
                }
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(1040)]
    [InlineData(860)]
    public async Task The_icon_and_label_sit_centered_in_each_sidebar_button(double width)
    {
        using var harness = await ShellViewHarness.StartAsync(width: width);

        foreach (var name in _buttonNames)
        {
            var button = harness.Find<Button>(name);
            var group = Assert.IsAssignableFrom<Control>(button.Content);
            var left = group.TranslatePoint(default, button)!.Value.X;
            var right = button.Bounds.Width - left - group.Bounds.Width;
            Assert.True(Math.Abs(left - right) <= _tolerance, $"{name} at width {width} has gaps {left} left and {right} right.");
        }
    }
}
