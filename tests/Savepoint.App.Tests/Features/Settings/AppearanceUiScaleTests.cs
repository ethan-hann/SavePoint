using Savepoint.App.Features.Common.Toasts;
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
