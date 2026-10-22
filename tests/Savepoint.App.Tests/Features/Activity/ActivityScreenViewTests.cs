using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Savepoint.App.Features.Activity;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.History;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using static Savepoint.App.Tests.Fakes.MockupScenario;

namespace Savepoint.App.Tests.Features.Activity;

public class ActivityScreenViewTests
{
    private static Task<ScreenHarness> StartAsync(FakeBackend? backend = null, ThemePreference theme = ThemePreference.Dark, double width = 1040, double height = 660)
    {
        backend ??= new FakeBackend(ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings with { Theme = theme }));
        var clock = backend.Clock;
        return ScreenHarness.StartAsync(
            "activity",
            context => [new ActivityScreenFactory(new ActivityDependencies(context.Services.Jobs, context.Text, context.Dispatcher, clock))],
            NavDestination.Activity,
            backend,
            theme,
            width,
            height);
    }

    private static ActivityScreenViewModel Screen(ScreenHarness harness) => (ActivityScreenViewModel)harness.Shell.CurrentScreen!;

    private static ScrollViewer LinesScroll(ScreenHarness harness) =>
        harness.Find<ListBox>("LineList").GetVisualDescendants().OfType<ScrollViewer>().First();

    // A game's jobs in every state the screen draws: one running with files done, one done, one that failed, one partly done and one canceled.
    private static async Task<FakeBackend> BusyAsync(ThemePreference theme = ThemePreference.Dark)
    {
        var backend = new FakeBackend(ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings with { Theme = theme }));
        await ActivityScenario.AddJobsAsync(backend);
        return backend;
    }

    // ---- The list and the header

    [AvaloniaFact]
    public async Task The_header_shows_the_title_the_game_the_job_count_and_the_scope_choice()
    {
        using var harness = await StartAsync(await BusyAsync());

        var texts = harness.VisibleTexts().ToList();

        Assert.Contains("Activity", texts);
        Assert.Contains("Skyrim Special Edition · 5 jobs", texts);
        Assert.Contains("This game", texts);
        Assert.Contains("All games", texts);
        Assert.Equal("Show jobs for", AutomationProperties.GetName(harness.Find<ListBox>("ScopeToggle")));
    }

    [AvaloniaFact]
    public async Task Each_row_shows_what_the_job_is_its_profile_its_state_its_progress_and_when_it_started()
    {
        using var harness = await StartAsync(await BusyAsync());

        var texts = harness.VisibleTexts().ToList();

        Assert.Contains("Taking a snapshot of Skyrim Special Edition", texts);
        Assert.Contains("Profile: Modded playthrough - 7-2-26", texts);
        Assert.Contains("In progress", texts);
        Assert.Contains("Done", texts);
        Assert.Contains("Failed", texts);
        Assert.Contains("Partly done", texts);
        Assert.Contains("Canceled", texts);
        Assert.Contains("30 of 120 files", texts);
        Assert.Contains(texts, text => text is not null && text.StartsWith("Started Jul 8,", StringComparison.Ordinal));
        Assert.Contains(texts, text => text is not null && text.StartsWith("Finished Jul 8,", StringComparison.Ordinal));
        Assert.Equal(5, harness.Find<ListBox>("JobList").GetVisualDescendants().OfType<ListBoxItem>().Count());
    }

    [AvaloniaFact]
    public async Task A_running_jobs_bar_moves_and_a_finished_jobs_bar_is_gone()
    {
        using var harness = await StartAsync(await BusyAsync());

        var bars = harness.Find<ListBox>("JobList").GetVisualDescendants().OfType<ProgressBar>().Where(bar => bar.IsEffectivelyVisible).ToList();

        var bar = Assert.Single(bars);
        Assert.False(bar.IsIndeterminate);
        Assert.Equal(25, bar.Value, 1);
    }

    [AvaloniaFact]
    public async Task Choosing_all_games_in_the_toggle_lists_every_job()
    {
        var backend = await BusyAsync();
        FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(CaretakerId));
        using var harness = await StartAsync(backend);

        harness.Find<ListBox>("ScopeToggle").SelectedIndex = 1;
        harness.Settle();

        Assert.Contains("All games · 6 jobs", harness.VisibleTexts());
        Assert.Contains("Freeing up space for The Last Caretaker", harness.VisibleTexts());
    }

    [AvaloniaFact]
    public async Task A_job_chosen_in_code_is_scrolled_into_view_in_a_short_list()
    {
        using var harness = await StartAsync(await BusyAsync(), width: 910, height: 432);
        var screen = Screen(harness);
        var list = harness.Find<ListBox>("JobList");
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "the jobs should overflow the list");

        screen.SelectedRow = screen.Rows.Single(row => row.StatusText == "Canceled");
        harness.Settle();

        var item = list.GetVisualDescendants().OfType<ListBoxItem>().SingleOrDefault(candidate => ReferenceEquals(candidate.DataContext, screen.SelectedRow));
        Assert.NotNull(item);
        var top = item.TranslatePoint(default, scroll)!.Value.Y;
        Assert.True(top >= -1 && top + item.Bounds.Height <= scroll.Viewport.Height + 1, $"the row sits at {top} to {top + item.Bounds.Height} in a viewport {scroll.Viewport.Height} tall");
    }

    // ---- The chosen job's details

    [AvaloniaFact]
    public async Task The_chosen_job_shows_its_lines_in_monospace_and_a_copy_button_and_cancel_while_it_can_stop()
    {
        using var harness = await StartAsync(await BusyAsync());

        var lines = harness.Find<ListBox>("LineList").GetVisualDescendants().OfType<TextBlock>().Where(block => block.Classes.Contains("mono")).ToList();

        Assert.Equal(32, harness.Find<ListBox>("LineList").ItemCount);
        Assert.NotEmpty(lines);
        Assert.True(harness.Find<Button>("CancelButton").IsEffectivelyVisible);
        Assert.True(harness.Find<Button>("CopyButton").IsEffectivelyVisible);
        Assert.Equal(SelectionMode.Multiple, harness.Find<ListBox>("LineList").SelectionMode);
        Assert.Equal("Cancel Taking a snapshot of Skyrim Special Edition", AutomationProperties.GetName(harness.Find<Button>("CancelButton")));
    }

    [AvaloniaFact]
    public async Task A_finished_job_shows_no_cancel_and_a_failed_one_shows_its_error_card()
    {
        using var harness = await StartAsync(await BusyAsync());
        var screen = Screen(harness);

        screen.SelectedRow = screen.Rows.Single(row => row.StatusText == "Failed");
        harness.Settle();

        Assert.False(harness.Find<Button>("CancelButton").IsEffectivelyVisible);
        Assert.True(harness.Find<ContentControl>("ErrorHost").IsEffectivelyVisible);
        Assert.Single(harness.All<ErrorCardView>());
    }

    [AvaloniaFact]
    public async Task Pressing_cancel_shows_that_the_job_is_stopping_and_hides_the_button()
    {
        using var harness = await StartAsync(await BusyAsync());

        harness.Find<Button>("CancelButton").Command!.Execute(null);
        harness.Settle();

        Assert.Contains("Stopping…", harness.VisibleTexts());
        Assert.Contains("Stopping. SavePoint is finishing the step it is on, then it stops.", harness.VisibleTexts());
        Assert.False(harness.Find<Button>("CancelButton").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task A_job_that_cannot_be_stopped_shows_why_where_the_cancel_button_would_be()
    {
        var backend = FakeBackend.Mockup();
        FakeAssert.Ok(await backend.Settings.ClearAllDataAsync());
        using var harness = await StartAsync(backend);
        harness.Find<ListBox>("ScopeToggle").SelectedIndex = 1;
        harness.Settle();

        Assert.False(harness.Find<Button>("CancelButton").IsEffectivelyVisible);
        Assert.Contains("Clearing SavePoint's data can't be stopped once it has started.", harness.VisibleTexts());
    }

    // ---- Following new lines

    [AvaloniaFact]
    public async Task New_lines_follow_the_bottom_until_the_reader_scrolls_up_and_then_leave_their_place_alone()
    {
        var backend = await BusyAsync();
        using var harness = await StartAsync(backend);
        var running = backend.Jobs.LastStarted(JobKind.Capture).JobId;
        var scroll = LinesScroll(harness);

        // The list opens at its end, and a new line keeps it there.
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "the lines should overflow the pane");
        Assert.Equal(scroll.Extent.Height - scroll.Viewport.Height, scroll.Offset.Y, 1);
        backend.Jobs.ReportProgress(running, new JobProgress(FilesDone: 31, FilesTotal: 120, CurrentPath: @"Data\Textures\Landscape\tile031.dds"));
        harness.Settle();
        Assert.Equal(scroll.Extent.Height - scroll.Viewport.Height, scroll.Offset.Y, 1);

        // The reader scrolls to the top; more lines arrive and the view stays where it is.
        scroll.Offset = new Vector(0, 0);
        harness.Settle();
        Assert.False(Screen(harness).Details!.IsFollowing);
        Assert.True(harness.Find<Button>("JumpButton").IsEffectivelyVisible);
        backend.Jobs.ReportProgress(running, new JobProgress(FilesDone: 32, FilesTotal: 120, CurrentPath: @"Data\Textures\Landscape\tile032.dds"));
        harness.Settle();
        Assert.Equal(0, scroll.Offset.Y, 1);

        // Jumping to the latest line follows again.
        harness.Find<Button>("JumpButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        harness.Settle();
        Assert.True(Screen(harness).Details!.IsFollowing);
        Assert.Equal(scroll.Extent.Height - scroll.Viewport.Height, scroll.Offset.Y, 1);
        backend.Jobs.ReportProgress(running, new JobProgress(FilesDone: 33, FilesTotal: 120, CurrentPath: @"Data\Textures\Landscape\tile033.dds"));
        harness.Settle();
        Assert.Equal(scroll.Extent.Height - scroll.Viewport.Height, scroll.Offset.Y, 1);
        Assert.False(harness.Find<Button>("JumpButton").IsEffectivelyVisible);
    }

    // ---- Copy

    [AvaloniaFact]
    public async Task Selecting_lines_in_the_list_tells_the_details_which_ones_to_copy()
    {
        using var harness = await StartAsync(await BusyAsync());
        var list = harness.Find<ListBox>("LineList");
        var copied = new List<string>();
        Screen(harness).Details!.CopyRequested += (_, text) => copied.Add(text);

        list.SelectedItems!.Add(list.Items[2]);
        list.SelectedItems.Add(list.Items[3]);
        harness.Settle();
        Screen(harness).Details!.CopyCommand.Execute(null);

        Assert.Equal([@"Data\Textures\Landscape\tile001.dds" + "\n" + @"Data\Textures\Landscape\tile002.dds"], copied);
    }

    [AvaloniaFact]
    public async Task Copy_puts_the_lines_on_the_clipboard()
    {
        using var harness = await StartAsync(await BusyAsync());
        var clipboard = TopLevelOf(harness).Clipboard;
        Assert.NotNull(clipboard);

        Screen(harness).Details!.CopyCommand.Execute(null);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        var text = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
        Assert.StartsWith("Taking the snapshot \"Session 7-8-26\" of the profile", text, StringComparison.Ordinal);
        Assert.Equal(Screen(harness).Details!.AllText, text);
    }

    private static TopLevel TopLevelOf(ScreenHarness harness) => TopLevel.GetTopLevel(harness.View)!;

    // ---- Empty states

    [AvaloniaFact]
    public async Task With_no_jobs_the_screen_says_so_in_plain_words()
    {
        using var harness = await StartAsync();

        var texts = harness.VisibleTexts().ToList();

        Assert.Contains("Nothing has run yet", texts);
        Assert.Contains("When SavePoint takes a snapshot, switches your files, frees up space or moves its folder, the job shows up here.", texts);
        Assert.False(harness.Find<Button>("ShowAllButton").IsEffectivelyVisible);
        Assert.False(harness.Find<Grid>("Panes").IsEffectivelyVisible);
        harness.Render("empty");
    }

    [AvaloniaFact]
    public async Task A_game_with_no_jobs_offers_all_games()
    {
        var backend = FakeBackend.Mockup();
        FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(CaretakerId));
        using var harness = await StartAsync(backend);

        Assert.Contains("Nothing has run for this game", harness.VisibleTexts());
        Assert.True(harness.Find<Button>("ShowAllButton").IsEffectivelyVisible);

        harness.Find<Button>("ShowAllButton").Command!.Execute(null);
        harness.Settle();

        Assert.True(harness.Find<Grid>("Panes").IsEffectivelyVisible);
        Assert.False(harness.Find<StackPanel>("EmptyState").IsEffectivelyVisible);
    }

    // ---- Fit

    [AvaloniaFact]
    public async Task The_jobs_and_the_details_sit_side_by_side_when_wide_and_stack_when_narrow()
    {
        using var harness = await StartAsync(await BusyAsync(), width: 1040);
        var list = harness.Find<Border>("ListPane");
        var details = harness.Find<Panel>("DetailsPane");

        Assert.Equal(0, Grid.GetRow(details));
        Assert.Equal(2, Grid.GetColumn(details));
        Assert.True(details.Bounds.X > list.Bounds.Right - 1);

        harness.Window.Width = 700;
        harness.Settle();

        Assert.Equal(2, Grid.GetRow(details));
        Assert.Equal(0, Grid.GetColumn(details));
        Assert.True(details.Bounds.Y >= list.Bounds.Bottom - 1);
    }

    [AvaloniaTheory]
    [InlineData(910, 432)]
    [InlineData(960, 460)]
    [InlineData(640, 420)]
    public async Task Nothing_is_clipped_at_the_scaled_and_smallest_window_sizes(int width, int height)
    {
        using var harness = await StartAsync(await BusyAsync(), width: width, height: height);

        var panes = harness.Find<Grid>("Panes");
        var header = harness.Find<Border>("Header");

        Assert.True(header.Bounds.Right <= harness.Window.Width + 0.5);
        Assert.True(panes.Bounds.Width > 0 && panes.Bounds.Height > 0);
        Assert.True(harness.Find<Border>("ListPane").Bounds.Right <= harness.Window.Width + 0.5);
        Assert.True(harness.Find<Panel>("DetailsPane").Bounds.Right <= harness.Window.Width + 0.5);
        harness.Render($"fit-{width}x{height}");
    }

    [AvaloniaTheory]
    [InlineData(1040, 900, false)]
    [InlineData(910, 432, true)]
    public async Task The_lines_fill_a_tall_details_pane_and_a_short_one_scrolls_instead_of_squeezing_them(int width, int height, bool scrolls)
    {
        using var harness = await StartAsync(await BusyAsync(), width: width, height: height);
        var lines = harness.Find<Border>("LinesHost");
        var scroller = harness.Find<ScrollViewer>("Scroller");

        Assert.True(lines.Bounds.Height >= 179.5, $"the lines are {lines.Bounds.Height} px tall");
        if (scrolls)
        {
            Assert.Equal(180, lines.Bounds.Height, 1);
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height + 1);
        }
        else
        {
            Assert.True(lines.Bounds.Height > 300, $"the lines are {lines.Bounds.Height} px tall");
            Assert.True(scroller.Extent.Height <= scroller.Viewport.Height + 1);
        }
    }

    // ---- Accessibility

    [AvaloniaFact]
    public async Task A_job_with_no_progress_yet_has_a_bar_that_sweeps_and_holds_still_under_reduced_motion()
    {
        var backend = FakeBackend.Mockup();
        FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(SkyrimId));
        using var harness = await StartAsync(backend);
        var bar = harness.Find<ListBox>("JobList").GetVisualDescendants().OfType<ProgressBar>().Single(candidate => candidate.IsEffectivelyVisible);
        Assert.True(bar.IsIndeterminate);

        try
        {
            Assert.True(Sweeping(harness, bar));

            ((App)Application.Current!).Theme.ReduceMotion = true;

            Assert.False(Sweeping(harness, bar));
            Assert.True(bar.GetVisualDescendants().OfType<Control>().First(part => part.Name == "ReducedMotionIndicator").IsEffectivelyVisible);
        }
        finally
        {
            ThemeTestSupport.ResetApplicationTheme();
        }
    }

    // Whether the moving segment is under an animation: it starts off its resting place and returns to it when the animation stops.
    private static bool Sweeping(ScreenHarness harness, ProgressBar bar)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(5);
        harness.Settle();
        return bar.GetVisualDescendants().OfType<Control>().First(part => part.Name == "IndeterminateProgressBarIndicator").RenderTransform?.Value.M31 is not (null or 0);
    }

    [AvaloniaFact]
    public async Task Every_control_has_a_name_a_screen_reader_can_say()
    {
        using var harness = await StartAsync(await BusyAsync());

        Assert.Empty(AutomationAudit.Problems(harness.View));
    }

    [AvaloniaFact]
    public async Task Each_row_is_named_with_the_job_its_state_and_when_it_started_and_the_list_has_a_name()
    {
        using var harness = await StartAsync(await BusyAsync());

        var rows = harness.Find<ListBox>("JobList").GetVisualDescendants().OfType<ListBoxItem>().ToList();

        Assert.Equal("Jobs", AutomationProperties.GetName(harness.Find<ListBox>("JobList")));
        Assert.All(rows, row => Assert.Equal(((JobRowViewModel)row.DataContext!).AutomationName, AutomationProperties.GetName(row)));
        Assert.Contains(rows, row => AutomationProperties.GetName(row)?.StartsWith("Taking a snapshot of Skyrim Special Edition. In progress. Profile: Modded playthrough - 7-2-26. 30 of 120 files.", StringComparison.Ordinal) == true);
    }

    [AvaloniaFact]
    public async Task A_change_of_state_is_spoken_by_a_polite_live_region()
    {
        var backend = FakeBackend.Mockup();
        using var harness = await StartAsync(backend);
        var region = harness.Find<Avalonia.Controls.TextBlock>("Announcer");

        var id = FakeAssert.Ok(await backend.Profiles.CaptureAsync(SkyrimId, ModdedId, "Session")).JobId;
        harness.Settle();
        Assert.Equal("Taking a snapshot of Skyrim Special Edition: In progress.", region.Text);

        backend.Jobs.Complete(id);
        harness.Settle();

        Assert.Equal("Taking a snapshot of Skyrim Special Edition: Done.", region.Text);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(region));
    }

    [AvaloniaFact]
    public async Task The_same_words_twice_in_a_row_are_spoken_twice()
    {
        var backend = FakeBackend.Mockup();
        using var harness = await StartAsync(backend);
        var region = harness.Find<AnnouncementRegion>("Announcer");
        FakeAssert.Ok(await backend.Profiles.CaptureAsync(SkyrimId, ModdedId, "First"));
        harness.Settle();
        var heard = new List<string?>();
        region.PropertyChanged += (_, e) =>
        {
            if (e.Property == AnnouncementRegion.MessageProperty)
            {
                heard.Add(e.NewValue as string);
            }
        };

        FakeAssert.Ok(await backend.Profiles.CaptureAsync(SkyrimId, ModdedId, "Second"));
        harness.Settle();

        Assert.Equal(["", "Taking a snapshot of Skyrim Special Edition: In progress."], heard);
    }

    [AvaloniaFact]
    public async Task Tab_reaches_the_scope_choice_the_jobs_cancel_copy_and_the_lines_in_the_order_they_are_drawn()
    {
        using var harness = await StartAsync(await BusyAsync());

        var order = new List<Control>
        {
            harness.Find<ListBox>("ScopeToggle"),
            harness.Find<ListBox>("JobList"),
            harness.Find<Button>("CancelButton"),
            harness.Find<Button>("CopyButton"),
            harness.Find<ListBox>("LineList"),
        };

        Assert.All(order, control => Assert.True(IsReachable(control), control.Name));
        var positions = order.Select(control => control.TranslatePoint(default, harness.View)!.Value).ToList();
        Assert.True(positions[0].Y < positions[1].Y);
        Assert.True(positions[2].Y < positions[3].Y && positions[3].Y < positions[4].Y);
        Assert.True(positions[1].X < positions[2].X);
    }

    // A list is reached through its items; a button or anything else is reached itself.
    private static bool IsReachable(Control control) =>
        control.IsEffectivelyVisible
        && control.IsEffectivelyEnabled
        && (control is ListBox list ? list.GetVisualDescendants().OfType<ListBoxItem>().Any(item => item.Focusable) : control.Focusable);

    // ---- Renders

    [AvaloniaTheory]
    [InlineData(ThemePreference.Dark)]
    [InlineData(ThemePreference.Light)]
    public async Task The_screen_renders_a_running_a_finished_a_failed_a_partly_done_and_a_canceled_job(ThemePreference theme)
    {
        using var harness = await StartAsync(await BusyAsync(theme), theme);

        var path = harness.Render($"running-{theme.ToString().ToLowerInvariant()}");

        Assert.True(File.Exists(path));
    }

    [AvaloniaTheory]
    [InlineData(ThemePreference.Dark)]
    [InlineData(ThemePreference.Light)]
    public async Task The_screen_renders_the_details_of_a_failed_and_a_partly_done_job(ThemePreference theme)
    {
        using var harness = await StartAsync(await BusyAsync(theme), theme);
        var screen = Screen(harness);

        screen.SelectedRow = screen.Rows.Single(row => row.StatusText == "Failed");
        harness.Render($"failed-{theme.ToString().ToLowerInvariant()}");
        screen.SelectedRow = screen.Rows.Single(row => row.StatusText == "Partly done");
        var path = harness.Render($"partly-done-{theme.ToString().ToLowerInvariant()}");

        Assert.True(File.Exists(path));
    }

    [AvaloniaFact]
    public async Task The_screen_renders_a_canceled_job_and_every_games_jobs()
    {
        var backend = await BusyAsync();
        FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(CaretakerId));
        FakeAssert.Ok(await backend.Settings.RelocateStorageAsync(@"D:\SavePoint"));
        using var harness = await StartAsync(backend);
        var screen = Screen(harness);

        screen.SelectedRow = screen.Rows.Single(row => row.StatusText == "Canceled");
        harness.Render("canceled-dark");
        harness.Find<ListBox>("ScopeToggle").SelectedIndex = 1;
        harness.Settle();
        var path = harness.Render("all-games-dark");

        Assert.True(File.Exists(path));
    }

    [AvaloniaFact]
    public async Task The_library_layout_shows_the_screen_under_the_top_bar_with_Activity_in_the_navigation()
    {
        var scenario = ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings with { Layout = ShellLayout.Library });
        var backend = new FakeBackend(scenario);
        FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(SkyrimId));
        using var harness = await StartAsync(backend);

        Assert.Contains("Activity", harness.All<ListBox>().Single(list => list.Name == "TopNavList").GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
        var path = harness.Render("library-layout");

        Assert.True(File.Exists(path));
        _ = HistoryRig.MockupZone;
    }
}
