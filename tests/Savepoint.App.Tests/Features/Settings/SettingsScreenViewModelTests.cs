using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Settings;

public class SettingsScreenViewModelTests
{
    private static ErrorInfo SaveFailure() => ErrorInfo.From(new InvalidOperationException("The settings could not be saved."));

    private static async Task<AppSettings> StoredAsync(SettingsRig rig) => FakeAssert.Ok(await rig.Backend.Settings.GetAsync());

    // ---- The screen

    [Fact]
    public async Task Showing_the_screen_reads_the_settings_and_the_sections_show_them()
    {
        var rig = await SettingsRig.StartAsync();

        rig.Show();

        Assert.True(rig.Screen.IsLoaded);
        Assert.Null(rig.Screen.LoadError);
        Assert.Equal(ThemePreference.Dark, rig.Screen.Appearance.SelectedTheme!.Preference);
        Assert.Equal(AccentColorId.Blue, rig.Screen.Appearance.SelectedAccent!.Accent);
        Assert.Equal(ShellLayout.Sidebar, rig.Screen.Appearance.SelectedLayout!.Layout);
        Assert.True(rig.Screen.Session.AskToSave);
        Assert.False(rig.Screen.Session.SkipWhenOnlySaveChanges);
    }

    [Fact]
    public async Task A_failed_read_shows_an_error_card_and_try_again_reads_again()
    {
        var rig = await SettingsRig.StartAsync();
        rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetAsync), ErrorInfo.From(new InvalidOperationException("No settings.")));

        rig.Show();

        Assert.False(rig.Screen.IsLoaded);
        Assert.NotNull(rig.Screen.LoadError);
        var retry = Assert.Single(rig.Screen.LoadError.Actions);
        Assert.Equal("Try again", retry.Label);

        retry.Command.Execute(null);

        Assert.True(rig.Screen.IsLoaded);
        Assert.Null(rig.Screen.LoadError);
    }

    [Fact]
    public async Task Coming_back_to_the_screen_reads_the_settings_again()
    {
        var rig = await SettingsRig.ShownAsync();
        rig.Shell.Navigate(NavDestination.Games);
        FakeAssert.Ok(await rig.Backend.Settings.SaveAsync((await StoredAsync(rig)) with { Theme = ThemePreference.Light }));

        rig.Show();

        Assert.Equal(ThemePreference.Light, rig.Screen.Appearance.SelectedTheme!.Preference);
    }

    [Fact]
    public async Task The_screen_is_the_settings_destination()
    {
        var rig = await SettingsRig.StartAsync();

        Assert.Equal(NavDestination.Settings, rig.Screen.Destination);
    }

    // ---- Appearance

    [Theory]
    [InlineData("windows", "Match Windows")]
    [InlineData("macos", "Match macOS")]
    [InlineData("linux", "Match system")]
    public async Task The_system_theme_segment_names_the_operating_system(string os, string expected)
    {
        var rig = await SettingsRig.ShownAsync(os: os);

        Assert.Equal([expected, "Light", "Dark"], rig.Screen.Appearance.ThemeOptions.Select(option => option.Label));
    }

    [Fact]
    public async Task Choosing_a_theme_saves_it_and_the_shell_follows_at_once()
    {
        var rig = await SettingsRig.ShownAsync();

        rig.Screen.Appearance.SelectedTheme = rig.Screen.Appearance.ThemeOptions.Single(option => option.Preference == ThemePreference.Light);

        Assert.Equal(ThemePreference.Light, (await StoredAsync(rig)).Theme);
        Assert.Equal((ThemePreference.Light, AccentColorId.Blue), rig.Theme.Applied.Last());
    }

    [Fact]
    public async Task A_theme_save_leaves_every_other_setting_as_it_was()
    {
        var rig = await SettingsRig.ShownAsync();
        var before = await StoredAsync(rig);

        rig.Screen.Appearance.SelectedTheme = rig.Screen.Appearance.ThemeOptions.Single(option => option.Preference == ThemePreference.Light);

        Assert.Equal(before with { Theme = ThemePreference.Light }, await StoredAsync(rig));
    }

    [Fact]
    public async Task Choosing_the_theme_already_in_use_saves_nothing()
    {
        var rig = await SettingsRig.ShownAsync();
        var saves = rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync));

        rig.Screen.Appearance.SelectedTheme = rig.Screen.Appearance.ThemeOptions.Single(option => option.Preference == ThemePreference.Dark);

        Assert.Equal(saves, rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
    }

    [Fact]
    public async Task Choosing_an_accent_saves_it_and_the_shell_follows_at_once()
    {
        var rig = await SettingsRig.ShownAsync();

        rig.Screen.Appearance.SelectedAccent = rig.Screen.Appearance.AccentOptions.Single(option => option.Accent == AccentColorId.Crimson);

        Assert.Equal(AccentColorId.Crimson, (await StoredAsync(rig)).AccentColorId);
        Assert.Equal((ThemePreference.Dark, AccentColorId.Crimson), rig.Theme.Applied.Last());
    }

    [Fact]
    public async Task The_five_accents_are_named_in_the_presets_order()
    {
        var rig = await SettingsRig.ShownAsync();

        Assert.Equal(
            [AccentColorId.Blue, AccentColorId.Violet, AccentColorId.Green, AccentColorId.Amber, AccentColorId.Crimson],
            rig.Screen.Appearance.AccentOptions.Select(option => option.Accent));
        Assert.Equal(["Blue", "Violet", "Green", "Amber", "Crimson"], rig.Screen.Appearance.AccentOptions.Select(option => option.Label));
    }

    [Fact]
    public async Task The_two_layouts_say_what_each_is_good_for()
    {
        var rig = await SettingsRig.ShownAsync();

        Assert.Equal(["Sidebar", "Library"], rig.Screen.Appearance.LayoutChoices.Select(choice => choice.Label));
        Assert.Equal(["Every game one click away", "Cover art up front"], rig.Screen.Appearance.LayoutChoices.Select(choice => choice.Description));
    }

    [Fact]
    public async Task Choosing_a_layout_asks_the_shell_to_switch_and_save_it()
    {
        var rig = await SettingsRig.ShownAsync();

        rig.Screen.Appearance.SelectedLayout = rig.Screen.Appearance.LayoutChoices.Single(choice => choice.Layout == ShellLayout.Library);

        Assert.Equal(ShellLayout.Library, rig.Shell.Layout);
        Assert.Equal(ShellLayout.Library, (await StoredAsync(rig)).Layout);
    }

    [Fact]
    public async Task A_layout_changed_from_the_games_screen_moves_the_selection_without_saving_again()
    {
        var rig = await SettingsRig.ShownAsync();
        var saves = rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync));

        await rig.Shell.SetLayoutAsync(ShellLayout.Library);

        Assert.Equal(ShellLayout.Library, rig.Screen.Appearance.SelectedLayout!.Layout);
        Assert.Equal(saves + 1, rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
    }

    [Fact]
    public async Task A_failed_theme_save_puts_the_control_back_and_says_so_without_touching_the_shell()
    {
        var rig = await SettingsRig.ShownAsync();
        var applied = rig.Theme.Applied.Count;
        rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.SaveAsync), SaveFailure());

        rig.Screen.Appearance.SelectedTheme = rig.Screen.Appearance.ThemeOptions.Single(option => option.Preference == ThemePreference.Light);

        Assert.Equal(ThemePreference.Dark, rig.Screen.Appearance.SelectedTheme!.Preference);
        Assert.Equal(applied, rig.Theme.Applied.Count);
        var toast = Assert.Single(rig.Toasts.Toasts);
        Assert.Equal(ToastKind.Error, toast.Message.Kind);
    }

    [Fact]
    public async Task A_save_keeps_a_layout_that_was_changed_elsewhere_after_the_screen_read_its_settings()
    {
        var rig = await SettingsRig.ShownAsync();
        await rig.Shell.SetLayoutAsync(ShellLayout.Library);

        rig.Screen.Appearance.SelectedTheme = rig.Screen.Appearance.ThemeOptions.Single(option => option.Preference == ThemePreference.Light);

        var stored = await StoredAsync(rig);
        Assert.Equal(ThemePreference.Light, stored.Theme);
        Assert.Equal(ShellLayout.Library, stored.Layout);
        Assert.Equal(ShellLayout.Library, rig.Shell.Layout);
    }

    [Fact]
    public async Task A_list_box_clearing_a_choice_while_it_rebuilds_changes_nothing()
    {
        var rig = await SettingsRig.ShownAsync();
        var saves = rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync));

        rig.Screen.Appearance.SelectedTheme = null;
        rig.Screen.Appearance.SelectedAccent = null;
        rig.Screen.Appearance.SelectedLayout = null;

        Assert.Equal(ThemePreference.Dark, rig.Screen.Appearance.SelectedTheme!.Preference);
        Assert.Equal(AccentColorId.Blue, rig.Screen.Appearance.SelectedAccent!.Accent);
        Assert.Equal(ShellLayout.Sidebar, rig.Screen.Appearance.SelectedLayout!.Layout);
        Assert.Equal(saves, rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
    }

    // ---- When you finish playing

    [Fact]
    public async Task Turning_off_the_session_question_saves_it_and_the_shell_hears_about_it()
    {
        var rig = await SettingsRig.ShownAsync();
        var told = rig.Theme.Applied.Count;

        rig.Screen.Session.AskToSave = false;

        Assert.False((await StoredAsync(rig)).ConfirmSessionCapture);
        Assert.False(rig.Screen.Session.AskToSave);
        Assert.Equal(told + 1, rig.Theme.Applied.Count);
    }

    [Fact]
    public async Task Skipping_the_confirmation_when_only_the_save_changes_saves_that_switch()
    {
        var rig = await SettingsRig.ShownAsync();

        rig.Screen.Session.SkipWhenOnlySaveChanges = true;

        var stored = await StoredAsync(rig);
        Assert.True(stored.SkipConfirmWhenOnlySaveChanges);
        Assert.True(stored.ConfirmSessionCapture);
    }

    [Fact]
    public async Task A_failed_switch_save_flips_the_switch_back_and_says_so()
    {
        var rig = await SettingsRig.ShownAsync();
        rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.SaveAsync), SaveFailure());

        rig.Screen.Session.AskToSave = false;

        Assert.True(rig.Screen.Session.AskToSave);
        Assert.Single(rig.Toasts.Toasts);
        Assert.True((await StoredAsync(rig)).ConfirmSessionCapture);
    }

    [Fact]
    public async Task Reading_the_settings_does_not_save_them_back()
    {
        var rig = await SettingsRig.StartAsync();

        rig.Show();

        Assert.Equal(0, rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
    }

    // ---- About

    [Fact]
    public async Task About_states_the_version()
    {
        var rig = await SettingsRig.ShownAsync();

        Assert.Equal("Version 1.2.3", rig.Screen.About.VersionText);
    }

    [Fact]
    public async Task The_notices_action_is_hidden_when_the_file_was_not_shipped()
    {
        var rig = await SettingsRig.ShownAsync(notices: null);

        Assert.False(rig.Screen.About.HasNotices);
        Assert.Null(rig.Screen.About.NoticesPath);
    }

    [Fact]
    public async Task The_notices_action_shows_the_file_in_the_file_manager()
    {
        var rig = await SettingsRig.ShownAsync(notices: @"C:\Program Files\SavePoint\THIRD-PARTY-NOTICES.md");

        Assert.True(rig.Screen.About.HasNotices);
        rig.Screen.About.ShowNoticesCommand.Execute(null);

        Assert.Equal([@"C:\Program Files\SavePoint\THIRD-PARTY-NOTICES.md"], rig.Revealer.Revealed);
        Assert.Empty(rig.Toasts.Toasts);
    }

    [Fact]
    public async Task A_file_manager_that_will_not_open_is_reported()
    {
        var rig = await SettingsRig.ShownAsync(notices: @"C:\Program Files\SavePoint\THIRD-PARTY-NOTICES.md");
        rig.Revealer.Succeeds = false;

        rig.Screen.About.ShowNoticesCommand.Execute(null);

        var toast = Assert.Single(rig.Toasts.Toasts);
        Assert.Equal(ToastKind.Error, toast.Message.Kind);
    }

    [Fact]
    public async Task The_license_action_is_hidden_when_the_file_was_not_shipped()
    {
        var rig = await SettingsRig.ShownAsync(license: null);

        Assert.False(rig.Screen.About.HasLicense);
        Assert.Null(rig.Screen.About.LicensePath);
        Assert.False(rig.Screen.About.ShowLicenseCommand.CanExecute(null));
        rig.Screen.About.ShowLicenseCommand.Execute(null);
        Assert.Empty(rig.Revealer.Revealed);
    }

    [Fact]
    public async Task The_license_action_shows_the_file_in_the_file_manager()
    {
        var rig = await SettingsRig.ShownAsync(license: @"C:\Program Files\SavePoint\LICENSE");

        Assert.True(rig.Screen.About.HasLicense);
        rig.Screen.About.ShowLicenseCommand.Execute(null);

        Assert.Equal([@"C:\Program Files\SavePoint\LICENSE"], rig.Revealer.Revealed);
        Assert.Empty(rig.Toasts.Toasts);
    }

    [Fact]
    public async Task A_file_manager_that_will_not_open_the_license_is_reported_in_words()
    {
        var rig = await SettingsRig.ShownAsync(license: @"C:\Program Files\SavePoint\LICENSE");
        rig.Revealer.Succeeds = false;

        rig.Screen.About.ShowLicenseCommand.Execute(null);

        var toast = Assert.Single(rig.Toasts.Toasts);
        Assert.Equal(ToastKind.Error, toast.Message.Kind);
        Assert.Equal("SavePoint couldn't open the file manager.", toast.Message.Text);
    }

    [Fact]
    public async Task A_license_added_beside_the_app_shows_the_next_time_the_screen_shows()
    {
        string? license = null;
        var rig = await SettingsRig.ShownAsync(license: null);
        Assert.False(rig.Screen.About.HasLicense);

        license = @"C:\Program Files\SavePoint\LICENSE";
        var refreshed = new AboutSectionViewModel(new SettingsEnvironment(rig.Revealer, () => null, "1.2.3", FindLicense: () => license), rig.Text, rig.Toasts);
        refreshed.Refresh();

        Assert.True(refreshed.HasLicense);
        Assert.Equal(license, refreshed.LicensePath);
    }

    [Theory]
    [InlineData("windows", "explorer.exe","/select,\"C:\\App\\THIRD-PARTY-NOTICES.md\"", "")]
    [InlineData("macos", "open", "", "-R|/Applications/SavePoint/THIRD-PARTY-NOTICES.md")]
    [InlineData("linux", "xdg-open", "", "/opt/savepoint")]
    public void The_file_manager_is_started_the_way_each_system_expects(string os, string program, string arguments, string argumentList)
    {
        var path = os switch
        {
            "windows" => @"C:\App\THIRD-PARTY-NOTICES.md",
            "macos" => "/Applications/SavePoint/THIRD-PARTY-NOTICES.md",
            _ => "/opt/savepoint/THIRD-PARTY-NOTICES.md",
        };

        var start = ProcessFileRevealer.Start(path, os);

        Assert.Equal(program, start.FileName);
        Assert.Equal(arguments, start.Arguments);
        Assert.Equal(argumentList, string.Join('|', start.ArgumentList));
        Assert.False(start.UseShellExecute);
    }
}
