using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Every screen and dialog in the states a person meets them is opened once and audited in place: the controls have names a screen
// reader can speak, the icon-only buttons are big enough to hit, and at each scaled work area (D36) every control is in view or behind
// a scroll area. The scaled sizes come last and only shrink the window, so the first audits see the window as it opened and each
// state keeps the scroll position its own opening gave it. None of the audits changes the state.
public class AppStateAuditTests
{
    private const double _smallest = 32;

    [AvaloniaTheory]
    [MemberData(nameof(AppStates.All), MemberType = typeof(AppStates))]
    public async Task Every_state_passes_the_naming_target_size_and_scaled_window_audits(string state)
    {
        using var app = await AppStates.OpenAsync(state);
        var problems = new List<string>();

        problems.AddRange(AutomationAudit.Problems(app.View!).Select(problem => $"[screen reader name] {problem}"));
        problems.AddRange(SmallIconButtons(app).Select(problem => $"[target size] {problem}"));

        foreach (var (name, width, height) in LayoutAudit.ScaledScreens.OrderByDescending(size => size.Width))
        {
            app.Window.Width = width;
            app.Window.Height = height;
            app.Settle();
            problems.AddRange(LayoutAudit.Problems(app.Window).Select(problem => $"[{name}, {width}x{height}] {problem}"));
        }

        Assert.True(problems.Count == 0, $"{state}:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    // An icon-only button is a small target for a hand that is not steady, so none is smaller than 32 by 32 anywhere in the app.
    private static IEnumerable<string> SmallIconButtons(ComposedApp app)
    {
        app.Settle();
        return app.All<Button>()
            .Where(button => button is not RepeatButton && button.Classes.Contains("icon") && button.IsEffectivelyVisible)
            .Where(button => button.Bounds.Width < _smallest - 0.5 || button.Bounds.Height < _smallest - 0.5)
            .Select(button => $"{Avalonia.Automation.AutomationProperties.GetName(button)} is {button.Bounds.Width:0}x{button.Bounds.Height:0}")
            .ToList();
    }
}
