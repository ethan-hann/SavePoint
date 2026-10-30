using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Games;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Games;

// The wizard's first step offers to start with no saves and no mods, each only for a game that has that folder, and the choice goes to the Api.
public class NewProfileWizardStartEmptyTests
{
    private const string _dot = " · ";

    // A folder is "held" when its files are already in a snapshot of a profile, which is what lets a new profile start it empty.
    private static async Task<(NewProfileWizardViewModel Wizard, FakeBackend Backend)> StartAsync(GameDefinition? game = null, bool held = true, StartEmptyBlock block = StartEmptyBlock.None)
    {
        game ??= ModsSamples.SevenDays;
        var preview = new CapturePreview(
            game.Id,
            [
                new FolderCapturePreview(RepoRole.Install, ModsSamples.InstallPath, 900, 0, 9_400_000_000),
                .. game.Mods is null ? Array.Empty<FolderCapturePreview>() : [new FolderCapturePreview(RepoRole.Mods, ModsSamples.ModsResolved, 40, 40, 3_100_000_000, held, block)],
                .. game.External is null ? Array.Empty<FolderCapturePreview>() : [new FolderCapturePreview(RepoRole.External, ModsSamples.SavesResolved, 1, 0, 2_000, held, block)],
            ],
            0);
        return await StartWithAsync(game, preview);
    }

    private static async Task<(NewProfileWizardViewModel Wizard, FakeBackend Backend)> StartWithAsync(GameDefinition game, CapturePreview preview)
    {
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

        Assert.Equal(["Game version", "Mods", "Saves"], wizard.Preview.Rows.Select(row => row.Title));
        Assert.Equal(ModsSamples.ModsResolved + _dot + "starts with no mods", wizard.Preview.Rows[1].Subtitle);
        Assert.Equal(ModsSamples.SavesResolved + _dot + "starts with no saves", wizard.Preview.Rows[2].Subtitle);

        wizard.StartWithoutMods = false;
        wizard.StartWithoutSaves = false;

        Assert.Equal(["Game version", "Mods", "Current saves"], wizard.Preview.Rows.Select(row => row.Title));
        Assert.Equal(ModsSamples.ModsResolved + _dot + "40 files" + _dot + "3.1 GB", wizard.Preview.Rows[1].Subtitle);
    }

    [Fact]
    public async Task Both_boxes_can_be_ticked_when_the_current_files_are_in_a_snapshot_and_say_what_they_do()
    {
        var (wizard, _) = await StartAsync();

        Assert.True(wizard.CanStartWithoutSaves);
        Assert.True(wizard.CanStartWithoutMods);
        Assert.Contains("Nothing is removed now", wizard.StartWithoutSavesHint, StringComparison.Ordinal);
        Assert.Contains("Nothing is removed now", wizard.StartWithoutModsHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_folder_with_new_or_changed_files_cannot_start_empty_and_the_hint_says_to_take_a_snapshot_first()
    {
        var (wizard, _) = await StartAsync(held: false, block: StartEmptyBlock.NewChanges);

        Assert.False(wizard.CanStartWithoutSaves);
        Assert.False(wizard.CanStartWithoutMods);
        Assert.Equal("Take a snapshot first, so the files you have now are kept.", wizard.StartWithoutSavesHint);
        Assert.Equal("Take a snapshot first, so the files you have now are kept.", wizard.StartWithoutModsHint);
        Assert.True(wizard.SavesBoxIsOff);
        Assert.True(wizard.ModsBoxIsOff);
    }

    [Fact]
    public async Task On_a_games_first_profile_the_hint_says_to_make_this_profile_first_instead_of_taking_a_snapshot()
    {
        var (wizard, _) = await StartAsync(held: false, block: StartEmptyBlock.FirstProfile);

        Assert.Equal("Your current files aren't in a snapshot yet. Make this profile first, then a later one can start empty.", wizard.StartWithoutSavesHint);
        Assert.Equal("Your current files aren't in a snapshot yet. Make this profile first, then a later one can start empty.", wizard.StartWithoutModsHint);
    }

    [Fact]
    public async Task While_a_switch_is_stopped_the_hint_says_to_finish_or_discard_it()
    {
        var (wizard, _) = await StartAsync(held: false, block: StartEmptyBlock.StoppedSwitch);

        Assert.Equal("Finish or discard the stopped switch first.", wizard.StartWithoutSavesHint);
        Assert.Equal("Finish or discard the stopped switch first.", wizard.StartWithoutModsHint);
    }

    [Fact]
    public async Task For_any_other_reason_the_hint_does_not_point_at_a_snapshot_or_a_stopped_switch()
    {
        var (wizard, _) = await StartAsync(held: false, block: StartEmptyBlock.Other);

        Assert.Equal("SavePoint can't check these files right now. Load one of this game's profiles and close any program using them, then try again.", wizard.StartWithoutSavesHint);
    }

    [Fact]
    public async Task A_folder_left_on_an_older_snapshot_by_go_back_says_to_load_one_of_the_games_profiles_first()
    {
        var (wizard, _) = await StartAsync(held: false, block: StartEmptyBlock.OlderSnapshot);

        Assert.Equal("This folder is on an older snapshot after Go back. Load one of this game's profiles first, then try again.", wizard.StartWithoutSavesHint);
        Assert.Equal("This folder is on an older snapshot after Go back. Load one of this game's profiles first, then try again.", wizard.StartWithoutModsHint);
        Assert.True(wizard.SavesBoxIsOff);
    }

    [Theory]
    [InlineData(StartEmptyBlock.NotConfirmed, "SavePoint hasn't confirmed this folder yet. Open Edit game and choose Save changes to confirm it, then try again.")]
    [InlineData(StartEmptyBlock.Other, "SavePoint can't check these files right now. Load one of this game's profiles and close any program using them, then try again.")]
    public async Task A_folder_whose_marker_problem_kept_its_files_unread_gives_its_own_reason_and_the_other_folder_is_still_previewed(StartEmptyBlock block, string reason)
    {
        var game = ModsSamples.SevenDays;
        var (wizard, _) = await StartWithAsync(
            game,
            new CapturePreview(
                game.Id,
                [
                    new FolderCapturePreview(RepoRole.Install, ModsSamples.InstallPath, 900, 0, 9_400_000_000),
                    new FolderCapturePreview(RepoRole.Mods, ModsSamples.ModsResolved, 0, 0, 0, false, block, FilesCounted: false),
                    new FolderCapturePreview(RepoRole.External, ModsSamples.SavesResolved, 1, 0, 2_000, true),
                ],
                0));

        Assert.True(wizard.ModsBoxIsOff);
        Assert.Equal(reason, wizard.StartWithoutModsHint);
        Assert.Equal("Start with no mods. " + reason, wizard.StartWithoutModsReasonName);
        Assert.False(wizard.SavesBoxIsOff);
        Assert.Equal(ModsSamples.ModsResolved + _dot + "SavePoint can't count these files right now", wizard.Preview.Rows[1].Subtitle);
        Assert.Equal(ModsSamples.SavesResolved + _dot + "1 file", wizard.Preview.Rows[2].Subtitle);
    }

    [Fact]
    public async Task A_box_that_is_on_is_not_reported_as_off()
    {
        var (wizard, _) = await StartAsync();

        Assert.False(wizard.SavesBoxIsOff);
        Assert.False(wizard.ModsBoxIsOff);
    }

    [Fact]
    public async Task The_name_a_screen_reader_speaks_for_an_off_box_is_the_box_and_its_reason()
    {
        var (wizard, _) = await StartAsync(held: false, block: StartEmptyBlock.StoppedSwitch);

        Assert.Equal("Start with no saves. Finish or discard the stopped switch first.", wizard.StartWithoutSavesReasonName);
        Assert.Equal("Start with no mods. Finish or discard the stopped switch first.", wizard.StartWithoutModsReasonName);
    }

    [Fact]
    public async Task A_box_that_is_off_never_asks_for_an_empty_start_even_when_set()
    {
        var (wizard, backend) = await StartAsync(held: false);
        wizard.StartWithoutSaves = true;
        wizard.StartWithoutMods = true;

        Assert.Empty(await SavedWithAsync(wizard, backend));
    }

    [Fact]
    public async Task Before_the_preview_is_read_neither_box_can_be_ticked_and_no_reason_is_given_yet()
    {
        var game = ModsSamples.SevenDays;
        var backend = new FakeBackend(ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings).WithGame(game, 1));
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, "windows", TimeZoneInfo.Utc);
        var wizard = new NewProfileWizardViewModel(game, backend.Games, backend.Profiles, backend.Jobs, text, new ImmediateUiDispatcher(), backend.Clock);

        Assert.False(wizard.CanStartWithoutSaves);
        Assert.False(wizard.CanStartWithoutMods);
        Assert.Contains("Nothing is removed now", wizard.StartWithoutSavesHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_the_preview_raises_the_boxes_state_so_the_view_updates()
    {
        var game = ModsSamples.SevenDays;
        var backend = new FakeBackend(ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings).WithGame(game, 1).WithCapturePreview(
            new CapturePreview(game.Id, [new FolderCapturePreview(RepoRole.Mods, ModsSamples.ModsResolved, 40, 40, 3_100_000_000, true)], 0)));
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, "windows", TimeZoneInfo.Utc);
        var wizard = new NewProfileWizardViewModel(game, backend.Games, backend.Profiles, backend.Jobs, text, new ImmediateUiDispatcher(), backend.Clock);
        var raised = new List<string?>();
        wizard.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await wizard.LoadPreviewAsync();

        Assert.Contains(nameof(NewProfileWizardViewModel.CanStartWithoutMods), raised);
        Assert.Contains(nameof(NewProfileWizardViewModel.StartWithoutModsHint), raised);
        Assert.True(wizard.CanStartWithoutMods);
        Assert.False(wizard.CanStartWithoutSaves);
    }

    [Fact]
    public async Task The_fake_refuses_what_the_real_backend_refuses_for_a_folder_no_snapshot_holds()
    {
        var (_, backend) = await StartAsync(held: false);

        var refused = await backend.Profiles.CreateAsync(ModsSamples.Id, "A", null, "A", [RepoRole.Mods]);

        Assert.False(refused.Ok);
        var issue = Assert.Single(Assert.IsAssignableFrom<IEnumerable<ValidationIssue>>(refused.Error!.Args["issues"]));
        Assert.Equal(ValidationMessageIds.ProfileStartEmptyUnsaved, issue.MessageId);
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
