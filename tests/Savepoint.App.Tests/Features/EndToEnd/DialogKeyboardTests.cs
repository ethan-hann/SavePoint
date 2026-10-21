using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Savepoint.App.Features.Launch;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Every dialog is operable from the keyboard alone: focus lands inside it, Tab stays inside it, and Escape closes the one on top.
public class DialogKeyboardTests
{
    [AvaloniaTheory]
    [MemberData(nameof(AppStates.Dialogs), MemberType = typeof(AppStates))]
    public async Task Escape_closes_the_dialog_on_top_and_nothing_else(string state)
    {
        using var app = await AppStates.OpenAsync(state);
        var top = app.Main.Dialogs.Current;
        var depth = app.Main.Dialogs.Stack.Count;
        Assert.NotNull(top);
        Assert.True(top.CanDismiss, $"{state}: the dialog cannot be dismissed while idle.");

        app.Press(Key.Escape, PhysicalKey.Escape);

        Assert.True(top.IsClosed, $"{state}: Escape left the dialog open.");
        Assert.Equal(depth - 1, app.Main.Dialogs.Stack.Count);
    }

    [AvaloniaTheory]
    [MemberData(nameof(AppStates.Dialogs), MemberType = typeof(AppStates))]
    public async Task Focus_starts_inside_the_dialog_and_tab_never_leaves_it(string state)
    {
        using var app = await AppStates.OpenAsync(state);

        Assert.True(IsInsideDialog(app), $"{state}: focus did not move into the dialog.");
        for (var i = 0; i < 12; i++)
        {
            app.Press(Key.Tab, PhysicalKey.Tab);
            Assert.True(IsInsideDialog(app), $"{state}: Tab {i + 1} left the dialog.");
        }
    }

    [AvaloniaFact]
    public async Task The_switch_dialog_cannot_be_dismissed_with_escape_because_its_way_out_is_cancel()
    {
        using var app = await AppStates.OpenAsync("launch-progress");
        var progress = app.Dialog<SwitchProgressDialogViewModel>();

        app.Press(Key.Escape, PhysicalKey.Escape);

        Assert.False(progress.IsClosed);
        Assert.False(progress.CanDismiss);
        Assert.Contains(app.All<Avalonia.Controls.Button>(), button => button.IsEffectivelyVisible && button.Name == "CancelButton");
    }

    private static bool IsInsideDialog(ComposedApp app) =>
        app.Focused is { } focused && Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(focused).Contains(app.Find<Avalonia.Controls.Border>("Chrome"));
}
