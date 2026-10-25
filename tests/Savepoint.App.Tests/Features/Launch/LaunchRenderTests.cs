using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Launch;

// Writes PNGs to render-output/launch/ (gitignored) for side-by-side review against the launch flow designs at 1040x660.
public class LaunchRenderTests
{
    private static void AssertRendered(string path) =>
        Assert.True(new FileInfo(path).Length > 10_000, "The render looks empty.");

    private static FakeBackend Backend(ThemePreference theme, Func<ScenarioBuilder, ScenarioBuilder>? change = null)
    {
        var scenario = ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Theme = theme });
        return new FakeBackend(change?.Invoke(scenario) ?? scenario);
    }

    private static async Task<FakeBackend> WithPreviewAsync(ThemePreference theme, Func<LaunchPreview, LaunchPreview> change, LaunchTarget target)
    {
        var source = FakeAssert.Ok(await FakeBackend.Sample().Launch.GetPreviewAsync(SampleScenario.SkyrimId, target));
        return Backend(theme, scenario => scenario.WithLaunchPreview(change(source)));
    }

    private static async Task PlayToExitedAsync(LaunchViewHarness harness, LaunchTarget target)
    {
        FakeAssert.Ok(await harness.Backend.Settings.SaveAsync(SampleScenario.Settings with { Theme = SampleScenario.Settings.Theme, SkipConfirmWhenOnlySaveChanges = true }));
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, target);
        harness.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);
        harness.Backend.Launch.ReportExited(SampleScenario.SkyrimId, SampleScenario.PlayDuration, SampleScenario.NewSaveFiles);
    }

    private static async Task CloseAsync(LaunchViewHarness harness, Task begin)
    {
        harness.Dialogs.RequestDismissCurrent();
        await begin;
    }

    [AvaloniaFact]
    public async Task The_banner_over_the_games_table_dark_renders_to_a_png()
    {
        using var harness = await LaunchViewHarness.StartAsync();

        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);

        AssertRendered(harness.Render("launch-nothing-to-change-dark"));
    }

    [AvaloniaFact]
    public async Task The_launch_dialog_dark_renders_to_a_png()
    {
        using var harness = await LaunchViewHarness.StartAsync();

        var begin = harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.SurvivalTip);

        AssertRendered(harness.Render("launch-dialog-dark"));
        await CloseAsync(harness, begin);
    }

    [AvaloniaFact]
    public async Task The_launch_dialog_light_renders_to_a_png()
    {
        using var harness = await LaunchViewHarness.StartAsync(Backend(ThemePreference.Light), ThemePreference.Light);

        var begin = harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.SurvivalTip);

        AssertRendered(harness.Render("launch-dialog-light"));
        await CloseAsync(harness, begin);
    }

    [AvaloniaFact]
    public async Task The_launch_dialog_with_every_section_dark_renders_to_a_png()
    {
        var backend = await WithPreviewAsync(
            ThemePreference.Dark,
            preview => preview with
            {
                Install = preview.Install with { Dirty = true, DirtyFileCount = 12, DirtyOwnerProfileId = SampleScenario.SurvivalId },
                External = preview.External! with { DirtyOwnerProfileId = null },
                TargetIsOlderPoint = true,
            },
            LaunchRig.SurvivalTip);
        using var harness = await LaunchViewHarness.StartAsync(backend, ThemePreference.Dark, height: 1060);

        var begin = harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.SurvivalTip);

        AssertRendered(harness.Render("launch-dialog-all-sections-dark"));
        await CloseAsync(harness, begin);
    }

    [AvaloniaFact]
    public async Task The_keep_prompt_dark_renders_to_a_png()
    {
        var backend = await WithPreviewAsync(ThemePreference.Dark, preview => preview with { External = preview.External! with { DirtyOwnerProfileId = null } }, LaunchRig.SurvivalTip);
        using var harness = await LaunchViewHarness.StartAsync(backend);
        var begin = harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.SurvivalTip);
        var launchDialog = harness.Dialogs.Stack.OfType<LaunchDialogViewModel>().Single();

        var keep = launchDialog.KeepCommand.ExecuteAsync(null);

        AssertRendered(harness.Render("launch-keep-prompt-dark"));
        harness.Dialogs.RequestDismissCurrent();
        await keep;
        await CloseAsync(harness, begin);
    }

    [AvaloniaFact]
    public async Task The_progress_dialog_dark_renders_to_a_png()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        FakeAssert.Ok(await harness.Backend.Settings.SaveAsync(SampleScenario.Settings with { SkipConfirmWhenOnlySaveChanges = true }));
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ClearedBlackreachPoint);

        harness.Backend.Launch.ReportSwitching(SampleScenario.SkyrimId, SampleScenario.SwitchProgress);

        AssertRendered(harness.Render("progress-dark"));
    }

    [AvaloniaFact]
    public async Task The_progress_dialog_light_and_indeterminate_render_to_a_png()
    {
        using var harness = await LaunchViewHarness.StartAsync(Backend(ThemePreference.Light), ThemePreference.Light);
        FakeAssert.Ok(await harness.Backend.Settings.SaveAsync(SampleScenario.Settings with { Theme = ThemePreference.Light, SkipConfirmWhenOnlySaveChanges = true }));
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ClearedBlackreachPoint);

        AssertRendered(harness.Render("progress-indeterminate-light"));
    }

    [AvaloniaFact]
    public async Task The_running_card_dark_renders_to_a_png()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);

        harness.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);

        AssertRendered(harness.Render("running-dark"));
    }

    [AvaloniaFact]
    public async Task The_running_card_light_renders_to_a_png()
    {
        using var harness = await LaunchViewHarness.StartAsync(Backend(ThemePreference.Light), ThemePreference.Light);
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);

        harness.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);

        AssertRendered(harness.Render("running-light"));
    }

    [AvaloniaFact]
    public async Task The_post_play_dialog_dark_renders_to_a_png()
    {
        using var harness = await LaunchViewHarness.StartAsync();

        await PlayToExitedAsync(harness, LaunchRig.ModdedTip);

        AssertRendered(harness.Render("post-play-dark"));
    }

    [AvaloniaFact]
    public async Task The_post_play_dialog_light_renders_to_a_png()
    {
        using var harness = await LaunchViewHarness.StartAsync(Backend(ThemePreference.Light), ThemePreference.Light);

        await PlayToExitedAsync(harness, LaunchRig.ModdedTip);

        AssertRendered(harness.Render("post-play-light"));
    }

    [AvaloniaFact]
    public async Task The_post_play_dialog_for_an_older_point_dark_renders_to_a_png()
    {
        using var harness = await LaunchViewHarness.StartAsync();

        await PlayToExitedAsync(harness, LaunchRig.ClearedBlackreachPoint);

        AssertRendered(harness.Render("post-play-older-point-dark"));
    }

    [AvaloniaFact]
    public async Task The_saving_card_dark_renders_to_a_png()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await PlayToExitedAsync(harness, LaunchRig.ModdedTip);
        var dialog = harness.Dialogs.Stack.OfType<PostPlayDialogViewModel>().Single();

        await dialog.SaveCommand.ExecuteAsync(null);
        harness.Backend.Jobs.ReportProgress(harness.Backend.Jobs.LastStarted(JobKind.Capture).JobId, new JobProgress(FilesDone: 5, FilesTotal: 12));

        AssertRendered(harness.Render("launch-saving-card-dark"));
    }

    [AvaloniaFact]
    public async Task The_half_saved_card_dark_renders_to_a_png()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await PlayToExitedAsync(harness, LaunchRig.ModdedTip);
        await harness.Dialogs.Stack.OfType<PostPlayDialogViewModel>().Single().SaveCommand.ExecuteAsync(null);

        harness.Backend.Jobs.Complete(harness.Backend.Jobs.LastStarted(JobKind.Capture).JobId, SampleScenario.HalfSaved);

        AssertRendered(harness.Render("half-saved-card-dark"));
    }

    [AvaloniaFact]
    public async Task The_file_in_use_card_dark_renders_to_a_png()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        FakeAssert.Ok(await harness.Backend.Settings.SaveAsync(SampleScenario.Settings with { SkipConfirmWhenOnlySaveChanges = true }));
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ClearedBlackreachPoint);

        harness.Backend.Launch.FailLaunch(SampleScenario.SkyrimId, SampleScenario.FileInUseError);

        AssertRendered(harness.Render("file-in-use-card-dark"));
    }

    [AvaloniaFact]
    public async Task The_folder_gone_card_dark_renders_to_a_png()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);

        harness.Backend.Launch.FailLaunch(SampleScenario.SkyrimId, SampleScenario.FolderGoneError);

        AssertRendered(harness.Render("folder-gone-card-dark"));
    }

    [AvaloniaFact]
    public async Task The_checks_dialog_dark_renders_to_a_png()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);
        var banner = (LaunchBannerViewModel)harness.Flow.InlineContentFor(SampleScenario.SkyrimId)!;

        var details = banner.ShowDetailsCommand.ExecuteAsync(null);

        AssertRendered(harness.Render("launch-checks-dialog-dark"));
        harness.Dialogs.RequestDismissCurrent();
        await details;
    }
}
