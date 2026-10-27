using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Games;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

// The wizard's first step offers to start with no saves and no mods, each only for a game that has that folder, and the choice goes to the Api.
public class NewProfileWizardStartEmptyTests
{
    private const string _dot = " · ";

    private static async Task<(NewProfileWizardViewModel Wizard, FakeBackend Backend)> StartAsync(GameDefinition? game = null)
    {
        game ??= ModsSamples.SevenDays;
        var preview = new CapturePreview(
            game.Id,
            [
                new FolderCapturePreview(RepoRole.Install, ModsSamples.InstallPath, 900, 0, 9_400_000_000),
                .. game.Mods is null ? Array.Empty<FolderCapturePreview>() : [new FolderCapturePreview(RepoRole.Mods, ModsSamples.ModsResolved, 40, 40, 3_100_000_000)],
                .. game.External is null ? Array.Empty<FolderCapturePreview>() : [new FolderCapturePreview(RepoRole.External, ModsSamples.SavesResolved, 1, 0, 2_000)],
            ],
            0);
        var backend = new FakeBackend(ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings).WithGame(game, 1).WithCapturePreview(preview));
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, "windows", TimeZoneInfo.Utc);
        var wizard = new NewProfileWizardViewModel(game, backend.Games, backend.Profiles, backend.Jobs, text, new ImmediateUiDispatcher(), backend.Clock);
        await wizard.LoadPreviewAsync();
        return (wizard, backend);
    }

    private static async Task<IReadOnlyCollection<RepoRole>> SavedWithAsync(NewProfileWizardViewModel wizard, FakeBackend backend)
    {
        wizard.Name = "Test run";
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        var call = Assert.Single(backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CreateAsync)));
        return Assert.IsAssignableFrom<IReadOnlyCollection<RepoRole>>(call.Args[4]);
    }

    [Fact]
    public async Task Each_box_is_offered_only_when_the_game_has_that_folder()
    {
        var (both, _) = await StartAsync();
        var (savesOnly, _) = await StartAsync(ModsSamples.SevenDays with { Mods = null });
        var (modsOnly, _) = await StartAsync(ModsSamples.SevenDays with { External = null });
        var (neither, _) = await StartAsync(ModsSamples.SevenDays with { Mods = null, External = null });

        Assert.Equal((true, true), (both.HasSavesFolder, both.HasModsFolder));
        Assert.Equal((true, false), (savesOnly.HasSavesFolder, savesOnly.HasModsFolder));
        Assert.Equal((false, true), (modsOnly.HasSavesFolder, modsOnly.HasModsFolder));
        Assert.Equal((false, false), (neither.HasSavesFolder, neither.HasModsFolder));
        Assert.True(both.HasStartEmptyChoices);
        Assert.False(neither.HasStartEmptyChoices);
    }

    [Fact]
    public async Task Neither_box_is_ticked_to_begin_with_and_creating_then_starts_nothing_empty()
    {
        var (wizard, backend) = await StartAsync();

        Assert.False(wizard.StartWithoutSaves);
        Assert.False(wizard.StartWithoutMods);
        Assert.Empty(await SavedWithAsync(wizard, backend));
    }

    [Fact]
    public async Task Ticking_no_mods_asks_for_an_empty_mods_folder_only()
    {
        var (wizard, backend) = await StartAsync();
        wizard.StartWithoutMods = true;

        Assert.Equal([RepoRole.Mods], await SavedWithAsync(wizard, backend));
    }

    [Fact]
    public async Task Ticking_no_saves_asks_for_an_empty_saves_folder_only()
    {
        var (wizard, backend) = await StartAsync();
        wizard.StartWithoutSaves = true;

        Assert.Equal([RepoRole.External], await SavedWithAsync(wizard, backend));
    }

    [Fact]
    public async Task Ticking_both_asks_for_both_and_never_for_the_install_folder()
    {
        var (wizard, backend) = await StartAsync();
        wizard.StartWithoutSaves = true;
        wizard.StartWithoutMods = true;

        var asked = await SavedWithAsync(wizard, backend);

        Assert.Equal(2, asked.Count);
        Assert.Contains(RepoRole.Mods, asked);
        Assert.Contains(RepoRole.External, asked);
    }

    [Fact]
    public async Task A_box_ticked_and_then_cleared_asks_for_nothing()
    {
        var (wizard, backend) = await StartAsync();
        wizard.StartWithoutMods = true;
        wizard.StartWithoutMods = false;

        Assert.Empty(await SavedWithAsync(wizard, backend));
    }

    [Fact]
    public async Task The_preview_says_which_folders_start_with_nothing_on_both_steps_and_goes_back_when_cleared()
    {
        var (wizard, _) = await StartAsync();

        wizard.StartWithoutMods = true;
        wizard.StartWithoutSaves = true;

        Assert.Equal(["Game version and mods", "Mods", "Saves"], wizard.Preview.Rows.Select(row => row.Title));
        Assert.Equal(ModsSamples.ModsResolved + _dot + "starts with no mods", wizard.Preview.Rows[1].Subtitle);
        Assert.Equal(ModsSamples.SavesResolved + _dot + "starts with no saves", wizard.Preview.Rows[2].Subtitle);

        wizard.StartWithoutMods = false;
        wizard.StartWithoutSaves = false;

        Assert.Equal(["Game version and mods", "Mods", "Current saves"], wizard.Preview.Rows.Select(row => row.Title));
        Assert.Equal(ModsSamples.ModsResolved + _dot + "40 files" + _dot + "3.1 GB", wizard.Preview.Rows[1].Subtitle);
    }

    [Fact]
    public async Task A_choice_made_before_the_preview_loads_is_shown_once_it_does()
    {
        var game = ModsSamples.SevenDays;
        var backend = new FakeBackend(ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings).WithGame(game, 1).WithCapturePreview(
            new CapturePreview(game.Id, [new FolderCapturePreview(RepoRole.Install, ModsSamples.InstallPath, 900, 0, 9_400_000_000), new FolderCapturePreview(RepoRole.Mods, ModsSamples.ModsResolved, 40, 40, 3_100_000_000)], 0)));
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, "windows", TimeZoneInfo.Utc);
        var wizard = new NewProfileWizardViewModel(game, backend.Games, backend.Profiles, backend.Jobs, text, new ImmediateUiDispatcher(), backend.Clock);
        wizard.StartWithoutMods = true;

        await wizard.LoadPreviewAsync();

        Assert.Equal(ModsSamples.ModsResolved + _dot + "starts with no mods", wizard.Preview.Rows.Single(row => row.Title == "Mods").Subtitle);
    }

    [Fact]
    public async Task The_fake_refuses_what_the_real_backend_refuses()
    {
        var (_, backend) = await StartAsync(ModsSamples.SevenDays with { Mods = null });

        var install = await backend.Profiles.CreateAsync(ModsSamples.Id, "A", null, "A", [RepoRole.Install]);
        var absent = await backend.Profiles.CreateAsync(ModsSamples.Id, "B", null, "B", [RepoRole.Mods]);

        Assert.False(install.Ok);
        Assert.False(absent.Ok);
        Assert.Equal("error.validation.invalidPayload", install.Error!.MessageId);
    }

    [Fact]
    public async Task The_fake_row_of_a_profile_made_with_no_mods_counts_zero_mod_files()
    {
        var (wizard, backend) = await StartAsync();
        wizard.StartWithoutMods = true;
        await SavedWithAsync(wizard, backend);

        var rows = FakeAssert.Ok(await backend.Profiles.GetRowStatsAsync(ModsSamples.Id, TestContext.Current.CancellationToken));

        Assert.Equal(0, Assert.Single(rows).ModFileCount);
    }
}
