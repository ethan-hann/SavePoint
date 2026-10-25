using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Games;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Three places write settings: the layout toggle (the shell), the Settings screen and the launch dialog's "don't ask again".
// Each change must land on what is stored at that moment, never on a copy that went stale.
public class SettingsWritersTests
{
    private static async Task<AppSettings> StoredAsync(ComposedApp app, CancellationToken cancel) =>
        FakeAssert.Ok(await app.Backend.Settings.GetAsync(cancel));

    // A write nobody is told about, the way a stale copy comes about.
    private static async Task ChangeBehindTheirBacksAsync(ComposedApp app, Func<AppSettings, AppSettings> change, CancellationToken cancel) =>
        FakeAssert.Ok(await app.Backend.Settings.SaveAsync(change(await StoredAsync(app, cancel))));

    [Fact]
    public async Task The_shell_and_the_settings_screen_keep_what_was_saved_since_they_last_looked()
    {
        var cancel = TestContext.Current.CancellationToken;
        using var app = await ComposedApp.StartAsync();
        var settings = app.Settings;

        await app.Shell.SetLayoutAsync(ShellLayout.Library);
        await ChangeBehindTheirBacksAsync(app, stored => stored with { AccentColorId = AccentColorId.Crimson }, cancel);
        settings.Session.AskToSave = false;
        await ChangeBehindTheirBacksAsync(app, stored => stored with { Theme = ThemePreference.Light }, cancel);
        await app.Shell.SetLayoutAsync(ShellLayout.Sidebar);

        var stored = await StoredAsync(app, cancel);
        Assert.Equal(ShellLayout.Sidebar, stored.Layout);
        Assert.False(stored.ConfirmSessionCapture);
        Assert.Equal(AccentColorId.Crimson, stored.AccentColorId);
        Assert.Equal(ThemePreference.Light, stored.Theme);
    }

    [Fact]
    public async Task The_launch_dialogs_preference_keeps_what_was_saved_since_the_flow_last_looked()
    {
        var cancel = TestContext.Current.CancellationToken;
        using var app = await ComposedApp.StartAsync();

        var play = app.Games.Profiles.Single(row => row.Id == SampleScenario.SurvivalId).PlayCommand.ExecuteAsync(null);
        var dialog = app.Dialog<LaunchDialogViewModel>();
        await app.Shell.SetLayoutAsync(ShellLayout.Library);
        await ChangeBehindTheirBacksAsync(app, stored => stored with { Theme = ThemePreference.Light }, cancel);
        dialog.DontAskAgain = true;
        dialog.CancelCommand.Execute(null);
        await play.Soon();

        var stored = await StoredAsync(app, cancel);
        Assert.True(stored.SkipConfirmWhenOnlySaveChanges);
        Assert.Equal(ShellLayout.Library, stored.Layout);
        Assert.Equal(ThemePreference.Light, stored.Theme);
    }

    [Fact]
    public async Task The_shell_follows_the_preference_the_launch_dialog_saved()
    {
        using var app = await ComposedApp.StartAsync();

        var play = app.Games.Profiles.Single(row => row.Id == SampleScenario.SurvivalId).PlayCommand.ExecuteAsync(null);
        var dialog = app.Dialog<LaunchDialogViewModel>();
        FakeAssert.Ok(await app.Backend.Settings.SaveAsync((await StoredAsync(app, TestContext.Current.CancellationToken)) with { Theme = ThemePreference.Light }));
        dialog.DontAskAgain = true;
        dialog.CancelCommand.Execute(null);
        await play.Soon();

        Assert.Equal(ThemePreference.Light, app.Theme.Applied[^1].Preference);
    }

    [Fact]
    public async Task A_dialog_preference_still_waiting_to_land_does_not_wipe_out_a_layout_chosen_meanwhile()
    {
        var cancel = TestContext.Current.CancellationToken;
        GatedSettingsApi? gated = null;
        using var app = await ComposedApp.StartAsync(settings: inner => gated = new GatedSettingsApi(inner));

        var play = app.Games.Profiles.Single(row => row.Id == SampleScenario.SurvivalId).PlayCommand.ExecuteAsync(null);
        var dialog = app.Dialog<LaunchDialogViewModel>();
        gated!.HoldNextSave();
        dialog.DontAskAgain = true;
        var layout = app.Shell.SetLayoutAsync(ShellLayout.Library);
        gated.ReleaseSaves();
        await layout.WaitAsync(TimeSpan.FromSeconds(5), cancel);
        await gated.HeldSaveDone.WaitAsync(TimeSpan.FromSeconds(5), cancel);
        dialog.CancelCommand.Execute(null);
        await play.Soon();

        var stored = await StoredAsync(app, cancel);
        Assert.Equal(ShellLayout.Library, stored.Layout);
        Assert.True(stored.SkipConfirmWhenOnlySaveChanges);
        Assert.Equal(ShellLayout.Library, app.Shell.Layout);
    }

    [Fact]
    public async Task A_save_still_waiting_to_land_does_not_wipe_out_a_layout_chosen_meanwhile()
    {
        var cancel = TestContext.Current.CancellationToken;
        GatedSettingsApi? gated = null;
        using var app = await ComposedApp.StartAsync(settings: inner => gated = new GatedSettingsApi(inner));
        var settings = app.Settings;

        gated!.HoldNextSave();
        settings.Session.AskToSave = false;
        var layout = app.Shell.SetLayoutAsync(ShellLayout.Library);
        gated.ReleaseSaves();
        await layout.WaitAsync(TimeSpan.FromSeconds(5), cancel);
        await gated.HeldSaveDone.WaitAsync(TimeSpan.FromSeconds(5), cancel);

        var stored = await StoredAsync(app, cancel);
        Assert.Equal(ShellLayout.Library, stored.Layout);
        Assert.False(stored.ConfirmSessionCapture);
        Assert.Equal(ShellLayout.Library, app.Shell.Layout);
        Assert.Equal(ShellLayout.Library, settings.Appearance.SelectedLayout!.Layout);
    }
}
