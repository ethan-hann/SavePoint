using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Help;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Help;

// What a player sees: an outline on the real control and a small card beside it, not a modal that hides anything.
public class TourViewTests
{
    private static Task<ComposedApp> StartAsync(ShellLayout layout = ShellLayout.Sidebar, double width = 1040, double height = 660) =>
        ComposedApp.StartWithViewAsync(new FakeBackend(ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings with { TourSeen = false, Layout = layout })), width: width, height: height);

    // A control that has to be there: the test fails on an assertion, not on a missing element, when it is not.
    private static T Named<T>(ComposedApp app, string name)
        where T : Control
    {
        var found = app.All<T>().FirstOrDefault(control => control.Name == name);
        Assert.NotNull(found);
        return found;
    }

    private static Border Card(ComposedApp app) => Named<Border>(app, "TourCard");

    private static Border Ring(ComposedApp app) => Named<Border>(app, "TourRing");

    private static bool CardShown(ComposedApp app) => app.All<Border>().Any(border => border.Name == "TourCard" && border.IsEffectivelyVisible);

    private static Rect Bounds(ComposedApp app, Visual visual) => LayoutAudit.BoundsIn(app.Window, visual);

    // What a click at this point would land on. A hit test reads the last rendered frame, which a running window keeps current and a headless one
    // only renders on a tick, so a frame is rendered first; without it the answer is whatever was laid out before the last navigation.
    private static Visual? ClickLandsOn(ComposedApp app, Point point)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return app.Window.InputHitTest(point) as Visual;
    }

    private static Border FirstRow(ComposedApp app)
    {
        var row = app.All<Border>().FirstOrDefault(border => border.Classes.Contains("table-row") && border.IsEffectivelyVisible);
        Assert.NotNull(row);
        return row;
    }

    private static T InRow<T>(ComposedApp app, string name)
        where T : Control
    {
        var found = FirstRow(app).GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);
        Assert.NotNull(found);
        return found;
    }

    // The real control a step points at, in the layout that is showing.
    private static Control Target(ComposedApp app, TourStep step)
    {
        if (step == TourStep.History && app.Shell.IsLibraryLayout)
        {
            var item = Named<ListBox>(app, "TopNavList").GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault(candidate => candidate.DataContext is NavItemViewModel { Destination: NavDestination.History });
            Assert.NotNull(item);
            return item;
        }

        return step switch
        {
            TourStep.Profile => InRow<Grid>(app, "ProfileNameCell"),
            TourStep.Play => InRow<Button>(app, "PlayButton"),
            TourStep.History => Named<ToggleButton>(app, "HistoryChip"),
            _ => app.Shell.IsLibraryLayout ? Named<Button>(app, "LibraryNewProfileButton") : Named<Button>(app, "NewProfileButton"),
        };
    }

    private static void Next(ComposedApp app)
    {
        app.Shell.Tour.NextCommand.Execute(null);
        app.Settle();
    }

    private static void AssertOutlines(ComposedApp app, Control target, string what)
    {
        var ring = Ring(app);
        Assert.NotNull(ring);
        Assert.True(ring.IsEffectivelyVisible, $"{what}: the outline is not showing.");
        var around = Bounds(app, ring);
        var inside = Bounds(app, target);
        Assert.True(around.Left <= inside.Left + 0.5 && around.Top <= inside.Top + 0.5 && around.Right >= inside.Right - 0.5 && around.Bottom >= inside.Bottom - 0.5, $"{what}: the outline {around} does not surround the control {inside}.");
        Assert.True(around.Width <= inside.Width + 24 && around.Height <= inside.Height + 24, $"{what}: the outline {around} is far larger than the control {inside}.");
    }

    [AvaloniaFact]
    public async Task The_first_card_and_an_outline_appear_on_the_new_profile_row_once_the_screen_has_one()
    {
        using var app = await StartAsync();

        var card = Card(app);

        Assert.NotNull(card);
        Assert.True(card.IsEffectivelyVisible);
        Assert.Contains("This is a profile", app.All<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text));
        Assert.Contains("Step 1 of 4", app.All<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text));
        AssertOutlines(app, Target(app, TourStep.Profile), "Step 1");
    }

    [AvaloniaFact]
    public async Task The_card_sits_beside_the_control_and_the_screen_stays_usable_under_it()
    {
        using var app = await StartAsync();

        foreach (var step in new[] { TourStep.Profile, TourStep.Play, TourStep.History, TourStep.NewProfile })
        {
            var target = Target(app, step);
            var card = Bounds(app, Card(app)!);
            var control = Bounds(app, target);

            Assert.False(card.Intersects(control), $"{step}: the card {card} covers the control {control}.");
            // A click on the control reaches the page under the tour, never the outline or the layer.
            var hit = ClickLandsOn(app, control.Center);
            Assert.NotNull(hit);
            Assert.False(hit.GetSelfAndVisualAncestors().OfType<TourOverlay>().Any(), $"{step}: a click on the control would land on the tour's layer ({hit.GetType().Name}).");
            Assert.True(app.View!.IsVisualAncestorOf(hit), $"{step}: a click on the control reaches {hit.GetType().Name}, which is outside the page.");

            // A button, chip or nav item is itself what the click lands on; the profile's name cell is plain layout, so only the layer is ruled out there.
            if (target is Button or ToggleButton or ListBoxItem)
            {
                Assert.True(ReferenceEquals(hit, target) || hit.GetVisualAncestors().Contains(target), $"{step}: a click on the control reaches {hit.GetType().Name}, which is not part of it.");
            }
            Next(app);
        }
    }

    [AvaloniaFact]
    public async Task Next_moves_the_outline_to_Play_then_History_then_New_profile_and_the_last_step_says_where_to_find_the_tour()
    {
        using var app = await StartAsync();
        AssertOutlines(app, Target(app, TourStep.Profile), "Step 1");

        Next(app);
        AssertOutlines(app, Target(app, TourStep.Play), "Step 2");
        Next(app);
        AssertOutlines(app, Target(app, TourStep.History), "Step 3");
        Next(app);
        AssertOutlines(app, Target(app, TourStep.NewProfile), "Step 4");

        var texts = app.All<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToList();
        Assert.Contains("You can see this again in Settings > Help.", texts);
        Assert.Equal("Done", Named<Button>(app, "TourNextButton")!.Content);
    }

    [AvaloniaFact]
    public async Task In_the_library_layout_the_third_step_points_at_the_History_item_of_the_top_bar()
    {
        using var app = await StartAsync(ShellLayout.Library);

        Next(app);
        Next(app);

        AssertOutlines(app, Target(app, TourStep.History), "Step 3 in the library layout");
        Next(app);
        AssertOutlines(app, Target(app, TourStep.NewProfile), "Step 4 in the library layout");
    }

    [AvaloniaFact]
    public async Task Focus_moves_to_the_card_when_the_tour_starts_and_at_every_step()
    {
        using var app = await StartAsync();

        Assert.Same(Card(app), app.Focused);
        for (var step = 1; step < 4; step++)
        {
            Named<Button>(app, "TourNextButton")!.Focus();
            Next(app);
            Assert.Same(Card(app), app.Focused);
        }
    }

    [AvaloniaFact]
    public async Task The_card_is_named_with_its_step_and_title_and_carries_the_text_for_assistive_technology()
    {
        using var app = await StartAsync();

        var card = Card(app)!;

        Assert.Equal("Tour, step 1 of 4: This is a profile", AutomationProperties.GetName(card));
        Assert.Equal(app.Shell.Tour.Current.Body, AutomationProperties.GetHelpText(card));
        Assert.True(card.Focusable);
    }

    [AvaloniaFact]
    public async Task Each_step_is_announced_through_the_windows_announcer()
    {
        using var app = await StartAsync();
        var announcer = app.All<AnnouncementRegion>().First(region => region.Name == "PageAnnouncer");
        var spoken = new List<string>();
        ControlAutomationPeer.CreatePeerForElement(announcer).PropertyChanged += (_, e) =>
        {
            if (e.Property == AutomationElementIdentifiers.NameProperty && e.NewValue is string { Length: > 0 } name)
            {
                spoken.Add(name);
            }
        };

        Next(app);

        Assert.Equal(["Play puts the right files in place. Press Play and SavePoint makes your game folders match this profile, then starts the game. When you close the game, it asks whether to keep the session as a new snapshot. To load the files without starting the game, choose Switch to this profile from the profile's menu."], spoken);
    }

    [AvaloniaFact]
    public async Task Skip_tour_ends_it_at_any_step_and_puts_focus_on_the_Play_button_of_the_profile()
    {
        using var app = await StartAsync();
        Next(app);
        Next(app);

        var skip = Named<Button>(app, "TourSkipButton");
        Assert.NotNull(skip);
        Assert.Equal("Skip tour", skip.Content);
        skip.Command!.Execute(null);
        app.Settle();

        Assert.False(app.Shell.Tour.IsActive);
        Assert.False(CardShown(app));
        Assert.Same(Target(app, TourStep.Play), app.Focused);
    }

    [AvaloniaFact]
    public async Task Escape_in_the_card_skips_the_tour_from_any_step_and_puts_focus_on_Play()
    {
        using var app = await StartAsync();
        Next(app);

        app.Press(Key.Escape, PhysicalKey.Escape);

        Assert.False(app.Shell.Tour.IsActive);
        Assert.Same(Target(app, TourStep.Play), app.Focused);
    }

    [AvaloniaFact]
    public async Task Done_on_the_last_step_ends_the_tour_and_Back_goes_back_a_step()
    {
        using var app = await StartAsync();
        Assert.False(Named<Button>(app, "TourBackButton")!.IsEffectivelyVisible);
        Next(app);

        var back = Named<Button>(app, "TourBackButton")!;
        Assert.True(back.IsEffectivelyVisible);
        back.Command!.Execute(null);
        app.Settle();
        Assert.Equal(TourStep.Profile, app.Shell.Tour.Current.Step);
        AssertOutlines(app, Target(app, TourStep.Profile), "Back to step 1");

        for (var step = 0; step < 4; step++)
        {
            Next(app);
        }

        Assert.False(app.Shell.Tour.IsActive);
        Assert.False(CardShown(app));
    }

    [AvaloniaFact]
    public async Task The_tour_waits_behind_a_dialog_and_comes_back_with_focus_on_its_card_when_the_dialog_closes()
    {
        using var app = await StartAsync();
        var dialog = new Features.Common.Samples.SampleDialogViewModel();

        _ = app.Main.Dialogs.ShowAsync(dialog);
        app.Settle();
        Assert.False(CardShown(app));
        dialog.AnswerFirstCommand.Execute(null);
        app.Settle();

        Assert.True(Card(app)!.IsEffectivelyVisible);
        Assert.Same(Card(app), app.Focused);
    }

    [AvaloniaFact]
    public async Task The_tour_has_no_animation_so_it_is_the_same_with_reduced_motion()
    {
        using var app = await StartAsync();

        Assert.True(Card(app)!.Transitions is null or { Count: 0 });
        Assert.True(Ring(app)!.Transitions is null or { Count: 0 });

        // The buttons' own state layers keep the theme's fade, which the theme already switches off for reduced motion; nothing of the tour's does.
        Assert.DoesNotContain(Card(app).GetVisualDescendants().OfType<Control>(), control => control.Transitions is { Count: > 0 } && control.FindAncestorOfType<Button>() is null);
    }

    [AvaloniaFact]
    public async Task Showing_the_tour_again_from_Settings_brings_back_the_card_on_the_Games_screen()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Show<SettingsScreenViewModel>(NavDestination.Settings);
        var button = Named<Button>(app, "TourButton");

        Assert.NotNull(button);
        Assert.Equal("Show the tour again", button.Content);
        button.Command!.Execute(null);
        app.Settle();

        Assert.Equal(NavDestination.Games, app.Shell.Destination);
        Assert.True(Card(app)!.IsEffectivelyVisible);
        AssertOutlines(app, Target(app, TourStep.Profile), "Step 1 again");
    }

    [AvaloniaFact]
    public async Task The_end_of_the_page_has_a_button_that_shows_the_tour_again()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.ShowHelp();
        app.Settle();

        var button = Named<Button>(app, "TourButton");

        Assert.NotNull(button);
        Assert.True(button.IsEffectivelyVisible);
        Assert.Equal("Show the tour again", button.Content);
        button.Command!.Execute(null);
        app.Settle();
        Assert.True(Card(app)!.IsEffectivelyVisible);
    }

    public static TheoryData<ShellLayout, double, double> Sizes()
    {
        var data = new TheoryData<ShellLayout, double, double>();
        foreach (var layout in new[] { ShellLayout.Sidebar, ShellLayout.Library })
        {
            foreach (var (width, height) in new[] { (1040d, 660d), (960d, 460d), (910d, 432d), (640d, 420d) })
            {
                data.Add(layout, width, height);
            }
        }

        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Sizes))]
    public async Task At_every_size_and_in_both_layouts_each_step_shows_its_card_and_outline_inside_the_window(ShellLayout layout, double width, double height)
    {
        using var app = await StartAsync(layout, width, height);

        foreach (var step in new[] { TourStep.Profile, TourStep.Play, TourStep.History, TourStep.NewProfile })
        {
            var what = $"{layout} {width}x{height} {step}";
            var card = Bounds(app, Card(app)!);
            var window = new Rect(0, 0, app.Window.ClientSize.Width, app.Window.ClientSize.Height);

            Assert.True(window.Contains(card.TopLeft) && window.Contains(card.BottomRight - new Point(1, 1)), $"{what}: the card {card} is outside the {window.Width:0}x{window.Height:0} window.");
            AssertOutlines(app, Target(app, step), what);
            Assert.False(card.Intersects(Bounds(app, Target(app, step))), $"{what}: the card covers its control.");
            Next(app);
        }
    }

    [AvaloniaTheory]
    [InlineData(ShellLayout.Sidebar)]
    [InlineData(ShellLayout.Library)]
    public async Task The_tour_adds_nothing_the_layout_audit_objects_to_at_a_scaled_size(ShellLayout layout)
    {
        using var app = await StartAsync(layout, 910, 432);

        for (var step = 0; step < 4; step++)
        {
            var problems = LayoutAudit.Problems(app.Window);
            Assert.True(problems.Count == 0, $"{layout} step {step + 1}: {string.Join("; ", problems)}");
            Next(app);
        }
    }
}
