using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Launch;

// Writes PNGs to render-output/launch/ (gitignored) at 1040x660 to compare with the previous render of the launch flow after a change.
// Nothing here asserts on a picture, so the renders are only drawn on request: SAVEPOINT_RENDER=1.
public class LaunchRenderTests
{
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
    public async Task Writes_every_launch_render_on_request()
    {
        RenderOutput.SkipUnlessRequested();

        await TheBannerOverTheGamesTableDark();
        await TheLaunchDialogDark();
        await TheLaunchDialogLight();
        await TheLaunchDialogWithEverySectionDark();
        await TheKeepPromptDark();
        await TheProgressDialogDark();
        await TheProgressDialogLightAndIndeterminate();
        await TheRunningCardDark();
        await TheRunningCardLight();
        await ThePostPlayDialogDark();
        await ThePostPlayDialogLight();
        await ThePostPlayDialogForAnOlderPointDark();
        await TheSavingCardDark();
        await TheHalfSavedCardDark();
        await TheFileInUseCardDark();
        await TheFolderGoneCardDark();
        await TheChecksDialogDark();
    }

    private static async Task TheBannerOverTheGamesTableDark()
    {
        using var harness = await LaunchViewHarness.StartAsync();

        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);

        harness.Render("launch-nothing-to-change-dark");
    }

    private static async Task TheLaunchDialogDark()
    {
        using var harness = await LaunchViewHarness.StartAsync();

        var begin = harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.SurvivalTip);

        harness.Render("launch-dialog-dark");
        await CloseAsync(harness, begin);
    }

    private static async Task TheLaunchDialogLight()
    {
        using var harness = await LaunchViewHarness.StartAsync(Backend(ThemePreference.Light), ThemePreference.Light);

        var begin = harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.SurvivalTip);

        harness.Render("launch-dialog-light");
        await CloseAsync(harness, begin);
    }

    private static async Task TheLaunchDialogWithEverySectionDark()
    {
        var backend = await WithPreviewAsync(
            ThemePreference.Dark,
            preview => (preview with { TargetIsOlderPoint = true })
                .WithFolder(RepoRole.Install, install => install with { Dirty = true, DirtyFileCount = 12, DirtyOwnerProfileId = SampleScenario.SurvivalId })
                .WithFolder(RepoRole.External, saves => saves with { DirtyOwnerProfileId = null }),
            LaunchRig.SurvivalTip);
        using var harness = await LaunchViewHarness.StartAsync(backend, ThemePreference.Dark, height: 1060);

        var begin = harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.SurvivalTip);

        harness.Render("launch-dialog-all-sections-dark");
        await CloseAsync(harness, begin);
    }

    private static async Task TheKeepPromptDark()
    {
        var backend = await WithPreviewAsync(ThemePreference.Dark, preview => preview.WithFolder(RepoRole.External, saves => saves with { DirtyOwnerProfileId = null }), LaunchRig.SurvivalTip);
        using var harness = await LaunchViewHarness.StartAsync(backend);
        var begin = harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.SurvivalTip);
        var launchDialog = harness.Dialogs.Stack.OfType<LaunchDialogViewModel>().Single();

        var keep = launchDialog.KeepCommand.ExecuteAsync(null);

        harness.Render("launch-keep-prompt-dark");
        harness.Dialogs.RequestDismissCurrent();
        await keep;
        await CloseAsync(harness, begin);
    }

    private static async Task TheProgressDialogDark()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        FakeAssert.Ok(await harness.Backend.Settings.SaveAsync(SampleScenario.Settings with { SkipConfirmWhenOnlySaveChanges = true }));
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ClearedBlackreachPoint);

        harness.Backend.Launch.ReportSwitching(SampleScenario.SkyrimId, SampleScenario.SwitchProgress);

        harness.Render("progress-dark");
    }

    private static async Task TheProgressDialogLightAndIndeterminate()
    {
        using var harness = await LaunchViewHarness.StartAsync(Backend(ThemePreference.Light), ThemePreference.Light);
        FakeAssert.Ok(await harness.Backend.Settings.SaveAsync(SampleScenario.Settings with { Theme = ThemePreference.Light, SkipConfirmWhenOnlySaveChanges = true }));
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ClearedBlackreachPoint);

        harness.Render("progress-indeterminate-light");
    }

    private static async Task TheRunningCardDark()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);

        harness.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);

        harness.Render("running-dark");
    }

    private static async Task TheRunningCardLight()
    {
        using var harness = await LaunchViewHarness.StartAsync(Backend(ThemePreference.Light), ThemePreference.Light);
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);

        harness.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);

        harness.Render("running-light");
    }

    private static async Task ThePostPlayDialogDark()
    {
        using var harness = await LaunchViewHarness.StartAsync();

        await PlayToExitedAsync(harness, LaunchRig.ModdedTip);

        harness.Render("post-play-dark");
    }

    private static async Task ThePostPlayDialogLight()
    {
        using var harness = await LaunchViewHarness.StartAsync(Backend(ThemePreference.Light), ThemePreference.Light);

        await PlayToExitedAsync(harness, LaunchRig.ModdedTip);

        harness.Render("post-play-light");
    }

    private static async Task ThePostPlayDialogForAnOlderPointDark()
    {
        using var harness = await LaunchViewHarness.StartAsync();

        await PlayToExitedAsync(harness, LaunchRig.ClearedBlackreachPoint);

        harness.Render("post-play-older-point-dark");
    }

    private static async Task TheSavingCardDark()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await PlayToExitedAsync(harness, LaunchRig.ModdedTip);
        var dialog = harness.Dialogs.Stack.OfType<PostPlayDialogViewModel>().Single();

        await dialog.SaveCommand.ExecuteAsync(null);
        harness.Backend.Jobs.ReportProgress(harness.Backend.Jobs.LastStarted(JobKind.Capture).JobId, new JobProgress(FilesDone: 5, FilesTotal: 12));

        harness.Render("launch-saving-card-dark");
    }

    private static async Task TheHalfSavedCardDark()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await PlayToExitedAsync(harness, LaunchRig.ModdedTip);
        await harness.Dialogs.Stack.OfType<PostPlayDialogViewModel>().Single().SaveCommand.ExecuteAsync(null);

        harness.Backend.Jobs.Complete(harness.Backend.Jobs.LastStarted(JobKind.Capture).JobId, SampleScenario.HalfSaved);

        harness.Render("half-saved-card-dark");
    }

    private static async Task TheFileInUseCardDark()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        FakeAssert.Ok(await harness.Backend.Settings.SaveAsync(SampleScenario.Settings with { SkipConfirmWhenOnlySaveChanges = true }));
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ClearedBlackreachPoint);

        harness.Backend.Launch.FailLaunch(SampleScenario.SkyrimId, SampleScenario.FileInUseError);

        harness.Render("file-in-use-card-dark");
    }

    private static async Task TheFolderGoneCardDark()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);

        harness.Backend.Launch.FailLaunch(SampleScenario.SkyrimId, SampleScenario.FolderGoneError);

        harness.Render("folder-gone-card-dark");
    }

    private static async Task TheChecksDialogDark()
    {
        using var harness = await LaunchViewHarness.StartAsync();
        await harness.Flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);
        var banner = (LaunchBannerViewModel)harness.Flow.InlineContentFor(SampleScenario.SkyrimId)!;

        var details = banner.ShowDetailsCommand.ExecuteAsync(null);

        harness.Render("launch-checks-dialog-dark");
        harness.Dialogs.RequestDismissCurrent();
        await details;
    }
}
