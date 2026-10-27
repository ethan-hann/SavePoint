using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Games;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

public class NewProfileWizardViewTests
{
    private static async Task<(GamesViewHarness Harness, NewProfileWizardViewModel Wizard)> OpenAsync()
    {
        var harness = await GamesViewHarness.StartAsync();
        harness.Rig.Screen.NewProfileCommand.Execute(null);
        harness.Settle();
        return (harness, Assert.IsType<NewProfileWizardViewModel>(harness.Dialogs.Current));
    }

    private static void Press(GamesViewHarness harness, Key key)
    {
        harness.Window.KeyPress(key, RawInputModifiers.None, key == Key.Escape ? PhysicalKey.Escape : PhysicalKey.Enter, null);
        harness.Window.KeyRelease(key, RawInputModifiers.None, key == Key.Escape ? PhysicalKey.Escape : PhysicalKey.Enter, null);
        harness.Settle();
    }

    private static JobKind Capture => JobKind.Capture;

    [AvaloniaFact]
    public async Task The_wizard_opens_with_the_name_field_focused_and_its_label_in_the_accent_color()
    {
        var (harness, _) = await OpenAsync();
        using var _ = harness;

        var field = harness.Find<TextBox>("NameField");
        Assert.Same(field, harness.Window.FocusManager!.GetFocusedElement());
        var label = harness.All<TextBlock>().First(block => block.Text == "Profile name");
        var accent = (Color)Application.Current!.FindResource("Md3PrimaryColor")!;
        Assert.Equal(accent, ((ISolidColorBrush)label.Foreground!).Color);
        Assert.Equal("Modded playthrough - Jul 8, 2026", field.Text);
    }

    [AvaloniaFact]
    public async Task The_check_steps_button_says_it_starts_the_save()
    {
        var (harness, wizard) = await OpenAsync();
        using var _ = harness;
        Assert.Equal("Continue", harness.Find<Button>("ContinueButton").Content);

        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Settle();

        Assert.Equal("Create profile", harness.Find<Button>("ContinueButton").Content);
    }

    [AvaloniaFact]
    public async Task Each_step_shows_its_own_content_and_buttons()
    {
        var (harness, wizard) = await OpenAsync();
        using var _ = harness;

        Assert.True(harness.Find<StackPanel>("NameStep").IsEffectivelyVisible);
        Assert.False(harness.Find<StackPanel>("CheckStep").IsEffectivelyVisible);
        Assert.False(harness.Find<StackPanel>("SaveStep").IsEffectivelyVisible);
        Assert.True(harness.Find<Button>("CancelButton").IsEffectivelyVisible);
        Assert.True(harness.Find<Button>("ContinueButton").IsEffectivelyVisible);
        Assert.False(harness.Find<Button>("BackButton").IsEffectivelyVisible);

        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Settle();

        Assert.False(harness.Find<StackPanel>("NameStep").IsEffectivelyVisible);
        Assert.True(harness.Find<StackPanel>("CheckStep").IsEffectivelyVisible);
        Assert.True(harness.Find<Button>("BackButton").IsEffectivelyVisible);
        Assert.False(harness.Find<Button>("CancelButton").IsEffectivelyVisible);
        Assert.Equal("Modded playthrough - Jul 8, 2026", harness.Find<TextBlock>("CheckName").Text);

        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Settle();

        Assert.True(harness.Find<StackPanel>("SaveStep").IsEffectivelyVisible);
        Assert.True(harness.Find<Button>("StopSaveButton").IsEffectivelyVisible);
        Assert.False(harness.Find<Button>("ContinueButton").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_preview_lists_the_folders_in_both_steps_that_show_it()
    {
        var (harness, wizard) = await OpenAsync();
        using var _ = harness;

        Assert.Contains("Game version and mods", harness.VisibleTexts());
        Assert.Contains("Current saves", harness.VisibleTexts());
        Assert.Contains(@"C:\Games\SkyrimSE" + " \u00b7 212 mod files \u00b7 41.2 GB", harness.VisibleTexts());
        Assert.Contains("Files SavePoint already has are reused, so only about 2.4 GB is added.", harness.VisibleTexts());
        Assert.Contains("WHAT GOES IN THE FIRST SNAPSHOT", harness.VisibleTexts());

        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Settle();

        Assert.Contains("Game version and mods", harness.VisibleTexts());
    }

    [AvaloniaFact]
    public async Task Enter_in_the_name_field_continues_and_escape_closes_the_wizard()
    {
        var (harness, wizard) = await OpenAsync();
        using var _ = harness;

        Press(harness, Key.Enter);

        Assert.Equal(WizardStep.Check, wizard.Step);

        Press(harness, Key.Escape);

        Assert.Null(harness.Dialogs.Current);
        Assert.False(await wizard.Result.Soon());
    }

    [AvaloniaFact]
    public async Task Focus_moves_with_the_step_so_keys_keep_reaching_the_dialog()
    {
        var (harness, wizard) = await OpenAsync();
        using var _ = harness;

        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Settle();
        Assert.Same(harness.Find<Button>("ContinueButton"), harness.Window.FocusManager!.GetFocusedElement());

        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Settle();
        Assert.Same(harness.Find<Button>("StopSaveButton"), harness.Window.FocusManager!.GetFocusedElement());

        harness.Rig.Backend.Jobs.Complete(harness.Rig.Backend.Jobs.LastStarted(Capture).JobId);
        harness.Settle();
        Assert.Same(harness.Find<Button>("DoneButton"), harness.Window.FocusManager!.GetFocusedElement());

        wizard.CloseCommand.Execute(null);
    }

    [AvaloniaFact]
    public async Task After_a_half_save_focus_lands_on_the_retry()
    {
        var (harness, wizard) = await OpenAsync();
        using var _ = harness;
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        var profileId = FakeAssert.Ok(await harness.Rig.Backend.Profiles.ListAsync(SampleScenario.SkyrimId)).Single(profile => profile.Name == wizard.TrimmedName).Id;

        harness.Rig.Backend.Jobs.Complete(
            harness.Rig.Backend.Jobs.LastStarted(Capture).JobId,
            new CaptureResult(profileId, new HistoryPointId("pt"), [new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.External, SampleScenario.SavesOutOfRoomError)]));
        harness.Settle();

        var focused = Assert.IsType<Button>(harness.Window.FocusManager!.GetFocusedElement());
        Assert.Equal("Retry the saves", focused.Content);
    }

    [AvaloniaFact]
    public async Task The_step_dots_are_named_and_the_finished_ones_show_a_check()
    {
        var (harness, wizard) = await OpenAsync();
        using var _ = harness;
        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Settle();

        var dots = harness.All<WizardStepView>().ToList();

        Assert.Equal(3, dots.Count);
        Assert.Equal(
            ["Step 1 of 3: Name it, done", "Step 2 of 3: Check, current step", "Step 3 of 3: Create"],
            dots.Select(dot => AutomationProperties.GetName(dot.GetVisualDescendants().OfType<StackPanel>().First())));
        Assert.True(dots[0].GetVisualDescendants().OfType<TextBlock>().First(block => block.Name == "DoneIcon").IsEffectivelyVisible);
        Assert.False(dots[1].GetVisualDescendants().OfType<TextBlock>().First(block => block.Name == "DoneIcon").IsEffectivelyVisible);
        Assert.All(dots, dot => Assert.Equal(24, dot.GetVisualDescendants().OfType<Border>().First(border => border.Name == "Dot").Bounds.Width));
    }

    [AvaloniaFact]
    public async Task The_save_shows_a_bar_that_turns_determinate_and_speaks_its_milestones()
    {
        var (harness, wizard) = await OpenAsync();
        using var _ = harness;
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Settle();
        var bar = harness.Find<ProgressBar>("SaveProgress");

        Assert.True(bar.IsIndeterminate);
        Assert.Contains("A large install can take a while the first time.", harness.VisibleTexts());

        harness.Rig.Backend.Jobs.ReportProgress(harness.Rig.Backend.Jobs.LastStarted(Capture).JobId, new JobProgress(1000, 4000, CurrentPath: @"Data\x.esm"));
        harness.Settle();

        Assert.False(bar.IsIndeterminate);
        Assert.Equal(25, bar.Value, 1);
        Assert.Contains("1,000 of 4,000 files copied", harness.VisibleTexts());
        Assert.Contains(@"Latest file finished: Data\x.esm", harness.VisibleTexts());
        Assert.Equal("1,000 of 4,000 files copied", AutomationProperties.GetName(bar));
        Assert.Equal("25% copied", harness.All<NewProfileWizardView>().Single().GetVisualDescendants().OfType<AnnouncementRegion>().Single().Message);
    }

    [AvaloniaFact]
    public async Task The_bar_fills_by_bytes_while_one_large_file_is_stored()
    {
        var (harness, wizard) = await OpenAsync();
        using var _ = harness;
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Settle();
        var bar = harness.Find<ProgressBar>("SaveProgress");

        harness.Rig.Backend.Jobs.ReportProgress(harness.Rig.Backend.Jobs.LastStarted(Capture).JobId, new JobProgress(0, 23, 35_000_000_000, 70_000_000_000));
        harness.Settle();

        Assert.False(bar.IsIndeterminate);
        Assert.Equal(50, bar.Value, 1);
        Assert.Contains("35 GB of 70 GB copied", harness.VisibleTexts());
        Assert.Contains("0 of 23 files finished", harness.VisibleTexts());
        Assert.Equal("35 GB of 70 GB copied", AutomationProperties.GetName(bar));
    }

    [AvaloniaFact]
    public async Task Keep_working_sits_next_to_cancel_and_closes_the_dialog_without_stopping_the_snapshot()
    {
        var (harness, wizard) = await OpenAsync();
        using var _ = harness;
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Settle();

        var keep = harness.Find<Button>("KeepWorkingButton");

        Assert.True(keep.IsEffectivelyVisible);
        Assert.True(keep.Focusable);
        Assert.Equal("Keep working", keep.Content);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(keep)));
        Assert.True(harness.Find<Button>("StopSaveButton").IsEffectivelyVisible);

        keep.Command!.Execute(null);
        harness.Settle();

        Assert.Null(harness.Dialogs.Current);
        Assert.Equal(JobStatus.Running, harness.Rig.Backend.Jobs.LastStarted(Capture).Status);
        Assert.False(harness.Rig.Backend.Jobs.LastStarted(Capture).CancelRequested);
    }

    [AvaloniaFact]
    public async Task A_complete_save_shows_the_success_line_and_done_closes_the_wizard()
    {
        var (harness, wizard) = await OpenAsync();
        using var _ = harness;
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Rig.Backend.Jobs.Complete(harness.Rig.Backend.Jobs.LastStarted(Capture).JobId);
        harness.Settle();

        Assert.True(harness.Find<StackPanel>("SavedBlock").IsEffectivelyVisible);
        Assert.Equal("Profile created.", harness.Find<TextBlock>("SavedText").Text);
        var done = harness.Find<Button>("DoneButton");
        Assert.True(done.IsEffectivelyVisible);

        done.Command!.Execute(null);
        harness.Settle();

        Assert.Null(harness.Dialogs.Current);
    }

    [AvaloniaFact]
    public async Task A_half_saved_capture_shows_the_card_its_status_list_and_the_retry()
    {
        var (harness, wizard) = await OpenAsync();
        using var _ = harness;
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        var profileId = FakeAssert.Ok(await harness.Rig.Backend.Profiles.ListAsync(SampleScenario.SkyrimId)).Single(profile => profile.Name == wizard.TrimmedName).Id;
        harness.Rig.Backend.Jobs.Complete(
            harness.Rig.Backend.Jobs.LastStarted(Capture).JobId,
            new CaptureResult(profileId, new HistoryPointId("pt"), [new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.External, SampleScenario.SavesOutOfRoomError)]));
        harness.Settle();

        var texts = harness.VisibleTexts().ToList();

        Assert.True(harness.Find<ContentControl>("ResultCardHost").IsEffectivelyVisible);
        Assert.Contains("Only part of the snapshot was taken", texts);
        Assert.Contains("Retry the saves", texts);
        Assert.Contains("Game version and mods \u00b7 in the snapshot", texts);
        Assert.Contains("Saves \u00b7 not in the snapshot yet", texts);
        Assert.True(harness.Find<Button>("CloseButton").IsEffectivelyVisible);
        Assert.False(harness.Find<Button>("DoneButton").IsEffectivelyVisible);
        Assert.False(harness.Find<StackPanel>("SavingBlock").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task A_refused_create_shows_the_reason_on_the_check_step()
    {
        var (harness, wizard) = await OpenAsync();
        using var _ = harness;
        harness.Rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.CreateAsync), SampleScenario.SavesOutOfRoomError);
        await wizard.ContinueCommand.ExecuteAsync(null);

        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Settle();

        Assert.True(harness.Find<Border>("SubmitErrorBox").IsEffectivelyVisible);
        Assert.Contains("Not enough free disk space to take this snapshot. Free up about 1.2 GB on drive D: and try again.", harness.VisibleTexts());
        Assert.True(harness.Find<StackPanel>("CheckStep").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_edit_and_duplicate_dialogs_open_on_their_name_field_with_the_reason_ready()
    {
        using var harness = await GamesViewHarness.StartAsync();

        harness.Rig.Screen.Profiles[1].EditCommand.Execute(null);
        harness.Settle();

        Assert.Same(harness.Find<TextBox>("NameField"), harness.Window.FocusManager!.GetFocusedElement());
        Assert.Contains("Leave blank to use the detected version, 1.6.1170.", harness.VisibleTexts());
        Assert.Equal("1.6.1170", harness.Find<TextBox>("VersionField").PlaceholderText);
        harness.Dialogs.Current!.RequestDismiss();
        harness.Settle();

        harness.Rig.Screen.Profiles[0].DuplicateCommand.Execute(null);
        harness.Settle();

        Assert.Same(harness.Find<TextBox>("NameField"), harness.Window.FocusManager!.GetFocusedElement());
        Assert.Equal("Modded playthrough - 7-2-26 copy", harness.Find<TextBox>("NameField").Text);
        Assert.Contains("The copy starts with the same game version and mods, and no save snapshots of its own.", harness.VisibleTexts());
    }
}
