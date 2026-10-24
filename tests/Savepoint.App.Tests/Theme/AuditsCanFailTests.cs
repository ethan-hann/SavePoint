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
}
