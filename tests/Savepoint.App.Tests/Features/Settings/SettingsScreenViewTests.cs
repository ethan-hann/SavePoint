using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Savepoint.App.Composition;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.History;
using Savepoint.App.Tests.Theme;
using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Settings;

public class SettingsScreenViewTests
{
    private const string _notices = @"C:\Program Files\SavePoint\THIRD-PARTY-NOTICES.md";
    private const string _license = @"C:\Program Files\SavePoint\LICENSE";

    internal static Task<ScreenHarness> StartAsync(
        FakeBackend? backend = null,
        ThemePreference theme = ThemePreference.Dark,
        double width = 1040,
        double height = 660,
        bool realDialogs = false,
        string? notices = _notices,
        string? license = _license)
    {
        backend ??= new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Theme = theme }));
        return ScreenHarness.StartAsync(
            "settings",
            context => [new SettingsScreenFactory(FeatureComposition.SettingsDependenciesOf(context, new SettingsEnvironment(new RecordingFileRevealer(), () => notices, "1.2.3", FindLicense: () => license)))],
            NavDestination.Settings,
            backend,
            theme,
            width,
            height,
            realDialogs);
    }

    private static SettingsScreenViewModel Screen(ScreenHarness harness) => (SettingsScreenViewModel)harness.Shell.CurrentScreen!;

    // ---- What the screen shows

    [AvaloniaFact]
    public async Task Every_section_shows_with_its_heading_and_copy()
    {
        using var harness = await StartAsync();

        var texts = harness.VisibleTexts().ToList();

        Assert.Contains("Settings", texts);
        foreach (var expected in new[]
        {
            "WHERE SAVEPOINT KEEPS YOUR SNAPSHOTS",
            @"D:\SavePoint",
            "79.3 GB used by 3 games",
            "131 GB free",
            "Free up space",
            "Clears out snapshots no profile uses and deletes recently removed ones for good. SavePoint also tidies up by itself, but never deletes recently removed snapshots. You last freed up space on Jul 1.",
            "APPEARANCE",
            "Theme",
            "Match Windows",
            "Layout",
            "You can also switch this on the Games screen.",
            "Every game one click away",
            "Cover art up front",
            "Accent color",
            "BEFORE YOU PLAY",
            "Skip the check before playing when only your saves change",
            "A change to the game version or mods always shows the check.",
            "WHEN YOU FINISH PLAYING",
            "Ask whether to keep the session",
            "REMOVE ALL SAVEPOINT DATA",
            "ABOUT",
            "Version 1.2.3",
        })
        {
            Assert.Contains(expected, texts);
        }
    }

    [AvaloniaFact]
    public async Task The_buttons_carry_their_labels()
    {
        using var harness = await StartAsync();

        var buttons = harness.All<Button>().Where(button => button.IsEffectivelyVisible).Select(button => button.Content as string).ToList();

        Assert.Contains("Change", buttons);
        Assert.Contains("Free up space", buttons);
        Assert.DoesNotContain("Run now", buttons);
        Assert.Contains("Remove all data…", buttons);
        Assert.Contains("Third-party notices", buttons);
    }

    [AvaloniaFact]
    public async Task The_notices_button_is_hidden_when_the_file_was_not_shipped()
    {
        using var harness = await StartAsync(notices: null);

        Assert.False(harness.Find<Button>("NoticesButton").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task About_states_the_license_and_offers_the_license_text()
    {
        using var harness = await StartAsync();

        Assert.Contains("SavePoint is free software under the GNU GPL, version 3 or later, and comes with no warranty.", harness.VisibleTexts());
        var button = Assert.Single(harness.All<Button>(), candidate => candidate.IsEffectivelyVisible && (candidate.Content as string) == "License");
        Assert.Equal("LicenseButton", button.Name);
        Assert.Equal("License", AutomationAudit.NameOf(button));
    }

    [AvaloniaFact]
    public async Task The_license_button_sits_beside_the_notices_button()
    {
        using var harness = await StartAsync();

        var license = Assert.Single(harness.All<Button>(), button => button.Name == "LicenseButton");

        Assert.True(harness.Find<Button>("NoticesButton").Bounds.X < license.Bounds.X);
    }

    [AvaloniaFact]
    public async Task The_license_button_is_hidden_when_the_file_was_not_shipped_but_the_line_stays()
    {
        using var harness = await StartAsync(license: null);

        Assert.DoesNotContain(harness.All<Button>(), button => button.IsEffectivelyVisible && (button.Content as string) == "License");
        Assert.Contains("SavePoint is free software under the GNU GPL, version 3 or later, and comes with no warranty.", harness.VisibleTexts());
    }

    [AvaloniaFact]
    public async Task A_failed_read_shows_the_error_card_in_place_of_the_sections()
    {
        using var harness = await StartAsync();
        harness.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetAsync), ErrorInfo.From(new InvalidOperationException("No settings.")));

        // The shell read its own copy while starting; coming back to the screen reads again.
        harness.Shell.Navigate(NavDestination.Games);
        harness.Shell.Navigate(NavDestination.Settings);
        harness.Settle();

        Assert.True(harness.Find<ContentControl>("LoadErrorHost").IsEffectivelyVisible);
        Assert.False(harness.Find<StackPanel>("Sections").IsEffectivelyVisible);
        Assert.Contains(harness.All<Button>(), button => button.IsEffectivelyVisible && (button.Content as string) == "Try again");
    }

    [AvaloniaFact]
    public async Task The_usage_bar_shows_how_much_of_the_drive_is_used()
    {
        using var harness = await StartAsync();

        var bar = harness.Find<ProgressBar>("UsageBar");

        Assert.Equal(79.3 / 210, bar.Value, 3);
        Assert.Equal(1, bar.Maximum);
    }

    [AvaloniaFact]
    public async Task Without_a_summary_the_usage_bar_is_gone_and_the_rest_stays()
    {
        using var harness = await StartAsync();
        harness.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetStorageSummaryAsync), ErrorInfo.From(new InvalidOperationException("No size.")));

        harness.Shell.Navigate(NavDestination.Games);
        harness.Shell.Navigate(NavDestination.Settings);
        harness.Settle();

        Assert.False(harness.Find<StackPanel>("UsagePanel").IsEffectivelyVisible);
        Assert.True(harness.Find<Button>("RunNowButton").IsEffectivelyVisible);
    }

    // ---- Appearance

    [AvaloniaFact]
    public async Task The_current_theme_layout_and_accent_are_the_selected_ones()
    {
        using var harness = await StartAsync();

        Assert.Equal(2, harness.Find<ListBox>("ThemeSelector").SelectedIndex);
        Assert.Equal(0, harness.Find<ListBox>("LayoutCards").SelectedIndex);
        Assert.Equal(0, harness.Find<ListBox>("AccentSwatches").SelectedIndex);
    }

    [AvaloniaFact]
    public async Task Choosing_a_theme_in_the_control_saves_it_and_the_app_changes_theme()
    {
        using var harness = await StartAsync();

        harness.Find<ListBox>("ThemeSelector").SelectedIndex = 1;
        harness.Settle();

        Assert.Equal(ThemePreference.Light, FakeAssert.Ok(await harness.Backend.Settings.GetAsync()).Theme);
        Assert.False(((App)Avalonia.Application.Current!).Theme.IsDark);
    }

    [AvaloniaFact]
    public async Task Choosing_a_layout_card_switches_the_shell_layout()
    {
        using var harness = await StartAsync();

        harness.Find<ListBox>("LayoutCards").SelectedIndex = 1;
        harness.Settle();

        Assert.Equal(ShellLayout.Library, harness.Shell.Layout);
        Assert.Equal(2, harness.Find<ListBox>("LayoutCards").ItemCount);
    }

    [AvaloniaFact]
    public async Task The_swatches_are_drawn_in_the_five_raw_seed_colors_and_named()
    {
        using var harness = await StartAsync();
        var swatches = harness.Find<ListBox>("AccentSwatches");

        var drawn = harness.All<Border>()
            .Where(border => border.Width == 30 && border.Height == 30 && border.IsEffectivelyVisible && border.Background is ISolidColorBrush)
            .Select(border => ((ISolidColorBrush)border.Background!).Color)
            .ToList();

        Assert.Equal(
            Enum.GetValues<AccentColorId>().Select(accent => Color.FromUInt32(AccentSeeds.For(accent))),
            drawn);
        Assert.Equal(["Blue", "Violet", "Green", "Amber", "Crimson"], swatches.GetLogicalChildren().OfType<ListBoxItem>().Select(item => AutomationProperties.GetName(item)));
    }

    [AvaloniaFact]
    public async Task The_swatches_keep_their_seed_colors_after_the_accent_changes()
    {
        using var harness = await StartAsync();

        harness.Find<ListBox>("AccentSwatches").SelectedIndex = 4;
        harness.Settle();

        var violet = harness.All<Border>().Where(border => border.Width == 30 && border.Height == 30).Select(border => ((ISolidColorBrush)border.Background!).Color).ElementAt(1);
        Assert.Equal(Color.FromUInt32(AccentSeeds.For(AccentColorId.Violet)), violet);
        Assert.Equal(AccentColorId.Crimson, FakeAssert.Ok(await harness.Backend.Settings.GetAsync()).AccentColorId);
    }

    // ---- Before you play (D35)

    [AvaloniaFact]
    public async Task The_save_only_switch_sits_in_its_own_Before_you_play_group_with_the_wording_D35_gives_it()
    {
        using var harness = await StartAsync();

        var group = harness.Find<StackPanel>("BeforePlaySection");
        var skip = harness.Find<ToggleSwitch>("SkipSwitch");
        Assert.Contains(skip, group.GetVisualDescendants());
        var texts = group.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToList();
        Assert.Contains("BEFORE YOU PLAY", texts);
        Assert.Contains("Skip the check before playing when only your saves change", texts);
        Assert.Contains("A change to the game version or mods always shows the check.", texts);
        Assert.Equal("Skip the check before playing when only your saves change", AutomationAudit.NameOf(skip));
        Assert.Equal("A change to the game version or mods always shows the check.", AutomationProperties.GetHelpText(skip));
    }

    [AvaloniaFact]
    public async Task When_you_finish_playing_keeps_only_the_question_about_saving_the_session()
    {
        using var harness = await StartAsync();

        var group = harness.Find<StackPanel>("SessionSection");
        Assert.Contains(harness.Find<ToggleSwitch>("AskSwitch"), group.GetVisualDescendants());
        Assert.DoesNotContain(harness.Find<ToggleSwitch>("SkipSwitch"), group.GetVisualDescendants());
        var order = harness.All<StackPanel>().Where(panel => panel.Name is "BeforePlaySection" or "SessionSection").Select(panel => panel.Name).ToList();
        Assert.Equal(["BeforePlaySection", "SessionSection"], order);
    }

    [AvaloniaFact]
    public async Task The_session_question_says_what_happens_when_it_is_turned_off()
    {
        using var harness = await StartAsync();

        var group = harness.Find<StackPanel>("SessionSection");
        var ask = harness.Find<ToggleSwitch>("AskSwitch");
        var texts = group.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToList();
        const string hint = "When off, nothing is kept after you play. SavePoint asks about new changes before the next switch.";
        Assert.Contains(hint, texts);
        Assert.Equal("Ask whether to keep the session", AutomationAudit.NameOf(ask));
        Assert.Equal(hint, AutomationProperties.GetHelpText(ask));
    }

    // ---- When you finish playing

    [AvaloniaFact]
    public async Task The_switches_show_the_settings_and_save_when_flipped()
    {
        using var harness = await StartAsync();
        Assert.True(harness.Find<ToggleSwitch>("AskSwitch").IsChecked);
        Assert.False(harness.Find<ToggleSwitch>("SkipSwitch").IsChecked);

        harness.Find<ToggleSwitch>("SkipSwitch").IsChecked = true;
        harness.Settle();

        Assert.True(FakeAssert.Ok(await harness.Backend.Settings.GetAsync()).SkipConfirmWhenOnlySaveChanges);
    }

    // ---- Storage and removal

    [AvaloniaFact]
    public async Task Change_opens_the_folder_picker()
    {
        using var harness = await StartAsync();

        harness.Find<Button>("ChangeButton").Command!.Execute(null);

        Assert.Single(harness.FakeDialogs.FolderPicks);
    }

    [AvaloniaFact]
    public async Task The_unsafe_folder_note_shows_only_for_a_folder_the_installer_clears()
    {
        using var safe = await StartAsync();
        var backend = new FakeBackend(ScenarioBuilder.Sample());
        backend.State.StorageInInstallerFolder = true;
        using var flagged = await StartAsync(backend);

        Assert.False(safe.Find<TextBlock>("UnsafeStorageNote").IsEffectivelyVisible);
        Assert.True(flagged.Find<TextBlock>("UnsafeStorageNote").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task While_a_move_runs_its_panel_shows_with_progress_and_cancel()
    {
        using var harness = await StartAsync();
        FakeAssert.Ok(await harness.Backend.Settings.RelocateStorageAsync(@"E:\NewData"));
        var job = harness.Backend.Jobs.LastStarted(JobKind.RelocateStorage);

        harness.Backend.Jobs.ReportProgress(job.JobId, new JobProgress(BytesDone: 19_800_000_000, BytesTotal: 79_200_000_000, CurrentPath: @"Skyrim\Data\textures\wrbuilding05.dds"));
        harness.Settle();

        Assert.True(harness.Find<Border>("MovePanel").IsEffectivelyVisible);
        Assert.Equal(0.25, harness.Find<ProgressBar>("MoveBar").Value);
        Assert.False(harness.Find<ProgressBar>("MoveBar").IsIndeterminate);
        Assert.True(harness.Find<Button>("CancelMoveButton").IsEffectivelyVisible);
        Assert.Contains("19.8 GB of 79.2 GB", harness.VisibleTexts());
        Assert.False(harness.Find<Button>("ChangeButton").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task While_the_optimization_runs_run_now_gives_way_to_cancel()
    {
        using var harness = await StartAsync();

        harness.Find<Button>("RunNowButton").Command!.Execute(null);
        harness.Settle();

        Assert.False(harness.Find<Button>("RunNowButton").IsEffectivelyVisible);
        Assert.True(harness.Find<Button>("CancelOptimizeButton").IsEffectivelyVisible);
        Assert.True(harness.Find<ProgressBar>("OptimizeBar").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_remove_button_shows_the_confirmation_through_the_dialog_stack()
    {
        using var harness = await StartAsync();

        harness.Find<Button>("RemoveButton").Command!.Execute(null);

        Assert.IsType<ClearAllDataDialogViewModel>(Assert.Single(harness.FakeDialogs.Shown));
    }

    [AvaloniaFact]
    public async Task A_problem_from_a_move_shows_as_an_error_card_under_the_storage_section()
    {
        var blocked = new StorageRelocationPreview(SampleScenario.StorageRoot, @"E:\NewData", [], 0, 0, 0, ErrorInfo.From(new StorageDestinationNotEmptyException(@"E:\NewData")));
        using var harness = await StartAsync(new FakeBackend(ScenarioBuilder.Sample().WithRelocationPreview(blocked)));
        harness.FakeDialogs.AnswerFolder(@"E:\NewData");

        await Screen(harness).Storage.ChangeCommand.ExecuteAsync(null);
        harness.Settle();

        Assert.True(harness.Find<ContentControl>("StorageProblemHost").IsEffectivelyVisible);
        Assert.Contains("That folder isn't empty", harness.VisibleTexts());
    }

    [AvaloniaFact]
    public async Task Every_icon_only_button_is_named_and_every_choice_control_has_a_name()
    {
        using var harness = await StartAsync();

        Assert.Empty(AutomationAudit.IconButtonsWithoutNames(harness.View));
        Assert.Equal("Theme", AutomationProperties.GetName(harness.Find<ListBox>("ThemeSelector")));
        Assert.Equal("Layout", AutomationProperties.GetName(harness.Find<ListBox>("LayoutCards")));
        Assert.Equal("Accent color", AutomationProperties.GetName(harness.Find<ListBox>("AccentSwatches")));
    }

    [Fact]
    public void The_composition_registers_the_screen_for_the_settings_destination()
    {
        var backend = FakeBackend.Sample();
        var services = new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork);
        var context = new FeatureContext(
            services,
            new FakeDialogs(),
            HistoryRig.FormatterFor(backend),
            new ToastHostViewModel(new ManualTimeProvider(), new ImmediateUiDispatcher()),
            new ImmediateUiDispatcher());

        var factory = FeatureComposition.SettingsScreen(context);

        Assert.NotNull(factory);
        Assert.Equal(NavDestination.Settings, factory.Destination);
    }
}
