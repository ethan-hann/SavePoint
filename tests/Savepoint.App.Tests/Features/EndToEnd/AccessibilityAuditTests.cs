using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Every dialog the app can open describes itself to a screen reader. The names of the controls on every screen are audited with the
// other per-state audits in AppStateAuditTests.
public class AccessibilityAuditTests
{
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
