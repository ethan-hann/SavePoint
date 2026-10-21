using Avalonia.Headless.XUnit;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Every screen, and every dialog the app can open, in the states a person meets them: each control that can be acted on has a name a
// screen reader can speak.
public class AccessibilityAuditTests
{
    [AvaloniaTheory]
    [MemberData(nameof(AppStates.All), MemberType = typeof(AppStates))]
    public async Task Every_control_a_person_can_act_on_has_a_name_a_screen_reader_can_speak(string state)
    {
        using var app = await AppStates.OpenAsync(state);

        var problems = AutomationAudit.Problems(app.View!);

        Assert.True(problems.Count == 0, $"{state}:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }
}
