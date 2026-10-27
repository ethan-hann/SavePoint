using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Shell;

// Focus follows a layout switch only when an arrow key made it; a click keeps the pointer's own behavior.
public class LayoutToggleFocusTests
{
    [Fact]
    public async Task A_switch_made_with_an_arrow_key_asks_for_focus_in_the_new_layout()
    {
        var rig = await ShellRig.StartAsync();
        var toggle = rig.Shell.LayoutToggle;
        var requests = 0;
        toggle.FocusRequested += (_, _) => requests++;

        toggle.BeginKeyboardChoice();
        toggle.Selected = toggle.Options.Single(option => option.Layout == ShellLayout.Library);
        toggle.EndKeyboardChoice();

        Assert.Equal(ShellLayout.Library, rig.Shell.Layout);
        Assert.True(requests >= 1, "No request for focus after a keyboard switch.");
    }

    [Fact]
    public async Task A_switch_made_with_the_pointer_does_not_ask_for_focus()
    {
        var rig = await ShellRig.StartAsync();
        var toggle = rig.Shell.LayoutToggle;
        var requests = 0;
        toggle.FocusRequested += (_, _) => requests++;

        toggle.Selected = toggle.Options.Single(option => option.Layout == ShellLayout.Library);

        Assert.Equal(ShellLayout.Library, rig.Shell.Layout);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task A_later_pointer_switch_is_not_taken_for_a_keyboard_one()
    {
        var rig = await ShellRig.StartAsync();
        var toggle = rig.Shell.LayoutToggle;
        toggle.BeginKeyboardChoice();
        toggle.EndKeyboardChoice();
        var requests = 0;
        toggle.FocusRequested += (_, _) => requests++;

        toggle.Selected = toggle.Options.Single(option => option.Layout == ShellLayout.Library);

        Assert.Equal(0, requests);
    }
}
