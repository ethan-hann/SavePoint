using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Games;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

// Writes PNGs to render-output/games/ (gitignored) for side-by-side review against the mockups at 1040x660.
public class GamesRenderTests
{
    private static void AssertRendered(string path) =>
        Assert.True(new FileInfo(path).Length > 10_000, "The render looks empty.");

    [AvaloniaFact]
    public async Task Sidebar_layout_dark_renders_to_a_png()
    {
        using var harness = await GamesViewHarness.StartAsync();

        AssertRendered(harness.Render("3a-games-sidebar-dark"));
    }

    [AvaloniaFact]
    public async Task Sidebar_layout_light_renders_to_a_png()
    {
        using var harness = await GamesViewHarness.StartAsync(theme: ThemePreference.Light);

        AssertRendered(harness.Render("3a-games-sidebar-light"));
    }

    [AvaloniaFact]
    public async Task Library_layout_dark_renders_to_a_png()
    {
        using var harness = await GamesViewHarness.StartAsync(layout: ShellLayout.Library);

        AssertRendered(harness.Render("3b-games-library-dark"));
    }

    [AvaloniaFact]
    public async Task Narrow_window_dark_renders_to_a_png()
    {
        using var harness = await GamesViewHarness.StartAsync(width: 900);

        AssertRendered(harness.Render("3a-games-sidebar-900-dark"));
    }

    [AvaloniaFact]
    public async Task A_game_without_profiles_dark_renders_to_a_png()
    {
        var backend = new FakeBackend(ScenarioBuilder.Empty().WithSettings(MockupScenario.Settings).WithVolume(210 * MockupScenario.Gb, 131 * MockupScenario.Gb).WithGame(MockupScenario.Caretaker, 9_400_000_000));
        using var harness = await GamesViewHarness.StartAsync(backend);

        AssertRendered(harness.Render("games-empty-dark"));
    }

    [AvaloniaFact]
    public async Task The_row_menu_dark_renders_to_a_png()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var more = harness.All<Avalonia.Controls.Button>().Where(button => button.Name == "MoreButton").ElementAt(1);
        more.Flyout!.ShowAt(more);

        AssertRendered(harness.Render("games-row-menu-dark"));
    }

    // ---- The New profile wizard (2f)

    private static NewProfileWizardViewModel OpenWizard(GamesViewHarness harness)
    {
        harness.Rig.Screen.NewProfileCommand.Execute(null);
        harness.Settle();
        return Assert.IsType<NewProfileWizardViewModel>(harness.Dialogs.Current);
    }

    [AvaloniaFact]
    public async Task Wizard_step_one_dark_renders_to_a_png()
    {
        using var harness = await GamesViewHarness.StartAsync();
        OpenWizard(harness);

        AssertRendered(harness.Render("2f-wizard-1-name-dark"));
    }

    [AvaloniaFact]
    public async Task Wizard_step_one_light_renders_to_a_png()
    {
        using var harness = await GamesViewHarness.StartAsync(theme: ThemePreference.Light);
        OpenWizard(harness);

        AssertRendered(harness.Render("2f-wizard-1-name-light"));
    }

    [AvaloniaFact]
    public async Task Wizard_step_two_dark_renders_to_a_png()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var wizard = OpenWizard(harness);
        wizard.Name = "Modded playthrough - 8-8-26";
        wizard.Notes = "Nolvus v6, before the survival patch";
        await wizard.ContinueCommand.ExecuteAsync(null);

        AssertRendered(harness.Render("2f-wizard-2-check-dark"));
    }

    [AvaloniaFact]
    public async Task Wizard_step_three_saving_dark_renders_to_a_png()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var wizard = OpenWizard(harness);
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Rig.Backend.Jobs.ReportProgress(harness.Rig.Backend.Jobs.LastStarted(JobKind.Capture).JobId, new JobProgress(1_845, 3_430, CurrentPath: @"Data\Textures\Architecture\Whiterun\WRBuilding05.dds"));

        AssertRendered(harness.Render("2f-wizard-3-saving-dark"));
    }

    [AvaloniaFact]
    public async Task Wizard_step_three_saved_dark_renders_to_a_png()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var wizard = OpenWizard(harness);
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        harness.Rig.Backend.Jobs.Complete(harness.Rig.Backend.Jobs.LastStarted(JobKind.Capture).JobId);

        AssertRendered(harness.Render("2f-wizard-3-saved-dark"));
    }

    [AvaloniaFact]
    public async Task Wizard_half_saved_dark_renders_to_a_png()
    {
        using var harness = await GamesViewHarness.StartAsync();
        var wizard = OpenWizard(harness);
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        var profileId = FakeAssert.Ok(await harness.Rig.Backend.Profiles.ListAsync(MockupScenario.SkyrimId)).Single(profile => profile.Name == wizard.TrimmedName).Id;
        harness.Rig.Backend.Jobs.Complete(
            harness.Rig.Backend.Jobs.LastStarted(JobKind.Capture).JobId,
            new CaptureResult(profileId, new HistoryPointId("pt"), [new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.External, MockupScenario.SavesOutOfRoomError)]));

        AssertRendered(harness.Render("2l-wizard-half-saved-dark"));
    }

    // ---- Dialogs from the table

    [AvaloniaFact]
    public async Task The_delete_preview_confirmation_dark_renders_to_a_png()
    {
        using var harness = await GamesViewHarness.StartAsync();
        harness.Rig.Screen.Profiles[1].DeleteCommand.Execute(null);

        AssertRendered(harness.Render("games-delete-profile-preview-dark"));
    }

    [AvaloniaFact]
    public async Task The_delete_game_confirmation_dark_renders_to_a_png()
    {
        using var harness = await GamesViewHarness.StartAsync();
        harness.Rig.Screen.DeleteGameCommand.Execute(null);

        AssertRendered(harness.Render("games-delete-game-dark"));
    }

    [AvaloniaFact]
    public async Task The_edit_profile_dialog_dark_renders_to_a_png()
    {
        using var harness = await GamesViewHarness.StartAsync();
        harness.Rig.Screen.Profiles[1].EditCommand.Execute(null);

        AssertRendered(harness.Render("games-edit-profile-dark"));
    }

    [AvaloniaFact]
    public async Task The_duplicate_profile_dialog_dark_renders_to_a_png()
    {
        using var harness = await GamesViewHarness.StartAsync();
        harness.Rig.Screen.Profiles[0].DuplicateCommand.Execute(null);

        AssertRendered(harness.Render("games-duplicate-profile-dark"));
    }
}
