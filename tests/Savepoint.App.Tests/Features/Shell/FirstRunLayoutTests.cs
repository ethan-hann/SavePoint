using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Shell;

public class FirstRunLayoutTests
{
    private const double _tolerance = 1.5;
    private static readonly string[] _buttonNames = ["AddFirstGameButton", "StartFromTemplateButton", "AddFromLibrariesButton"];

    private static FakeBackend Empty() => new(ScenarioBuilder.Empty().WithSettings(AppSettings.Defaults(@"D:\SavePoint")));

    private static Rect BoundsIn(Visual root, Visual child) =>
        new(child.TranslatePoint(default, root) ?? default, child.Bounds.Size);

    [AvaloniaTheory]
    [InlineData(1040)]
    [InlineData(760)]
    [InlineData(560)]
    [InlineData(420)]
    public async Task Each_line_of_start_buttons_is_centered_in_the_row(double width)
    {
        using var harness = await ShellViewHarness.StartAsync(Empty(), width: width);
        var buttons = _buttonNames.Select(harness.Find<Button>).ToList();
        var row = Assert.IsAssignableFrom<Panel>(buttons[0].GetVisualParent());
        var rowWidth = row.Bounds.Width;

        var lines = buttons
            .Select(b => BoundsIn(row, b))
            .GroupBy(r => Math.Round(r.Center.Y))
            .ToList();

        foreach (var line in lines)
        {
            var left = line.Min(r => r.Left);
            var right = rowWidth - line.Max(r => r.Right);
            Assert.True(Math.Abs(left - right) <= _tolerance, $"At width {width} a line has gaps {left} left and {right} right in a row {rowWidth} wide: {string.Join(" ", buttons.Select(b => BoundsIn(row, b)))}.");
        }
    }

    [AvaloniaFact]
    public async Task A_narrow_window_wraps_the_start_buttons_so_the_centering_check_is_meaningful()
    {
        using var harness = await ShellViewHarness.StartAsync(Empty(), width: 420);
        var rows = _buttonNames.Select(n => Math.Round(BoundsIn(harness.View, harness.Find<Button>(n)).Center.Y)).Distinct().Count();

        Assert.True(rows > 1);
    }

    [AvaloniaTheory]
    [InlineData(1040)]
    [InlineData(420)]
    public async Task The_label_and_icon_are_centered_inside_each_start_button(double width)
    {
        using var harness = await ShellViewHarness.StartAsync(Empty(), width: width);
        foreach (var name in _buttonNames.Append("HowItWorksLink"))
        {
            var button = harness.Find<Button>(name);
            // A button wider than its content is where an uncentered label would show.
            button.MinWidth = 330;
            harness.Window.UpdateLayout();
            var content = Assert.IsAssignableFrom<Control>(button.Content);
            var inner = BoundsIn(button, content);
            var left = inner.Left;
            var right = button.Bounds.Width - inner.Right;
            Assert.True(Math.Abs(left - right) <= _tolerance, $"{name} at width {width} has content gaps {left} left and {right} right.");
        }
    }
}
