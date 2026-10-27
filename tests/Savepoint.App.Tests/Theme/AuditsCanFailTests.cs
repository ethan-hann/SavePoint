using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Savepoint.App.Tests.Theme;

// The layout, automation and job pill audits are test helpers that every screen test trusts to say "no problems". Each is fed a tree
// that does have problems, so an edit that makes one report nothing fails here instead of passing everywhere.
public class AuditsCanFailTests
{
    private static Button Placed(string name, double left, double top, double width = 60, double height = 24)
    {
        var button = new Button { Name = name, Content = name, Width = width, Height = height };
        Canvas.SetLeft(button, left);
        Canvas.SetTop(button, top);
        return button;
    }

    private static void Redraw()
    {
        for (var frame = 0; frame < 3; frame++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void The_layout_audit_names_each_control_a_person_cannot_reach()
    {
        var outside = Placed("Outside", 500, 10);
        var offscreenCombo = new ComboBox { Name = "OffscreenCombo", Width = 120, Height = 32 };
        Canvas.SetLeft(offscreenCombo, 20);
        Canvas.SetTop(offscreenCombo, 600);
        var offscreenSwitch = new ToggleSwitch { Name = "OffscreenSwitch", Width = 80, Height = 32 };
        Canvas.SetLeft(offscreenSwitch, 300);
        Canvas.SetTop(offscreenSwitch, 700);

        var reachableInScroll = new Button { Name = "InScroll", Content = "InScroll", Width = 60, Height = 24, Margin = new Thickness(0, 500, 0, 0) };
        var scrollArea = new ScrollViewer { Name = "RoomyScroll", Width = 200, Height = 120, Content = new StackPanel { Children = { reachableInScroll } } };
        Canvas.SetLeft(scrollArea, 0);
        Canvas.SetTop(scrollArea, 0);

        var inSqueezed = new Button { Name = "InSqueezed", Content = "InSqueezed", Width = 60, Height = 24 };
        var squeezed = new ScrollViewer { Name = "Squeezed", Width = 150, Height = 50, Content = new StackPanel { Height = 800, Children = { inSqueezed } } };
        Canvas.SetLeft(squeezed, 210);
        Canvas.SetTop(squeezed, 0);

        var canvas = new Canvas { Children = { outside, offscreenCombo, offscreenSwitch, scrollArea, squeezed } };
        using var host = new ThemeHost(canvas, width: 400, height: 300);

        var problems = LayoutAudit.Problems(host.Window);

        Assert.Equal(4, problems.Count);
        Assert.Contains(problems, problem => problem.Contains("Button #Outside", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("ComboBox #OffscreenCombo", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("ToggleSwitch #OffscreenSwitch", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("ScrollViewer #Squeezed", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, problem => problem.Contains("InScroll", StringComparison.Ordinal) || problem.Contains("RoomyScroll", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void The_sideways_audit_names_what_spills_out_of_the_window_or_its_scroll_area_and_what_a_scroll_area_hides()
    {
        var outside = Placed("OutsideWindow", 360, 250);
        var inside = Placed("Fits", 20, 250);

        var wide = new TextBlock { Name = "WideLine", Text = "A line that is far too long for the area it is drawn in", FontSize = 14 };
        var clipped = new ScrollViewer { Name = "Clips", Width = 120, Height = 60, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { wide } } };
        Canvas.SetLeft(clipped, 0);
        Canvas.SetTop(clipped, 0);

        var wraps = new TextBlock { Name = "WrappedLine", Text = "A line that is far too long for the area it is drawn in", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var wrapArea = new ScrollViewer { Name = "Wraps", Width = 120, Height = 60, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = wraps };
        Canvas.SetLeft(wrapArea, 150);
        Canvas.SetTop(wrapArea, 0);

        var sideways = new ScrollViewer { Name = "ScrollsAcross", Width = 120, Height = 60, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = new Border { Width = 400, Height = 20 } };
        Canvas.SetLeft(sideways, 0);
        Canvas.SetTop(sideways, 100);

        var squeezedText = new TextBlock { Name = "SqueezedLine", Text = "Far more words than the column holds", TextWrapping = Avalonia.Media.TextWrapping.NoWrap };
        var column = new Grid { Name = "Column", Width = 40, ColumnDefinitions = { new ColumnDefinition(GridLength.Star) }, Children = { squeezedText } };
        Canvas.SetLeft(column, 150);
        Canvas.SetTop(column, 100);

        using var host = new ThemeHost(new Canvas { Children = { outside, inside, clipped, wrapArea, sideways, column } }, width: 400, height: 300);

        var problems = LayoutAudit.HorizontalProblems(host.Window);

        var all = string.Join(Environment.NewLine, problems);
        Assert.True(problems.Any(problem => problem.Contains("Button #OutsideWindow", StringComparison.Ordinal)), all);
        Assert.True(problems.Any(problem => problem.Contains("TextBlock #WideLine", StringComparison.Ordinal)), all);
        Assert.True(problems.Any(problem => problem.Contains("ScrollViewer #ScrollsAcross", StringComparison.Ordinal)), all);
        Assert.True(problems.Any(problem => problem.Contains("TextBlock #SqueezedLine", StringComparison.Ordinal)), all);
        Assert.DoesNotContain(problems, problem => problem.Contains("Fits", StringComparison.Ordinal) || problem.Contains("WrappedLine", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void The_automation_audit_names_each_control_a_screen_reader_cannot_use()
    {
        var nameless = new Button { Name = "Nameless" };
        var typeNamed = new Button { Name = "TypeNamed", Content = new Border() };
        var glyphOnly = new Button { Name = "GlyphOnly", Content = "" };
        var fine = new Button { Name = "Fine", Content = "Save" };
        AutomationProperties.SetName(fine, "Save");
        using var host = new ThemeHost(new StackPanel { Children = { nameless, typeNamed, glyphOnly, fine } }, width: 400, height: 300);

        var problems = AutomationAudit.Problems(host.Window);

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, problem => problem.Contains("#Nameless", StringComparison.Ordinal) && problem.Contains("no accessible name", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("#TypeNamed", StringComparison.Ordinal) && problem.Contains("named after a type", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("#GlyphOnly", StringComparison.Ordinal) && problem.Contains("icon glyph only", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, problem => problem.Contains("#Fine", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void The_job_pill_audit_names_the_control_under_the_pill_and_not_the_one_clear_of_it()
    {
        var under = Placed("Under", 20, 220, 80, 30);
        var clear = Placed("Clear", 250, 20);
        var pill = Placed("Pill", 10, 210, 200, 50);
        var canvas = new Canvas { Children = { under, clear, pill } };
        using var host = new ThemeHost(canvas, width: 400, height: 300);
        Redraw();

        var covered = JobPillAudit.Covered(host.Window, pill, scrolledToEnd: false).ToList();

        var only = Assert.Single(covered);
        Assert.Contains("'Under'", only, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void The_job_pill_audit_at_the_end_of_a_scroll_area_names_what_it_ends_with_and_leaves_pinned_controls_to_the_other_pass()
    {
        var inScroll = new Button { Name = "InScrollUnder", Content = "InScrollUnder", Width = 60, Height = 24, Margin = new Thickness(0, 215, 0, 0) };
        var words = new TextBlock { Name = "LastWords", Text = "The last words of the page", Margin = new Thickness(0, 10, 0, 0) };
        var farBelow = new Border { Height = 600 };
        var scrollArea = new ScrollViewer { Name = "Scrolls", Width = 200, Height = 300, Content = new StackPanel { Children = { inScroll, words, farBelow } } };
        Canvas.SetLeft(scrollArea, 0);
        Canvas.SetTop(scrollArea, 0);
        var pinned = Placed("PinnedUnder", 215, 230);
        var pill = Placed("Pill", 10, 210, 250, 50);
        using var host = new ThemeHost(new Canvas { Children = { scrollArea, pinned, pill } }, width: 400, height: 300);
        Redraw();

        var atEnd = JobPillAudit.Covered(host.Window, pill, scrolledToEnd: true).ToList();
        var pinnedView = JobPillAudit.Covered(host.Window, pill, scrolledToEnd: false).ToList();

        Assert.Equal(2, atEnd.Count);
        Assert.Contains(atEnd, problem => problem.Contains("'InScrollUnder'", StringComparison.Ordinal));
        Assert.Contains(atEnd, problem => problem.Contains("Text 'The last words", StringComparison.Ordinal));
        var pinnedOnly = Assert.Single(pinnedView);
        Assert.Contains("'PinnedUnder'", pinnedOnly, StringComparison.Ordinal);
    }
}
