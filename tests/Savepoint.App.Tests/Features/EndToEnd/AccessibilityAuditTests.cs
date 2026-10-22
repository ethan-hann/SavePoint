using Avalonia.Automation;
using Avalonia.Controls;
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

    // Focus lands on a dialog's first control, so the title is all a screen reader says unless the dialog also describes itself.
    public static TheoryData<string> DialogStates => [.. AppStates.Dialogs.Select(row => row.Data), "launch-progress", "launch-dialog-saving"];

    [AvaloniaTheory]
    [MemberData(nameof(DialogStates))]
    public async Task Every_dialog_describes_itself_to_a_screen_reader_as_well_as_naming_itself(string state)
    {
        using var app = await AppStates.OpenAsync(state);

        var chrome = app.Find<Border>("Chrome");
        var dialog = app.Main.Dialogs.Current!;

        Assert.False(string.IsNullOrWhiteSpace(dialog.Description), $"{state}: {dialog.GetType().Name} has no description.");
        Assert.Equal(dialog.Description, AutomationProperties.GetHelpText(chrome));
        Assert.Equal(dialog.Title, AutomationProperties.GetName(chrome));
    }
}
