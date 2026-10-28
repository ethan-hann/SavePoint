using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Games;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

// Writes PNGs to render-output/games/ (gitignored) at 1040x660 to compare with the previous render of the Games screen and wizard after a change.
// Nothing here asserts on a picture, so the renders are only drawn on request: SAVEPOINT_RENDER=1.
public class GamesRenderTests
{
    [AvaloniaFact]
    public async Task Writes_every_Games_render_on_request()
    {
        RenderOutput.SkipUnlessRequested();

        await SidebarLayoutDark();
        await SidebarLayoutLight();
        await LibraryLayoutDark();
        await NarrowWindowDark();
        await AGameWithoutProfilesDark();
        await TheRowMenuDark();
        await WizardStepOneDark();
        await WizardStepOneLight();
        await WizardStepTwoDark();
        await WizardStepThreeSavingDark();
        await WizardStepThreeSavedDark();
        await WizardHalfSavedDark();
        await TheDeletePreviewConfirmationDark();
        await TheDeleteGameConfirmationDark();
        await TheEditProfileDialogDark();
        await TheDuplicateProfileDialogDark();
    }

    private static async Task SidebarLayoutDark()
    {
        using var harness = await GamesViewHarness.StartAsync();

        harness.Render("games-sidebar-dark");
    }

    private static async Task SidebarLayoutLight()
    {
        using var harness = await GamesViewHarness.StartAsync(theme: ThemePreference.Light);

        harness.Render("games-sidebar-light");
    }

    private static async Task LibraryLayoutDark()
    {
        using var harness = await GamesViewHarness.StartAsync(layout: ShellLayout.Library);

        harness.Render("games-library-dark");
    }

    private static async Task NarrowWindowDark()
    {
        using var harness = await GamesViewHarness.StartAsync(width: 900);

        harness.Render("games-sidebar-900-dark");
    }

    private static async Task AGameWithoutProfilesDark()
    {
        var backend = new FakeBackend(ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings).WithVolume(210 * SampleScenario.Gb, 131 * SampleScenario.Gb).WithGame(SampleScenario.Caretaker, 9_400_000_000));
        using var harness = await GamesViewHarness.StartAsync(backend);

        harness.Render("games-empty-dark");
    }

    private static async Task TheRowMenuDark()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var more = harness.All<Avalonia.Controls.Button>().Where(button => button.Name == "MoreButton").ElementAt(1);
        more.Flyout!.ShowAt(more);

        harness.Render("games-row-menu-dark");
    }

    // ---- The New profile wizard

    private static NewProfileWizardViewModel OpenWizard(GamesViewHarness harness)
    {
        harness.Rig.Screen.NewProfileCommand.Execute(null);
        harness.Settle();
        return Assert.IsType<NewProfileWizardViewModel>(harness.Dialogs.Current);
    }

    private static async Task WizardStepOneDark()
    {
        using var harness = await GamesViewHarness.StartAsync();
        OpenWizard(harness);

        harness.Render("wizard-1-name-dark");
    }

    private static async Task WizardStepOneLight()
    {
        using var harness = await GamesViewHarness.StartAsync(theme: ThemePreference.Light);
        OpenWizard(harness);

        harness.Render("wizard-1-name-light");
    }

    private static async Task WizardStepTwoDark()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var wizard = OpenWizard(harness);
        wizard.Name = "Modded playthrough - 8-8-26";
        wizard.Notes = "Nolvus v6, before the survival patch";
        await wizard.ContinueCommand.ExecuteAsync(null);

        harness.Render("wizard-2-check-dark");
    }

    private static async Task WizardStepThreeSavingDark()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var wizard = OpenWizard(harness);
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Rig.Backend.Jobs.ReportProgress(harness.Rig.Backend.Jobs.LastStarted(JobKind.Capture).JobId, new JobProgress(1_845, 3_430, CurrentPath: @"Data\Textures\Architecture\Whiterun\WRBuilding05.dds"));

        harness.Render("wizard-3-saving-dark");
    }

    private static async Task WizardStepThreeSavedDark()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var wizard = OpenWizard(harness);
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Rig.Backend.Jobs.Complete(harness.Rig.Backend.Jobs.LastStarted(JobKind.Capture).JobId);

        harness.Render("wizard-3-saved-dark");
    }

    private static async Task WizardHalfSavedDark()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var wizard = OpenWizard(harness);
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        var profileId = FakeAssert.Ok(await harness.Rig.Backend.Profiles.ListAsync(SampleScenario.SkyrimId)).Single(profile => profile.Name == wizard.TrimmedName).Id;
        harness.Rig.Backend.Jobs.Complete(
            harness.Rig.Backend.Jobs.LastStarted(JobKind.Capture).JobId,
            new CaptureResult(profileId, new HistoryPointId("pt"), [new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.External, SampleScenario.SavesOutOfRoomError)]));

        harness.Render("wizard-half-saved-dark");
    }

    // ---- Dialogs from the table

    private static async Task TheDeletePreviewConfirmationDark()
    {
        using var harness = await GamesViewHarness.StartAsync();
        harness.Rig.Screen.Profiles[1].DeleteCommand.Execute(null);

        harness.Render("games-delete-profile-preview-dark");
    }

    private static async Task TheDeleteGameConfirmationDark()
    {
        using var harness = await GamesViewHarness.StartAsync();
        harness.Rig.Screen.DeleteGameCommand.Execute(null);

        harness.Render("games-delete-game-dark");
    }

    private static async Task TheEditProfileDialogDark()
    {
        using var harness = await GamesViewHarness.StartAsync();
        harness.Rig.Screen.Profiles[1].EditCommand.Execute(null);

        harness.Render("games-edit-profile-dark");
    }

    private static async Task TheDuplicateProfileDialogDark()
    {
        using var harness = await GamesViewHarness.StartAsync();
        harness.Rig.Screen.Profiles[0].DuplicateCommand.Execute(null);

        harness.Render("games-duplicate-profile-dark");
    }
}
