using Savepoint.App.Features.Help;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Settings;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Help;

// The tour starts once, when the Games screen first has a profile to point at, and again only when the player asks.
public class TourStartTests
{
    private sealed class Pane;

    // The mockup's library for a player who has not seen the tour.
    private static FakeBackend Unseen() => new(ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings with { TourSeen = false }));

    // A game with no profile yet: the Games screen shows its empty state, which has nothing for the tour to point at.
    private static FakeBackend GameWithoutProfiles() =>
        new(ScenarioBuilder.Empty().WithSettings(MockupScenario.Settings with { TourSeen = false }).WithGame(MockupScenario.Skyrim));

    private static AppSettings Stored(FakeBackend backend) => FakeAssert.Ok(backend.Settings.GetAsync().GetAwaiter().GetResult());

    private static int Writes(FakeBackend backend) => backend.Calls.To<ISettingsApi>(nameof(ISettingsApi.SaveAsync)).Count;

    [Fact]
    public async Task The_tour_starts_when_the_Games_screen_has_a_profile_and_the_player_has_not_seen_it()
    {
        var rig = await ShellRig.StartAsync(Unseen());

        Assert.True(rig.Shell.Tour.IsActive);
        Assert.Equal(TourStep.Profile, rig.Shell.Tour.Current.Step);
        Assert.Contains("Skyrim Special Edition", rig.Shell.Tour.Current.Body, StringComparison.Ordinal);
        Assert.True(rig.Shell.ShowTour);
    }

    [Fact]
    public async Task That_it_has_run_is_kept_in_the_settings_file_the_moment_it_starts()
    {
        var backend = Unseen();

        await ShellRig.StartAsync(backend);

        Assert.True(Stored(backend).TourSeen);
        Assert.Equal(1, Writes(backend));
    }

    [Fact]
    public async Task A_player_who_has_seen_the_tour_is_not_shown_it_and_nothing_is_written()
    {
        var backend = FakeBackend.Mockup();

        var rig = await ShellRig.StartAsync(backend);

        Assert.False(rig.Shell.Tour.IsActive);
        Assert.False(rig.Shell.ShowTour);
        Assert.Equal(0, Writes(backend));
    }

    [Fact]
    public async Task A_game_without_a_profile_has_no_tour_until_its_first_profile_exists()
    {
        var backend = GameWithoutProfiles();
        var rig = await ShellRig.StartAsync(backend);
        Assert.False(rig.Shell.Tour.IsActive);
        Assert.False(Stored(backend).TourSeen);

        backend.State.Profiles.Add(new Profile("first", MockupScenario.SkyrimId, "First profile", "profile/first"));
        await rig.Shell.RefreshLibraryAsync();

        Assert.True(rig.Shell.Tour.IsActive);
        Assert.True(Stored(backend).TourSeen);
    }

    [Fact]
    public async Task The_first_run_screen_has_no_tour_because_there_is_nothing_to_point_at()
    {
        var backend = new FakeBackend(ScenarioBuilder.Empty().WithSettings(MockupScenario.Settings with { TourSeen = false }));

        var rig = await ShellRig.StartAsync(backend);

        Assert.False(rig.Shell.Tour.IsActive);
        Assert.Equal(0, Writes(backend));
    }

    [Fact]
    public async Task The_tour_waits_for_the_Games_screen_and_starts_when_the_player_comes_to_it()
    {
        var rig = ShellRig.Create(Unseen());
        rig.Shell.Navigate(NavDestination.History);
        await rig.Shell.InitializeAsync();
        Assert.False(rig.Shell.Tour.IsActive);

        rig.Shell.Navigate(NavDestination.Games);

        Assert.True(rig.Shell.Tour.IsActive);
    }

    [Fact]
    public async Task The_tour_waits_while_a_pane_covers_the_screen_and_starts_when_it_closes()
    {
        var rig = ShellRig.Create(Unseen());
        rig.Shell.ShowPane(new Pane());
        await rig.Shell.InitializeAsync();
        Assert.False(rig.Shell.Tour.IsActive);

        rig.Shell.ClosePane();

        Assert.True(rig.Shell.Tour.IsActive);
    }

    [Fact]
    public async Task It_starts_once_per_run_so_ending_it_and_reloading_the_library_does_not_bring_it_back()
    {
        var backend = Unseen();
        var rig = await ShellRig.StartAsync(backend);
        rig.Shell.Tour.SkipCommand.Execute(null);

        await rig.Shell.RefreshLibraryAsync();
        rig.Shell.Navigate(NavDestination.History);
        rig.Shell.Navigate(NavDestination.Games);

        Assert.False(rig.Shell.Tour.IsActive);
        Assert.Equal(1, Writes(backend));
    }

    [Fact]
    public async Task Settings_saved_elsewhere_that_say_the_tour_was_seen_keep_it_from_starting()
    {
        var backend = GameWithoutProfiles();
        var rig = ShellRig.Create(backend);
        await rig.Shell.InitializeAsync();

        rig.Shell.SettingsChanged(MockupScenario.Settings with { TourSeen = true });
        backend.State.Profiles.Add(new Profile("first", MockupScenario.SkyrimId, "First profile", "profile/first"));
        await rig.Shell.RefreshLibraryAsync();

        Assert.False(rig.Shell.Tour.IsActive);
    }

    [Fact]
    public async Task Removing_all_data_resets_the_tour_with_everything_else_so_the_next_first_profile_starts_it_again()
    {
        var rig = await ShellRig.StartAsync(Unseen());
        Assert.True(Stored(rig.Backend).TourSeen);
        rig.Shell.Tour.SkipCommand.Execute(null);

        // What the Settings screen tells the shell once the settings file is gone and the defaults are back.
        rig.Shell.SettingsChanged(MockupScenario.Settings with { TourSeen = false });

        Assert.True(rig.Shell.Tour.IsActive);
    }

    [Fact]
    public async Task The_tour_shows_only_on_the_Games_screen_with_nothing_covering_it()
    {
        var rig = await ShellRig.StartAsync(Unseen());
        Assert.True(rig.Shell.ShowTour);

        rig.Shell.Navigate(NavDestination.History);
        Assert.False(rig.Shell.ShowTour);
        Assert.True(rig.Shell.Tour.IsActive);
        rig.Shell.Navigate(NavDestination.Games);
        Assert.True(rig.Shell.ShowTour);

        rig.Shell.ShowPane(new Pane());
        Assert.False(rig.Shell.ShowTour);
        rig.Shell.ClosePane();
        Assert.True(rig.Shell.ShowTour);

        rig.Shell.Tour.SkipCommand.Execute(null);
        Assert.False(rig.Shell.ShowTour);
    }

    [Fact]
    public async Task Whether_the_tour_shows_is_announced_to_the_window_as_it_changes()
    {
        var rig = await ShellRig.StartAsync(Unseen());
        var changed = new List<string?>();
        rig.Shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        rig.Shell.Navigate(NavDestination.History);
        rig.Shell.Tour.SkipCommand.Execute(null);

        Assert.Equal(2, changed.Count(name => name == nameof(ShellViewModel.ShowTour)));
    }

    // ---- Showing it again

    [Fact]
    public async Task Showing_the_tour_again_starts_it_from_the_first_step_on_the_Games_screen_from_anywhere()
    {
        var rig = await ShellRig.StartAsync(FakeBackend.Mockup());
        rig.Shell.Navigate(NavDestination.Settings);
        Assert.False(rig.Shell.Tour.IsActive);

        rig.Shell.StartTour();

        Assert.Equal(NavDestination.Games, rig.Shell.Destination);
        Assert.True(rig.Shell.Tour.IsActive);
        Assert.Equal(TourStep.Profile, rig.Shell.Tour.Current.Step);
        Assert.True(rig.Shell.ShowTour);
    }

    [Fact]
    public async Task Showing_the_tour_again_closes_the_help_page_that_was_open()
    {
        var rig = await ShellRig.StartAsync(FakeBackend.Mockup());
        rig.Shell.ShowHelp();

        rig.Shell.StartTour();

        Assert.IsNotType<HowItWorksViewModel>(rig.Shell.Content);
        Assert.True(rig.Shell.ShowTour);
    }

    [Fact]
    public async Task The_tour_can_be_shown_again_only_for_a_game_that_has_a_profile()
    {
        var withProfile = await ShellRig.StartAsync(FakeBackend.Mockup());
        var without = await ShellRig.StartAsync(GameWithoutProfiles());

        Assert.True(withProfile.Shell.CanShowTour);
        Assert.False(without.Shell.CanShowTour);
        without.Shell.StartTour();
        Assert.False(without.Shell.Tour.IsActive);
    }

    [Fact]
    public async Task Showing_it_again_does_not_touch_the_settings()
    {
        var backend = FakeBackend.Mockup();
        var rig = await ShellRig.StartAsync(backend);

        rig.Shell.StartTour();

        Assert.Equal(0, Writes(backend));
    }

    [Fact]
    public async Task The_Help_section_of_Settings_shows_the_tour_again_and_is_off_with_a_reason_when_there_is_no_profile()
    {
        var rig = await SettingsRig.ShownAsync();
        Assert.True(rig.Screen.Help.CanShowTour);
        Assert.False(rig.Screen.Help.ShowTourHint);

        rig.Screen.Help.ShowTourCommand.Execute(null);
        Assert.True(rig.Shell.Tour.IsActive);

        var without = await SettingsRig.ShownAsync(GameWithoutProfiles());
        Assert.False(without.Screen.Help.CanShowTour);
        Assert.True(without.Screen.Help.ShowTourHint);
        Assert.False(without.Screen.Help.ShowTourCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_end_of_the_page_shows_the_tour_again_and_closes_the_page()
    {
        var rig = await ShellRig.StartAsync(FakeBackend.Mockup());
        rig.Shell.ShowHelp();
        var page = Assert.IsType<HowItWorksViewModel>(rig.Shell.Content);

        Assert.True(page.HasTour);
        Assert.True(page.CanShowTour);
        Assert.Equal("Show the tour again", page.TourLabel);
        page.ShowTourCommand.Execute(null);

        Assert.True(rig.Shell.Tour.IsActive);
        Assert.IsNotType<HowItWorksViewModel>(rig.Shell.Content);
    }
}
