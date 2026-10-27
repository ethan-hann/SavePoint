using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Games;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

// The New profile wizard for a game that keeps its mods outside the game folder: three folders in the preview and in the outcome.
public class NewProfileWizardModsFolderTests
{
    private const string _dot = " · ";

    private static async Task<(NewProfileWizardViewModel Wizard, FakeBackend Backend)> StartAsync()
    {
        var preview = new CapturePreview(
            ModsSamples.Id,
            [
                new FolderCapturePreview(RepoRole.Install, ModsSamples.InstallPath, 900, 0, 9_400_000_000),
                new FolderCapturePreview(RepoRole.Mods, ModsSamples.ModsResolved, 40, 40, 3_100_000_000),
                new FolderCapturePreview(RepoRole.External, ModsSamples.SavesResolved, 1, 0, 2_000),
            ],
            0);
        var backend = new FakeBackend(ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings).WithGame(ModsSamples.SevenDays, 1).WithCapturePreview(preview));
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, "windows", TimeZoneInfo.Utc);
        var wizard = new NewProfileWizardViewModel(ModsSamples.SevenDays, backend.Games, backend.Profiles, backend.Jobs, text, new ImmediateUiDispatcher(), backend.Clock);
        await wizard.LoadPreviewAsync();
        return (wizard, backend);
    }

    private static async Task<Guid> SaveAsync(NewProfileWizardViewModel wizard, FakeBackend backend)
    {
        wizard.Name = "Alpha 21";
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        return backend.Jobs.LastStarted(JobKind.Capture).JobId;
    }

    [Fact]
    public async Task The_preview_lists_the_game_the_mods_and_the_saves_folders_in_that_order_with_their_sizes()
    {
        var (wizard, _) = await StartAsync();

        Assert.Equal(["Game version and mods", "Mods", "Current saves"], wizard.Preview.Rows.Select(row => row.Title));
        Assert.Equal(ModsSamples.ModsResolved + _dot + "40 files" + _dot + "3.1 GB", wizard.Preview.Rows[1].Subtitle);
    }

    [Fact]
    public async Task A_snapshot_that_took_the_game_and_saves_but_not_the_mods_says_so_and_retries_only_the_mods()
    {
        var (wizard, backend) = await StartAsync();
        var job = await SaveAsync(wizard, backend);
        var profileId = FakeAssert.Ok(await backend.Profiles.ListAsync(ModsSamples.Id, TestContext.Current.CancellationToken)).Single().Id;

        backend.Jobs.Complete(job, new CaptureResult(profileId, new HistoryPointId("pt"), [new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.Mods, SampleScenario.SavesOutOfRoomError), new FolderOutcome(RepoRole.External)]));

        var card = Assert.IsType<ErrorCardViewModel>(wizard.ResultCard);
        Assert.StartsWith("Your game version and mods are in the snapshot. Your saves are in the snapshot. Your mods aren't yet. Not enough free disk space", card.Body, StringComparison.Ordinal);
        Assert.Equal("Retry the mods", Assert.Single(card.Actions).Label);
        Assert.Equal(
            [("Game version and mods" + _dot + "in the snapshot", true), ("Mods" + _dot + "not in the snapshot yet", false), ("Saves" + _dot + "in the snapshot", true)],
            ((SaveOutcomeListViewModel)card.Supplement!).Rows.Select(row => (row.Text, row.Succeeded)));

        await ((IAsyncRelayCommand)card.Actions[0].Command).ExecuteAsync(null);

        var call = Assert.Single(backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));
        Assert.Equal([RepoRole.Mods], Assert.IsAssignableFrom<IEnumerable<RepoRole>>(call.Args[3]));
    }

    [Fact]
    public async Task A_snapshot_that_missed_two_folders_offers_one_retry_for_what_did_not_save()
    {
        var (wizard, backend) = await StartAsync();
        var job = await SaveAsync(wizard, backend);
        var profileId = FakeAssert.Ok(await backend.Profiles.ListAsync(ModsSamples.Id, TestContext.Current.CancellationToken)).Single().Id;

        backend.Jobs.Complete(job, new CaptureResult(profileId, new HistoryPointId("pt"), [new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.Mods, SampleScenario.SavesOutOfRoomError), new FolderOutcome(RepoRole.External, SampleScenario.SavesOutOfRoomError)]));

        var card = Assert.IsType<ErrorCardViewModel>(wizard.ResultCard);
        Assert.Equal("Retry what didn't save", Assert.Single(card.Actions).Label);
        Assert.StartsWith("Your game version and mods are in the snapshot. Your mods aren't yet. Your saves aren't yet.", card.Body, StringComparison.Ordinal);
    }
}
