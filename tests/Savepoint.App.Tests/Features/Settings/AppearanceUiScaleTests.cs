using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Settings;

// The Interface size control in Appearance: five steps, saved when chosen, applied by the shell at start and whenever settings change.
public class AppearanceUiScaleTests
{
    private static async Task<AppSettings> StoredAsync(SettingsRig rig) => FakeAssert.Ok(await rig.Backend.Settings.GetAsync());

    private static FakeBackend BackendWithScale(int percent) =>
        new(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { UiScalePercent = percent }));

    [Fact]
    public async Task The_shell_draws_the_interface_at_the_saved_scale_when_it_starts()
    {
        var rig = await ShellRig.StartAsync(BackendWithScale(150));

        Assert.Equal([150], rig.Theme.Scales);
    }

    [Fact]
    public async Task Without_a_saved_scale_the_shell_starts_at_100_percent()
    {
        var rig = await ShellRig.StartAsync();

        Assert.Equal([100], rig.Theme.Scales);
    }

    [Fact]
    public async Task A_scale_saved_elsewhere_is_applied_at_once()
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.SettingsChanged(SampleScenario.Settings with { UiScalePercent = 175 });

        Assert.Equal(175, rig.Theme.Scales[^1]);
    }

    [Fact]
    public async Task The_sizes_run_from_100_to_200_percent_in_steps_of_25()
    {
        var rig = await SettingsRig.ShownAsync();

        Assert.Equal([100, 125, 150, 175, 200], rig.Screen.Appearance.ScaleOptions.Select(option => option.Percent));
        Assert.Equal(["100%", "125%", "150%", "175%", "200%"], rig.Screen.Appearance.ScaleOptions.Select(option => option.Label));
    }

    [Fact]
    public async Task The_control_shows_the_saved_size()
    {
        var rig = await SettingsRig.ShownAsync(BackendWithScale(150));

        Assert.Equal(150, rig.Screen.Appearance.SelectedScale!.Percent);
    }

    [Fact]
    public async Task Without_a_saved_size_the_control_shows_100_percent()
    {
        var rig = await SettingsRig.ShownAsync();

        Assert.Equal(100, rig.Screen.Appearance.SelectedScale!.Percent);
    }

    [Fact]
    public async Task A_saved_size_that_is_not_a_step_shows_as_the_nearest_step()
    {
        var rig = await SettingsRig.ShownAsync(BackendWithScale(137));

        Assert.Equal(125, rig.Screen.Appearance.SelectedScale!.Percent);
    }

    [Fact]
    public async Task Choosing_a_size_saves_it_and_the_shell_applies_it_at_once()
    {
        var rig = await SettingsRig.ShownAsync();

        rig.Screen.Appearance.SelectedScale = rig.Screen.Appearance.ScaleOptions.Single(option => option.Percent == 200);

        Assert.Equal(200, (await StoredAsync(rig)).UiScalePercent);
        Assert.Equal(200, rig.Theme.Scales[^1]);
    }

    [Fact]
    public async Task Saving_a_size_leaves_every_other_setting_as_it_was()
    {
        var rig = await SettingsRig.ShownAsync();
        var before = await StoredAsync(rig);

        rig.Screen.Appearance.SelectedScale = rig.Screen.Appearance.ScaleOptions.Single(option => option.Percent == 125);

        Assert.Equal(before with { UiScalePercent = 125 }, await StoredAsync(rig));
    }

    [Fact]
    public async Task Choosing_the_size_already_in_use_saves_nothing()
    {
        var rig = await SettingsRig.ShownAsync();
        var saves = rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync));

        rig.Screen.Appearance.SelectedScale = rig.Screen.Appearance.ScaleOptions.Single(option => option.Percent == 100);

        Assert.Equal(saves, rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
    }

    [Fact]
    public async Task A_list_box_clearing_the_choice_while_it_rebuilds_changes_nothing()
    {
        var rig = await SettingsRig.ShownAsync(BackendWithScale(150));
        var saves = rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync));

        rig.Screen.Appearance.SelectedScale = null;

        Assert.Equal(150, rig.Screen.Appearance.SelectedScale!.Percent);
        Assert.Equal(saves, rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
    }

    // ---- Only the sizes the window's screen has room for

    private static async Task<SettingsRig> ShownOnScreenHoldingAsync(int largest, FakeBackend? backend = null)
    {
        var rig = await SettingsRig.StartAsync(backend);
        rig.Theme.ChangeLargestFittingScale(largest);
        rig.Show();
        return rig;
    }

    [Fact]
    public async Task A_size_the_screen_has_no_room_for_is_switched_off_with_the_reason()
    {
        var rig = await ShownOnScreenHoldingAsync(150);

        var options = rig.Screen.Appearance.ScaleOptions;
        Assert.All(options.Where(option => option.Percent <= 150), option => Assert.True(option.IsAvailable));
        Assert.All(options.Where(option => option.Percent > 150), option => Assert.False(option.IsAvailable));
        var off = options.Single(option => option.Percent == 175);
        Assert.Equal("Your screen is too small for this size.", off.Reason);
        Assert.Equal("175%, Your screen is too small for this size.", off.AccessibleName);
        Assert.Null(options.Single(option => option.Percent == 150).Reason);
        Assert.Equal("150%", options.Single(option => option.Percent == 150).AccessibleName);
    }

    [Fact]
    public async Task The_screen_says_why_some_sizes_are_off_only_while_some_are()
    {
        var rig = await SettingsRig.ShownAsync();
        Assert.False(rig.Screen.Appearance.HasUnavailableScales);

        rig.Theme.ChangeLargestFittingScale(125);

        Assert.True(rig.Screen.Appearance.HasUnavailableScales);
        Assert.Equal("Larger sizes need a bigger screen.", rig.Screen.Appearance.ScaleLimitNote);
    }

    [Fact]
    public async Task A_saved_size_the_screen_cannot_hold_shows_the_largest_that_fits_and_the_saved_choice_stays()
    {
        var rig = await ShownOnScreenHoldingAsync(125, BackendWithScale(200));
        var saves = rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync));

        Assert.Equal(125, rig.Screen.Appearance.SelectedScale!.Percent);
        Assert.Equal(200, (await StoredAsync(rig)).UiScalePercent);
        Assert.Equal(saves, rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
    }

    [Fact]
    public async Task The_saved_size_comes_back_when_the_screen_has_room_again()
    {
        var rig = await ShownOnScreenHoldingAsync(125, BackendWithScale(200));

        rig.Theme.ChangeLargestFittingScale(200);

        Assert.Equal(200, rig.Screen.Appearance.SelectedScale!.Percent);
        Assert.All(rig.Screen.Appearance.ScaleOptions, option => Assert.True(option.IsAvailable));
        Assert.Equal(200, (await StoredAsync(rig)).UiScalePercent);
    }

    [Fact]
    public async Task A_size_that_is_switched_off_cannot_be_chosen()
    {
        var rig = await ShownOnScreenHoldingAsync(125);
        var saves = rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync));

        rig.Screen.Appearance.SelectedScale = rig.Screen.Appearance.ScaleOptions.Single(option => option.Percent == 200);

        Assert.Equal(100, rig.Screen.Appearance.SelectedScale!.Percent);
        Assert.Equal(saves, rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
    }

    [Fact]
    public async Task The_shell_reports_the_largest_size_the_screen_holds_and_says_when_it_changes()
    {
        var rig = await ShellRig.StartAsync();
        Assert.Equal(200, rig.Shell.LargestFittingUiScale);

        var changes = rig.Changes(rig.Shell, () => rig.Theme.ChangeLargestFittingScale(150));

        Assert.Equal(150, rig.Shell.LargestFittingUiScale);
        Assert.Contains(nameof(IShellContext.LargestFittingUiScale), changes);
    }

    [Fact]
    public async Task A_failed_save_puts_the_control_back_and_says_so_without_changing_the_scale()
    {
        var rig = await SettingsRig.ShownAsync();
        var applied = rig.Theme.Scales.Count;
        rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.SaveAsync), ErrorInfo.From(new InvalidOperationException("The settings could not be saved.")));

        rig.Screen.Appearance.SelectedScale = rig.Screen.Appearance.ScaleOptions.Single(option => option.Percent == 150);

        Assert.Equal(100, rig.Screen.Appearance.SelectedScale!.Percent);
        Assert.Equal(applied, rig.Theme.Scales.Count);
        Assert.Equal(ToastKind.Error, Assert.Single(rig.Toasts.Toasts).Message.Kind);
    }
}
