using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Launch;

// The launch views in the real window: keyboard, defaults, live regions and what each view model state looks like on screen.
public class LaunchViewTests
{
    private static async Task PlayToExitedAsync(LaunchViewHarness harness, LaunchTarget? target = null)
    {
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, target ?? LaunchRig.ModdedTip);
        harness.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);
        harness.Backend.Launch.ReportExited(SampleScenario.SkyrimId, SampleScenario.PlayDuration, SampleScenario.NewSaveFiles);
        harness.Settle();
    }

    private static PostPlayDialogViewModel PostPlay(LaunchViewHarness harness) => harness.Dialogs.Stack.OfType<PostPlayDialogViewModel>().Single();

    private static void Press(LaunchViewHarness harness, PhysicalKey key)
    {
        harness.Window.KeyPressQwerty(key, RawInputModifiers.None);
        harness.Settle();
    }

    [AvaloniaFact]
    public async Task Post_play_cards_move_the_selection_with_the_arrow_keys()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await PlayToExitedAsync(harness);
        var dialog = PostPlay(harness);
        var items = harness.Find<ListBox>("ChoiceList").GetVisualDescendants().OfType<ListBoxItem>().ToList();
        items[0].Focus(NavigationMethod.Tab);
        harness.Settle();

        Press(harness, PhysicalKey.ArrowDown);
        Assert.Equal(CompletionChoice.UpdateExisting, dialog.SelectedChoice.Kind);

        Press(harness, PhysicalKey.ArrowDown);
        Assert.Equal(CompletionChoice.Skip, dialog.SelectedChoice.Kind);

        Press(harness, PhysicalKey.ArrowUp);
        Assert.Equal(CompletionChoice.UpdateExisting, dialog.SelectedChoice.Kind);
    }

    [AvaloniaFact]
    public async Task Post_play_cards_are_a_labelled_group_whose_items_carry_their_text()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await PlayToExitedAsync(harness);

        var list = harness.Find<ListBox>("ChoiceList");

        Assert.Equal("What to do with this session", AutomationProperties.GetName(list));
        var first = list.GetVisualDescendants().OfType<ListBoxItem>().First();
        Assert.Equal("Keep this session as a new snapshot", AutomationProperties.GetName(first));
        Assert.Equal("Recommended. Your earlier snapshots stay as they are.", AutomationProperties.GetHelpText(first));
    }

    [AvaloniaFact]
    public async Task Only_the_selected_post_play_card_shows_its_name_field()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await PlayToExitedAsync(harness);
        var dialog = PostPlay(harness);
        var field = harness.All<TextBox>().Single(box => box.Text == "Session - 7-8-26");
        Assert.True(field.IsEffectivelyVisible);

        dialog.SelectedChoice = dialog.Choices.Single(choice => choice.Kind == CompletionChoice.Skip);
        harness.Settle();

        Assert.False(field.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Enter_in_the_post_play_dialog_saves_with_the_selected_choice()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await PlayToExitedAsync(harness);
        var dialog = PostPlay(harness);

        Assert.True(harness.Find<Button>("SaveButton").IsDefault);
        Press(harness, PhysicalKey.Enter);

        Assert.True(dialog.IsClosed);
        Assert.Equal(new SessionCompletion.CaptureNew("Session - 7-8-26"), harness.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.CompleteSessionAsync)).Single().Args[1]);
    }

    [AvaloniaFact]
    public async Task Escape_in_the_post_play_dialog_leaves_the_saves_alone()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await PlayToExitedAsync(harness);

        Press(harness, PhysicalKey.Escape);

        Assert.Equal(new SessionCompletion.Skip(), harness.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.CompleteSessionAsync)).Single().Args[1]);
        Assert.Empty(harness.Dialogs.Stack);
    }

    [AvaloniaFact]
    public async Task Switch_and_play_stays_off_until_the_unsaved_changes_are_resolved()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        var begin = harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.SurvivalTip);
        harness.Settle();
        var dialog = harness.Dialogs.Stack.OfType<LaunchDialogViewModel>().Single();
        var button = harness.Find<Button>("SwitchButton");
        Assert.False(button.IsEffectivelyEnabled);
        Assert.True(button.IsDefault);

        var discard = dialog.UnsavedBlocks[0].DiscardCommand.ExecuteAsync(null);
        harness.Settle();
        Assert.IsType<ConfirmDialogViewModel>(harness.Dialogs.Current).ConfirmCommand.Execute(null);
        await discard;
        harness.Settle();

        Assert.True(harness.Find<Button>("SwitchButton").IsEffectivelyEnabled);
        harness.Dialogs.RequestDismissCurrent();
        await begin;
    }

    [AvaloniaFact]
    public async Task The_launch_dialog_shows_a_block_per_dirty_folder_and_the_picker_for_several_saves()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        var begin = harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.SurvivalTip);
        harness.Settle();

        Assert.True(harness.Find<ComboBox>("SavePicker").IsEffectivelyVisible);
        Assert.False(harness.Find<ContentControl>("SingleSave").IsEffectivelyVisible);
        Assert.Equal(2, harness.Find<ComboBox>("SavePicker").ItemCount);
        Assert.Single(harness.Find<ItemsControl>("UnsavedList").GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("error"));
        Assert.False(harness.Find<Border>("KeepCallout").IsEffectivelyVisible);
        Assert.True(harness.Find<Border>("VersionSection").IsEffectivelyVisible);

        harness.Dialogs.RequestDismissCurrent();
        await begin;
    }

    [AvaloniaFact]
    public async Task Escape_on_the_launch_dialog_cancels_it_and_starts_nothing()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        var begin = harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.SurvivalTip);
        harness.Settle();

        Press(harness, PhysicalKey.Escape);
        await begin;

        Assert.Empty(harness.Dialogs.Stack);
        Assert.Empty(harness.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.StartAsync)));
        Assert.False(harness.Flow.IsActive(SampleScenario.SkyrimId));
    }

    [AvaloniaFact]
    public async Task The_banner_is_a_polite_live_region_and_details_shows_what_was_checked()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);
        harness.Settle();
        var banner = harness.Find<Border>("Banner");

        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(banner));
        Assert.Equal("Everything was already in place, so Skyrim Special Edition is starting now", AutomationProperties.GetName(banner));

        var details = harness.Find<Button>("DetailsButton");
        details.Command!.Execute(null);
        harness.Settle();

        Assert.Single(harness.Dialogs.Stack.OfType<LaunchChecksDialogViewModel>());
        Assert.Equal(3, harness.Find<ItemsControl>("Lines").ItemCount);
        harness.Find<Button>("CloseButton").Command!.Execute(null);
        Assert.Empty(harness.Dialogs.Stack);
    }

    [AvaloniaFact]
    public async Task The_progress_dialog_speaks_its_progress_and_ignores_escape()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        FakeAssert.Ok(await harness.Backend.Settings.SaveAsync(SampleScenario.Settings with { SkipConfirmWhenOnlySaveChanges = true }));
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ClearedBlackreachPoint);
        harness.Settle();
        var announcer = harness.Find<AnnouncementRegion>("ProgressAnnouncer");
        Assert.Equal("Getting Skyrim Special Edition ready.", announcer.Message);

        harness.Backend.Launch.ReportSwitching(SampleScenario.SkyrimId, SampleScenario.SwitchProgress);
        harness.Settle();
        Assert.Equal("50% done.", announcer.Message);
        Assert.False(harness.Find<ProgressBar>("Bar").IsIndeterminate);
        Assert.Equal(62.3, harness.Find<ProgressBar>("Bar").Value, 1);

        Press(harness, PhysicalKey.Escape);
        Assert.Single(harness.Dialogs.Stack);
    }

    [AvaloniaFact]
    public async Task The_progress_dialogs_cancel_button_asks_the_service_to_cancel()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        FakeAssert.Ok(await harness.Backend.Settings.SaveAsync(SampleScenario.Settings with { SkipConfirmWhenOnlySaveChanges = true }));
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ClearedBlackreachPoint);
        harness.Settle();

        harness.Find<Button>("CancelButton").Command!.Execute(null);
        harness.Settle();

        Assert.Single(harness.Backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
        Assert.True(harness.Find<TextBlock>("CancelingText").IsEffectivelyVisible);
        Assert.False(harness.Find<Button>("CancelButton").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task The_running_card_is_on_screen_with_a_working_done_playing_button()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);
        harness.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);
        harness.Settle();

        Assert.Equal("Skyrim Special Edition is running", harness.Find<TextBlock>("TitleText").Text);
        Assert.Equal("Modded playthrough - 7-2-26 · started 41 minutes ago", harness.Find<TextBlock>("SubtitleText").Text);
        harness.Find<Button>("DonePlayingButton").Command!.Execute(null);

        Assert.Single(harness.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.MarkFinishedAsync)));
    }

    [AvaloniaFact]
    public async Task The_half_saved_card_lists_both_folders_with_a_retry()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await PlayToExitedAsync(harness);
        await PostPlay(harness).SaveCommand.ExecuteAsync(null);
        harness.Backend.Jobs.Complete(harness.Backend.Jobs.LastStarted(JobKind.Capture).JobId, SampleScenario.HalfSaved);
        harness.Settle();

        var rows = harness.Find<ItemsControl>("Rows");

        Assert.Equal(2, rows.ItemCount);
        Assert.Contains(harness.All<TextBlock>(), block => block.Text == "Saves · not in the snapshot yet");
        Assert.Contains(harness.All<Button>(), button => Equals(button.Content, "Retry the saves"));
    }

    [AvaloniaFact]
    public async Task A_clean_capture_after_play_ends_with_the_shells_saved_toast_and_no_card()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await PlayToExitedAsync(harness);
        await PostPlay(harness).SaveCommand.ExecuteAsync(null);
        Assert.IsType<LaunchSavingViewModel>(harness.Flow.InlineContentFor(SampleScenario.SkyrimId));

        harness.Backend.Jobs.Complete(harness.Backend.Jobs.LastStarted(JobKind.Capture).JobId);
        harness.Settle();

        Assert.Null(harness.Flow.InlineContentFor(SampleScenario.SkyrimId));
        var toast = Assert.Single(harness.Rig.Toasts.Toasts);
        Assert.Equal("Took a snapshot of Skyrim Special Edition.", toast.Text);
        Assert.True(toast.IsSuccess);
    }

    [AvaloniaFact]
    public async Task The_failure_card_over_the_games_table_has_its_actions_wired()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);
        harness.Backend.Launch.FailLaunch(SampleScenario.SkyrimId, SampleScenario.FileInUseError);
        harness.Settle();
        var card = (ErrorCardViewModel)harness.Flow.InlineContentFor(SampleScenario.SkyrimId)!;

        var tryAgain = harness.All<Button>().Single(button => Equals(button.Content, "Try again"));
        tryAgain.Command!.Execute(null);
        harness.Settle();

        Assert.Equal(2, harness.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.StartAsync)).Count);
        Assert.NotSame(card, harness.Flow.InlineContentFor(SampleScenario.SkyrimId));
    }

    [AvaloniaFact]
    public async Task No_launch_view_has_an_unnamed_icon_button()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        var begin = harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.SurvivalTip);
        harness.Settle();
        Assert.Empty(AutomationAudit.IconButtonsWithoutNames(harness.View));
        harness.Dialogs.RequestDismissCurrent();
        await begin;

        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);
        harness.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);
        harness.Backend.Launch.ReportExited(SampleScenario.SkyrimId, SampleScenario.PlayDuration, 3);
        harness.Settle();
        Assert.Empty(AutomationAudit.IconButtonsWithoutNames(harness.View));
    }

    [AvaloniaFact]
    public async Task The_views_for_every_launch_view_model_are_found_by_name()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        var types = new[]
        {
            typeof(LaunchDialogViewModel),
            typeof(KeepAsNewProfileDialogViewModel),
            typeof(SwitchProgressDialogViewModel),
            typeof(PostPlayDialogViewModel),
            typeof(LaunchChecksDialogViewModel),
            typeof(LaunchBannerViewModel),
            typeof(RunningCardViewModel),
            typeof(LaunchSavingViewModel),
            typeof(FolderOutcomeListViewModel),
        };

        foreach (var type in types)
        {
            Assert.NotNull(Savepoint.App.Features.Common.ViewLocator.ViewTypeFor(type));
        }

        await Task.CompletedTask;
    }
}
