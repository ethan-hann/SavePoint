using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Games;

public class GamesScreenViewTests
{
    private static List<Button> PlayButtons(GamesViewHarness harness) => [.. harness.All<Button>().Where(button => button.Name == "PlayButton")];

    private static List<Border> Rows(GamesViewHarness harness) =>
        [.. harness.Find<ItemsControl>("Rows").GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("table-row"))];

    private static List<Button> MoreButtons(GamesViewHarness harness) => [.. harness.All<Button>().Where(button => button.Name == "MoreButton")];

    private static void Press(GamesViewHarness harness, Control control, Key key)
    {
        control.Focus(NavigationMethod.Tab);
        harness.Settle();
        harness.Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.Enter, null);
        harness.Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.Enter, null);
        harness.Settle();
    }

    private static Control Focused(GamesViewHarness harness) =>
        (Control)harness.Window.FocusManager!.GetFocusedElement()!;

    private static FakeBackend NoProfiles() =>
        new(ScenarioBuilder.Empty().WithSettings(MockupScenario.Settings).WithVolume(210 * MockupScenario.Gb, 131 * MockupScenario.Gb).WithGame(MockupScenario.Caretaker, 9_400_000_000));

    // ---- The sidebar layout (3a)

    [AvaloniaFact]
    public async Task The_sidebar_layout_shows_the_header_chips_table_and_footer()
    {
        using var harness = await GamesViewHarness.StartAsync();

        var texts = harness.VisibleTexts().ToList();

        Assert.Contains("Skyrim Special Edition", texts);
        Assert.Contains(@"C:\Games\SkyrimSE", texts);
        Assert.Contains("Edit game", texts);
        Assert.Contains("New profile", texts);
        Assert.Contains("Profiles", texts);
        Assert.Contains("History", texts);
        Assert.Contains("PROFILE", texts);
        Assert.Contains("VERSION", texts);
        Assert.Contains("MOD FILES", texts);
        Assert.Contains("LAST PLAYED", texts);
        Assert.Contains("Modded playthrough - 7-2-26", texts);
        Assert.Contains("Active now \u00b7 6 saves", texts);
        Assert.Contains("Unsaved changes \u00b7 2 saves", texts);
        Assert.Contains("1 save", texts);
        Assert.Contains("1.6.1170", texts);
        Assert.Contains("212", texts);
        Assert.Contains("3 days ago", texts);
        Assert.Contains("This game is using 41.2 GB of your 210 GB storage folder", texts);
        Assert.False(harness.Find<Grid>("LibraryHeader").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_table_uses_the_mockups_columns()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var table = harness.Find<Border>("Table");

        double X(string name) => harness.Find<TextBlock>(name).TranslatePoint(new Point(0, 0), table)!.Value.X;

        // 1fr 108 92 118 128 with 14px gaps inside a 14px padding.
        Assert.InRange(X("VersionHeader"), 253, 259);
        Assert.InRange(X("ModFilesHeader"), X("VersionHeader") + 108 + 14 - 1, X("VersionHeader") + 108 + 14 + 1);
        Assert.InRange(X("LastPlayedHeader"), X("ModFilesHeader") + 92 + 14 - 1, X("ModFilesHeader") + 92 + 14 + 1);
        Assert.InRange(harness.Find<Border>("TableHeader").Bounds.Height, 30, 34);
        Assert.InRange(table.Bounds.Width, 750, 765);
    }

    [AvaloniaFact]
    public async Task The_active_row_has_a_dot_and_a_filled_play_button()
    {
        using var harness = await GamesViewHarness.StartAsync();

        var plays = PlayButtons(harness);

        Assert.Equal(3, plays.Count);
        Assert.Equal([true, false, false], plays.Select(button => button.Classes.Contains("filled")));
        Assert.Single(harness.All<Border>(), border => border.Classes.Contains("profile-dot") && border.Classes.Contains("active"));
        Assert.Equal("Play", plays[0].GetVisualDescendants().OfType<TextBlock>().Last().Text);
    }

    [AvaloniaFact]
    public async Task Play_starts_the_launch_flow()
    {
        using var harness = await GamesViewHarness.StartAsync();

        PlayButtons(harness)[1].Command!.Execute(null);

        Assert.Single(harness.Rig.LaunchFlow!.Begun);
        Assert.All(PlayButtons(harness), button => Assert.True(button.IsEffectivelyEnabled));
    }

    [AvaloniaFact]
    public async Task The_play_buttons_follow_the_launch_flow()
    {
        using var harness = await GamesViewHarness.StartAsync();

        harness.Rig.LaunchFlow!.SetActive(MockupScenario.SkyrimId, true);
        harness.Settle();

        Assert.All(PlayButtons(harness), button => Assert.False(button.IsEffectivelyEnabled));
    }

    [AvaloniaFact]
    public async Task The_row_menu_offers_edit_duplicate_and_delete_for_that_row()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var more = MoreButtons(harness)[1];
        var row = harness.Rig.Screen.Profiles[1];

        more.Flyout!.ShowAt(more);
        harness.Settle();

        var items = ((MenuFlyout)more.Flyout).Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Edit", "Duplicate", "Delete"], items.Select(item => item.Header));
        Assert.Same(row.EditCommand, items[0].Command);
        Assert.Same(row.DuplicateCommand, items[1].Command);
        Assert.Same(row.DeleteCommand, items[2].Command);
    }

    [AvaloniaFact]
    public async Task Choosing_delete_in_the_row_menu_asks_with_what_would_be_lost()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var more = MoreButtons(harness)[1];
        more.Flyout!.ShowAt(more);
        harness.Settle();

        ((MenuFlyout)more.Flyout).Items.OfType<MenuItem>().Last().Command!.Execute(null);
        harness.Settle();

        var confirm = Assert.IsType<ConfirmDialogViewModel>(harness.Dialogs.Current);
        Assert.Equal("Delete Vanilla - latest update?", confirm.Title);
        Assert.StartsWith("1 point exists only in this profile.", confirm.Message);
    }

    [AvaloniaFact]
    public async Task Delete_game_stays_locked_until_the_games_name_is_typed()
    {
        using var harness = await GamesViewHarness.StartAsync();

        var deleting = harness.Rig.Screen.DeleteGameCommand.ExecuteAsync(null);
        harness.Settle();
        var confirm = Assert.IsType<ConfirmDialogViewModel>(harness.Dialogs.Current);
        var field = harness.Find<TextBox>("TypedField");
        var button = harness.Find<Button>("ConfirmButton");

        Assert.True(field.IsEffectivelyVisible);
        Assert.Equal("Type Skyrim Special Edition to confirm.", AutomationProperties.GetName(field));
        Assert.False(button.IsEffectivelyEnabled);

        field.Text = "skyrim special edition";
        harness.Settle();

        Assert.True(confirm.CanConfirm);
        Assert.True(button.IsEffectivelyEnabled);
        button.Command!.Execute(null);
        await deleting;
        Assert.Single(harness.Rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.DeleteAsync)));
    }

    [AvaloniaFact]
    public async Task The_header_overflow_offers_delete_game()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var menu = harness.Find<Button>("GameMenuButton");

        menu.Flyout!.ShowAt(menu);
        harness.Settle();

        var item = Assert.Single(((MenuFlyout)menu.Flyout).Items.OfType<MenuItem>());
        Assert.Equal("Delete game", item.Header);
        Assert.Same(harness.Rig.Screen.DeleteGameCommand, item.Command);
        Assert.Equal("More actions for Skyrim Special Edition", AutomationProperties.GetName(menu));
    }

    [AvaloniaFact]
    public async Task The_storage_footer_sits_at_the_bottom_of_the_pane_below_the_table_as_the_mockup_draws_it()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var root = harness.Find<Grid>("Root");
        var footer = harness.Find<Border>("StorageFooter");
        var table = harness.Find<Border>("Table");

        var footerBottom = footer.TranslatePoint(new Point(0, footer.Bounds.Height), root)!.Value.Y;
        var tableBottom = table.TranslatePoint(new Point(0, table.Bounds.Height), root)!.Value.Y;

        Assert.Equal(root.Bounds.Height, footerBottom, 1.0);
        Assert.True(footer.Bounds.Top > tableBottom + 40, "the footer is pinned to the pane, not drawn straight under the table");
    }

    [AvaloniaFact]
    public async Task The_history_chip_goes_to_history_and_neither_chip_flips_when_pressed()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var profiles = harness.Find<TabChip>("ProfilesChip");
        var history = harness.Find<TabChip>("HistoryChip");
        Assert.True(profiles.IsChecked);
        Assert.False(history.IsChecked);
        Assert.Equal("View", AutomationProperties.GetName(harness.Find<StackPanel>("Tabs")));

        Press(harness, profiles, Key.Enter);

        Assert.Equal(NavDestination.Games, harness.Rig.Shell.Destination);
        Assert.True(profiles.IsChecked);

        Press(harness, history, Key.Enter);

        Assert.Equal(NavDestination.History, harness.Rig.Shell.Destination);
        Assert.False(history.IsChecked);
        Assert.True(profiles.IsChecked);
    }

    [AvaloniaFact]
    public async Task The_activity_chip_sits_after_history_goes_to_activity_and_does_not_flip_when_pressed()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var chips = harness.Find<StackPanel>("Tabs").Children.OfType<TabChip>().ToList();
        var activity = chips.SingleOrDefault(chip => chip.Name == "ActivityChip");

        Assert.Equal(["Profiles", "History", "Activity"], chips.Select(chip => chip.Content));
        Assert.NotNull(activity);
        Assert.False(activity.IsChecked);
        harness.Render("activity-chip");

        Press(harness, activity, Key.Enter);

        Assert.Equal(NavDestination.Activity, harness.Rig.Shell.Destination);
        Assert.False(activity.IsChecked);
    }

    // ---- Keyboard

    [AvaloniaFact]
    public async Task Play_and_the_row_menu_work_from_the_keyboard()
    {
        using var harness = await GamesViewHarness.StartAsync();

        Press(harness, PlayButtons(harness)[2], Key.Enter);

        var begun = Assert.Single(harness.Rig.LaunchFlow!.Begun);
        Assert.Equal(new LaunchTarget.ProfileTip(MockupScenario.SurvivalId), begun.Target);

        var more = MoreButtons(harness)[0];
        Press(harness, more, Key.Enter);

        Assert.True(more.Flyout!.IsOpen);
    }

    [AvaloniaFact]
    public async Task Enter_on_a_focused_row_starts_play_for_that_profile_once()
    {
        using var harness = await GamesViewHarness.StartAsync();

        Press(harness, Rows(harness)[2], Key.Enter);

        var begun = Assert.Single(harness.Rig.LaunchFlow!.Begun);
        Assert.Equal(new LaunchTarget.ProfileTip(MockupScenario.SurvivalId), begun.Target);
    }

    [AvaloniaFact]
    public async Task Enter_on_a_row_that_cannot_play_starts_nothing()
    {
        using var harness = await GamesViewHarness.StartAsync();
        harness.Rig.Screen.Profiles[2].SetCanPlay(false);

        Press(harness, Rows(harness)[2], Key.Enter);

        Assert.Empty(harness.Rig.LaunchFlow!.Begun);
    }

    [AvaloniaFact]
    public async Task Tab_reaches_each_row_then_its_play_button_and_menu_in_turn()
    {
        using var harness = await GamesViewHarness.StartAsync();
        PlayButtons(harness)[0].Focus(NavigationMethod.Tab);
        harness.Settle();

        var reached = new List<string?> { AutomationProperties.GetName(Focused(harness)) };
        for (var step = 0; step < 4; step++)
        {
            harness.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            harness.Window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            harness.Settle();
            reached.Add(AutomationProperties.GetName(Focused(harness)));
        }

        Assert.Equal(
            [
                "Play Modded playthrough - 7-2-26",
                "More actions for Modded playthrough - 7-2-26",
                harness.Rig.Screen.Profiles[1].AutomationName,
                "Play Vanilla - latest update",
                "More actions for Vanilla - latest update",
            ],
            reached);
    }

    [AvaloniaFact]
    public async Task Edit_game_opens_the_editor_pane()
    {
        using var harness = await GamesViewHarness.StartAsync();

        harness.Find<Button>("EditGameButton").Command!.Execute(null);
        harness.Settle();

        Assert.Single(harness.Rig.Editor!.Created);
    }

    // ---- Narrow windows

    [AvaloniaFact]
    public async Task Below_about_900px_the_table_drops_mod_files_first_and_version_next()
    {
        using var harness = await GamesViewHarness.StartAsync();
        Assert.True(harness.Find<TextBlock>("ModFilesHeader").IsEffectivelyVisible);

        harness.Window.Width = 900;
        harness.Settle();

        Assert.False(harness.Find<TextBlock>("ModFilesHeader").IsEffectivelyVisible);
        Assert.True(harness.Find<TextBlock>("VersionHeader").IsEffectivelyVisible);
        Assert.DoesNotContain("212", harness.VisibleTexts());
        Assert.Contains("1.6.640", harness.VisibleTexts());

        harness.Window.Width = 520;
        harness.Settle();

        Assert.False(harness.Find<TextBlock>("VersionHeader").IsEffectivelyVisible);
        Assert.DoesNotContain("1.6.640", harness.VisibleTexts());
        Assert.Contains("3 days ago", harness.VisibleTexts());

        harness.Window.Width = 1040;
        harness.Settle();

        Assert.True(harness.Find<TextBlock>("ModFilesHeader").IsEffectivelyVisible);
        Assert.Contains("212", harness.VisibleTexts());
    }

    // ---- The library layout (3b)

    [AvaloniaFact]
    public async Task The_library_layout_shows_a_title_with_the_buttons_and_leaves_out_the_rest()
    {
        using var harness = await GamesViewHarness.StartAsync(layout: ShellLayout.Library);

        var texts = harness.VisibleTexts().ToList();

        Assert.Contains("Skyrim Special Edition \u2014 profiles", texts);
        Assert.True(harness.Find<Button>("LibraryEditGameButton").IsEffectivelyVisible);
        Assert.True(harness.Find<Button>("LibraryNewProfileButton").IsEffectivelyVisible);
        Assert.False(harness.Find<Grid>("SidebarHeader").IsEffectivelyVisible);
        Assert.False(harness.Find<StackPanel>("Tabs").IsEffectivelyVisible);
        Assert.False(harness.Find<Border>("StorageFooter").IsEffectivelyVisible);
        Assert.DoesNotContain(@"C:\Games\SkyrimSE", texts);
        Assert.Contains("Modded playthrough - 7-2-26", texts);
    }

    [AvaloniaFact]
    public async Task Changing_the_layout_keeps_the_same_screen_and_its_rows()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var screen = harness.Screen;
        var rows = harness.Rig.Screen.Profiles.ToList();

        await harness.Rig.Shell.SetLayoutAsync(ShellLayout.Library);
        harness.Settle();

        Assert.Same(screen, harness.Screen);
        Assert.Equal(rows, harness.Rig.Screen.Profiles);
        Assert.True(harness.Find<Grid>("LibraryHeader").IsEffectivelyVisible);
    }

    // ---- Accessibility

    [AvaloniaFact]
    public async Task Every_icon_button_has_a_name()
    {
        using var harness = await GamesViewHarness.StartAsync();

        Assert.Empty(AutomationAudit.IconButtonsWithoutNames(harness.View));
        Assert.Equal(
            ["More actions for Modded playthrough - 7-2-26", "More actions for Vanilla - latest update", "More actions for Survival test - 5-14-26"],
            MoreButtons(harness).Select(AutomationProperties.GetName));
        Assert.Equal(
            ["Play Modded playthrough - 7-2-26", "Play Vanilla - latest update", "Play Survival test - 5-14-26"],
            PlayButtons(harness).Select(AutomationProperties.GetName));
    }

    [AvaloniaFact]
    public async Task The_table_and_its_rows_carry_table_semantics()
    {
        using var harness = await GamesViewHarness.StartAsync();

        var table = harness.Find<Border>("Table");
        var rows = harness.Find<ItemsControl>("Rows").GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("table-row")).ToList();

        Assert.Equal(AutomationControlType.Table, AutomationProperties.GetControlTypeOverride(table));
        Assert.Equal("Profiles", AutomationProperties.GetName(table));
        Assert.Equal(AutomationControlType.Header, AutomationProperties.GetControlTypeOverride(harness.Find<Border>("TableHeader")));
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal(AutomationControlType.DataItem, AutomationProperties.GetControlTypeOverride(row)));
        Assert.All(rows, row => Assert.True(row.Focusable));
        Assert.Equal(harness.Rig.Screen.Profiles.Select(profile => profile.AutomationName), rows.Select(AutomationProperties.GetName));
    }

    // ---- States

    [AvaloniaFact]
    public async Task A_game_without_profiles_prompts_for_the_first_one()
    {
        using var harness = await GamesViewHarness.StartAsync(NoProfiles());

        Assert.True(harness.Find<Border>("EmptyState").IsEffectivelyVisible);
        Assert.False(harness.Find<Border>("Table").IsEffectivelyVisible);
        Assert.Contains("No profiles yet", harness.VisibleTexts());

        harness.Find<Button>("EmptyNewProfileButton").Command!.Execute(null);

        Assert.IsType<NewProfileWizardViewModel>(harness.Dialogs.Current);
    }

    [AvaloniaFact]
    public async Task The_launch_flows_content_shows_below_the_table()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var host = harness.Find<ContentControl>("InlineContentHost");
        Assert.False(host.IsEffectivelyVisible);

        harness.Rig.LaunchFlow!.SetInline(MockupScenario.SkyrimId, "Everything was already in place");
        harness.Settle();

        Assert.True(host.IsEffectivelyVisible);
        Assert.Contains("Everything was already in place", harness.VisibleTexts());
        var table = harness.Find<Border>("Table");
        Assert.True(host.TranslatePoint(new Point(0, 0), table)!.Value.Y >= table.Bounds.Height);
    }

    [AvaloniaFact]
    public async Task The_launch_flows_content_is_pinned_above_the_storage_footer_as_the_mockup_draws_it()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var host = harness.Find<ContentControl>("InlineContentHost");
        var footer = harness.Find<Border>("StorageFooter");

        harness.Rig.LaunchFlow!.SetInline(MockupScenario.SkyrimId, "Everything was already in place");
        harness.Settle();

        Assert.Equal(12, footer.Bounds.Top - host.Bounds.Bottom, 1.0);
    }

    [AvaloniaFact]
    public async Task The_launch_flows_content_stays_in_view_when_the_profiles_outgrow_the_pane()
    {
        var scenario = ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings);
        for (var i = 0; i < 12; i++)
        {
            scenario = scenario.WithProfile(new Profile($"profile-extra-{i}", MockupScenario.SkyrimId, $"Extra profile {i}", $"profile/extra-{i}"));
        }

        using var harness = await GamesViewHarness.StartAsync(new FakeBackend(scenario));
        var root = harness.Find<Grid>("Root");
        var host = harness.Find<ContentControl>("InlineContentHost");
        var scroller = harness.Find<Border>("Table").GetVisualAncestors().OfType<ScrollViewer>().First();

        harness.Rig.LaunchFlow!.SetInline(MockupScenario.SkyrimId, "Everything was already in place");
        harness.Settle();

        Assert.True(scroller.Extent.Height > scroller.Viewport.Height, "the profiles fill more than the pane");
        Assert.True(host.Bounds.Top >= 0 && host.Bounds.Bottom <= root.Bounds.Height, $"the content at {host.Bounds} is outside the {root.Bounds.Height:0}px pane");
    }

    // A failure card is as tall as the pane allows, so pinning it would squeeze the table to nothing in a short window.
    [AvaloniaFact]
    public async Task A_failure_card_stays_with_the_profiles_in_the_scroll_area_instead_of_being_pinned()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var scroller = harness.Find<Border>("Table").GetVisualAncestors().OfType<ScrollViewer>().First();
        var pinned = harness.Find<ContentControl>("InlineContentHost");

        harness.Rig.LaunchFlow!.SetInline(MockupScenario.SkyrimId, new ErrorCardViewModel(MockupScenario.FileInUseError, harness.Rig.Text));
        harness.Settle();

        var card = harness.All<ErrorCardView>().Single();
        Assert.True(card.GetVisualAncestors().Contains(scroller), "the failure card is outside the table's scroll area");
        Assert.False(pinned.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task A_failed_read_shows_the_error_card_instead_of_the_table()
    {
        using var harness = await GamesViewHarness.StartAsync();
        harness.Rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.ListAsync), ErrorInfo.From(new PathMissingException(@"C:\Games\SkyrimSE", RepoRole.Install)));

        await harness.Rig.Screen.ReloadAsync();
        harness.Settle();

        Assert.False(harness.Find<Border>("Table").IsEffectivelyVisible);
        Assert.IsType<ErrorCardViewModel>(harness.Find<ContentControl>("LoadErrorHost").Content);
        Assert.Contains("This game's folder has moved or is gone", harness.VisibleTexts());
        Assert.Contains("Try again", harness.VisibleTexts());
    }

    [AvaloniaFact]
    public async Task Rows_the_user_cannot_see_are_not_reachable_by_keyboard()
    {
        using var harness = await GamesViewHarness.StartAsync();

        Assert.Equal(3, PlayButtons(harness).Count(button => button.Focusable && button.IsEffectivelyEnabled));
    }
}
