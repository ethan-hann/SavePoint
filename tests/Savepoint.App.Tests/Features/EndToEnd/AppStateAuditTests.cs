using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Every screen and dialog in the states a person meets them is opened once and audited in place: the controls have names a screen
// reader can speak, a dialog describes itself to one, the icon-only buttons are big enough to hit, at each scaled work area (D36)
// every control is in view or behind a scroll area, and at those and the smallest window the app allows (640x420, which is also the
// layout at any interface size on a screen that holds it) nothing scrolls sideways or draws past its container. The sizes come last and
// only shrink the window, so the first audits see the window as it opened and each state keeps the scroll position its own opening gave
// it. None of the audits changes the state.
public class AppStateAuditTests
{
    private const double _smallest = 32;

    private static readonly (string Name, double Width, double Height) _smallestWindow = ("smallest window", 640, 420);

    // Focus lands on a dialog's first control, so the title is all a screen reader says unless the dialog also describes itself.
    private static readonly HashSet<string> _dialogStates = [.. AppStates.Dialogs.Select(row => row.Data), "launch-progress", "launch-dialog-saving"];

    [AvaloniaTheory]
    [MemberData(nameof(AppStates.All), MemberType = typeof(AppStates))]
    public async Task Every_state_passes_the_naming_description_target_size_scaled_window_and_sideways_audits(string state)
    {
        using var app = await AppStates.OpenAsync(state);
        var problems = new List<string>();

        problems.AddRange(AutomationAudit.Problems(app.View!).Select(problem => $"[screen reader name] {problem}"));
        problems.AddRange(SmallIconButtons(app).Select(problem => $"[target size] {problem}"));
        if (_dialogStates.Contains(state))
        {
            problems.AddRange(DialogDescriptionProblems(app).Select(problem => $"[screen reader description] {problem}"));
        }

        foreach (var (name, width, height) in LayoutAudit.ScaledScreens.OrderByDescending(size => size.Width))
        {
            Resize(app, width, height);
            problems.AddRange(LayoutAudit.Problems(app.Window).Select(problem => $"[{name}, {width}x{height}] {problem}"));
            problems.AddRange(LayoutAudit.HorizontalProblems(app.Window).Select(problem => $"[sideways, {name}, {width}x{height}] {problem}"));
        }

        Resize(app, _smallestWindow.Width, _smallestWindow.Height);
        problems.AddRange(LayoutAudit.HorizontalProblems(app.Window).Select(problem => $"[sideways, {_smallestWindow.Name}, {_smallestWindow.Width}x{_smallestWindow.Height}] {problem}"));

        Assert.True(problems.Count == 0, $"{state}:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    private static void Resize(ComposedApp app, double width, double height)
    {
        app.Window.Width = width;
        app.Window.Height = height;
        app.Settle();
    }

    // An icon-only button is a small target for a hand that is not steady, so none is smaller than 32 by 32 anywhere in the app.
    private static IEnumerable<string> SmallIconButtons(ComposedApp app)
    {
        app.Settle();
        return app.All<Button>()
            .Where(button => button is not RepeatButton && button.Classes.Contains("icon") && button.IsEffectivelyVisible)
            .Where(button => button.Bounds.Width < _smallest - 0.5 || button.Bounds.Height < _smallest - 0.5)
            .Select(button => $"{AutomationProperties.GetName(button)} is {button.Bounds.Width:0}x{button.Bounds.Height:0}")
            .ToList();
    }

    private static IEnumerable<string> DialogDescriptionProblems(ComposedApp app)
    {
        var chrome = app.Find<Border>("Chrome");
        var dialog = app.Main.Dialogs.Current!;

        if (string.IsNullOrWhiteSpace(dialog.Description))
        {
            yield return $"{dialog.GetType().Name} has no description.";
        }

        if (dialog.Description != AutomationProperties.GetHelpText(chrome))
        {
            yield return $"{dialog.GetType().Name} says '{AutomationProperties.GetHelpText(chrome)}' to a screen reader, not its description '{dialog.Description}'.";
        }

        if (dialog.Title != AutomationProperties.GetName(chrome))
        {
            yield return $"{dialog.GetType().Name} is named '{AutomationProperties.GetName(chrome)}' to a screen reader, not its title '{dialog.Title}'.";
        }
    }
}
