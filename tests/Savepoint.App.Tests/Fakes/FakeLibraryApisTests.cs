using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using static Savepoint.App.Tests.Fakes.FakeAssert;
using static Savepoint.App.Tests.Fakes.SampleScenario;

namespace Savepoint.App.Tests.Fakes;

// Games, profiles, settings, and startup status: the calls that change what the library holds.
public class FakeLibraryApisTests
{
    private static readonly OrphanedHistory _oldSkyrim = new(
        "skyrim-old-a1a1",
        [RepoRole.Install],
        [new RecoverableProfile("profile/modded-run-c4c4", "Modded Run"), new RecoverableProfile("profile/x9x9", null)]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Adding_a_game_saves_it_with_its_folders_confirmed()
    {
        var backend = FakeBackend.Empty();

        var result = await backend.Games.AddAsync(Skyrim);

        Assert.Equal(Skyrim, Assert.IsType<AddGameOutcome.Added>(result.Value).Game);
        Assert.Equal<GameDefinition>([Skyrim], Ok(await backend.Games.ListAsync(Ct)));
        var folders = Ok(await backend.Games.GetFolderStatusAsync(SkyrimId, Ct));
        Assert.Equal<RepoRole>([RepoRole.Install, RepoRole.External], folders.Select(f => f.Role));
        Assert.All(folders, f => Assert.True(f.Exists && f.Consent.Confirmed));
        Assert.Equal(backend.Clock.Now, folders[0].Consent.ConfirmedAt);
    }

    [Fact]
    public async Task Adding_a_game_at_a_folder_with_old_history_asks_instead_of_saving()
    {
        var backend = new FakeBackend(ScenarioBuilder.Empty().WithOrphanedHistory(SkyrimInstall, _oldSkyrim));

        var result = await backend.Games.AddAsync(Skyrim);

        Assert.Same(_oldSkyrim, Assert.IsType<AddGameOutcome.HistoryFound>(result.Value).History);
        Assert.Empty(Ok(await backend.Games.ListAsync(Ct)));
    }

    [Fact]
    public async Task Reconnecting_keeps_the_old_id_and_restores_the_named_profiles()
    {
        var backend = new FakeBackend(ScenarioBuilder.Empty().WithOrphanedHistory(SkyrimInstall, _oldSkyrim));
        var reconnect = new OrphanResolution.Reconnect(
            "skyrim-old-a1a1",
            [new RecoveredProfile("profile/modded-run-c4c4", "Modded Run"), new RecoveredProfile("profile/x9x9", "Recovered profile")]);

        var result = await backend.Games.ResolveOrphanAsync(Skyrim, reconnect);

        Assert.Equal("skyrim-old-a1a1", Ok(result).Game.Id);
        var profiles = Ok(await backend.Profiles.ListAsync("skyrim-old-a1a1", Ct));
        Assert.Equal<string>(["modded-run-c4c4", "x9x9"], profiles.Select(p => p.Id));
        Assert.Equal<string>(["Modded Run", "Recovered profile"], profiles.Select(p => p.Name));
        Assert.Equal("profile/x9x9", profiles[1].BranchName);
    }

    [Fact]
    public async Task Starting_fresh_adds_the_game_under_its_new_id_and_forgets_the_old_history()
    {
        var backend = new FakeBackend(ScenarioBuilder.Empty().WithOrphanedHistory(SkyrimInstall, _oldSkyrim));

        var result = await backend.Games.ResolveOrphanAsync(Skyrim, new OrphanResolution.StartFresh());
        await backend.Games.DeleteAsync(SkyrimId);
        var again = await backend.Games.AddAsync(Skyrim);

        Assert.Equal(SkyrimId, Ok(result).Game.Id);
        Assert.IsType<AddGameOutcome.Added>(again.Value);
    }

    [Fact]
    public async Task Updating_an_unknown_game_fails()
    {
        var backend = FakeBackend.Empty();

        var result = await backend.Games.UpdateAsync(Skyrim);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task Deleting_a_running_game_is_refused()
    {
        var backend = FakeBackend.Sample();
        await backend.Launch.StartAsync(SkyrimId, new LaunchTarget.ProfileTip(ModdedId));

        var result = await backend.Games.DeleteAsync(SkyrimId);

        Assert.Equal("error.gameService.gameRunning", result.Error!.MessageId);
        Assert.Equal(3, Ok(await backend.Games.ListAsync(Ct)).Count);
    }

    [Fact]
    public async Task Deleting_a_game_removes_its_profiles_and_history()
    {
        var backend = FakeBackend.Sample();

        await backend.Games.DeleteAsync(SkyrimId);

        Assert.DoesNotContain(Ok(await backend.Games.ListAsync(Ct)), g => g.Id == SkyrimId);
        Assert.DoesNotContain(Ok(await backend.Profiles.ListAsync(cancellationToken: Ct)), p => p.GameId == SkyrimId);
        Assert.False((await backend.History.GetAsync(SkyrimId, cancellationToken: Ct)).Ok);
    }

    [Fact]
    public async Task Creating_a_profile_adds_it_and_starts_a_capture_of_every_folder()
    {
        var backend = FakeBackend.Sample();

        var started = await backend.Profiles.CreateAsync(SkyrimId, "Modded playthrough - 8-8-26", "Nolvus v6", "Nolvus v6");

        var created = Ok(await backend.Profiles.ListAsync(SkyrimId, Ct)).Single(p => p.Name == "Modded playthrough - 8-8-26");
        Assert.Equal("Nolvus v6", created.Notes);
        Assert.StartsWith("modded-playthrough-8-8-26-", created.Id, StringComparison.Ordinal);
        Assert.Equal($"profile/{created.Id}", created.BranchName);
        var job = Job(backend, Ok(started).JobId);
        Assert.Equal(JobKind.Capture, job.Kind);
        backend.Jobs.Complete(job.JobId);
        var result = Assert.IsType<CaptureResult>(Job(backend, job.JobId).Result);
        Assert.Equal(created.Id, result.ProfileId);
        Assert.Equal(2, result.Folders.Count);
    }

    [Fact]
    public async Task A_new_profile_gets_empty_row_stats_until_the_service_knows_more()
    {
        var backend = FakeBackend.Sample();
        await backend.Profiles.CreateAsync(SkyrimId, "Fresh", null, "Fresh");
        var created = Ok(await backend.Profiles.ListAsync(SkyrimId, Ct)).Single(p => p.Name == "Fresh");

        var stats = Ok(await backend.Profiles.GetRowStatsAsync(SkyrimId, Ct)).Single(s => s.ProfileId == created.Id);

        Assert.False(stats.IsActive);
        Assert.Empty(stats.UnsavedChanges);
        Assert.Equal(0, stats.SnapshotCount);
        Assert.Null(stats.ModFileCount);
    }

    [Fact]
    public async Task Capturing_one_folder_reports_only_that_folder()
    {
        var backend = FakeBackend.Sample();
        var id = Ok(await backend.Profiles.CaptureAsync(SkyrimId, SurvivalId, "Before switching", [RepoRole.External])).JobId;

        backend.Jobs.Complete(id);

        var result = Assert.IsType<CaptureResult>(Job(backend, id).Result);
        Assert.Equal(RepoRole.External, Assert.Single(result.Folders).Role);
    }

    [Fact]
    public async Task Capturing_for_an_unknown_profile_fails_without_a_job()
    {
        var backend = FakeBackend.Sample();

        var result = await backend.Profiles.CaptureAsync(SkyrimId, "nobody-0000", "x");

        Assert.False(result.Ok);
        Assert.Empty(backend.Jobs.Started);
    }

    // The real backend refuses a snapshot of a profile the folders do not hold before it queues a job, and the row says so (SnapshotNow).
    [Fact]
    public async Task Capturing_a_profile_that_is_not_loaded_is_refused_without_a_job()
    {
        var backend = FakeBackend.Sample();

        var result = await backend.Profiles.CaptureAsync(SkyrimId, SurvivalId, "Not loaded");

        Assert.False(result.Ok);
        Assert.Equal("error.profile.notLoaded", result.Error.MessageId);
        Assert.Empty(backend.Jobs.Started);
    }

    // The row knows whether a profile is loaded as a whole, not per folder, so a request for particular folders is let through (it is how
    // the changes a profile owns in one folder are saved first).
    [Fact]
    public async Task Capturing_particular_folders_of_a_profile_that_is_not_loaded_is_let_through()
    {
        var backend = FakeBackend.Sample();

        var result = await backend.Profiles.CaptureAsync(SkyrimId, SurvivalId, "Its own changes", [RepoRole.External]);

        Assert.True(result.Ok);
        Assert.Single(backend.Jobs.Started);
    }

    [Fact]
    public async Task A_profile_with_no_snapshot_yet_can_take_its_first_while_another_profile_is_loaded()
    {
        var fresh = new Profile("profile-fresh", SkyrimId, "Fresh profile", "profile/profile-fresh");
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithProfile(
            fresh,
            new ProfileRowStats(fresh.Id, false, [], 0, null, null, null, null, false, SnapshotNowAvailability.FirstSnapshot)));

        var result = await backend.Profiles.CaptureAsync(SkyrimId, fresh.Id, "First snapshot");

        Assert.True(result.Ok);
        Assert.Single(backend.Jobs.Started);
    }

    [Fact]
    public async Task Creating_a_profile_loads_it_and_leaves_the_one_loaded_before_not_loaded()
    {
        var backend = FakeBackend.Sample();
        Ok(await backend.Profiles.CreateAsync(SkyrimId, "Newest", null, "Newest"));
        var newest = Ok(await backend.Profiles.ListAsync(SkyrimId, Ct)).Single(p => p.Name == "Newest");

        var before = await backend.Profiles.CaptureAsync(SkyrimId, ModdedId, "Loaded until the new one");
        var after = await backend.Profiles.CaptureAsync(SkyrimId, newest.Id, "Loaded now");

        Assert.False(before.Ok);
        Assert.Equal("error.profile.notLoaded", before.Error.MessageId);
        Assert.True(after.Ok);
    }

    [Fact]
    public async Task Updating_a_profile_changes_only_the_user_fields()
    {
        var backend = FakeBackend.Sample();

        var result = await backend.Profiles.UpdateAsync(SkyrimId, VanillaId, new ProfileChanges("Vanilla", "clean install", "1.6.1179"));

        var updated = Ok(result);
        Assert.Equal("Vanilla", updated.Name);
        Assert.Equal("clean install", updated.Notes);
        Assert.Equal("1.6.1179", updated.VersionOverride);
        Assert.Equal($"profile/{VanillaId}", updated.BranchName);
        Assert.NotNull(updated.LastLaunchedAt);
        var stats = Ok(await backend.Profiles.GetRowStatsAsync(SkyrimId, Ct)).Single(s => s.ProfileId == VanillaId);
        Assert.Equal("1.6.1179", stats.VersionOverride);
    }

    [Fact]
    public async Task Duplicating_adds_an_inactive_copy_with_the_source_stats()
    {
        var backend = FakeBackend.Sample();

        var copy = Ok(await backend.Profiles.DuplicateAsync(SkyrimId, ModdedId, "Modded copy"));

        var stats = Ok(await backend.Profiles.GetRowStatsAsync(SkyrimId, Ct)).Single(s => s.ProfileId == copy.Id);
        Assert.Equal("Modded copy", copy.Name);
        Assert.False(stats.IsActive);
        Assert.Equal(212, stats.ModFileCount);
        Assert.Equal("1.6.640", stats.DetectedVersion);
    }

    [Fact]
    public async Task The_delete_preview_counts_points_only_that_profile_holds()
    {
        var backend = FakeBackend.Sample();

        var preview = Ok(await backend.Profiles.GetDeletePreviewAsync(SkyrimId, SurvivalId, Ct));

        Assert.Equal(3, preview.PointsOnlyHere);
        Assert.Equal(1, preview.Folders.Single(f => f.Role == RepoRole.Install).PointCount);
        Assert.Equal(2, preview.Folders.Single(f => f.Role == RepoRole.External).PointCount);
        Assert.Null(preview.BlockedBy);
    }

    [Fact]
    public async Task The_active_profile_cannot_be_deleted_and_its_preview_says_why()
    {
        var backend = FakeBackend.Sample();

        var preview = Ok(await backend.Profiles.GetDeletePreviewAsync(SkyrimId, ModdedId, Ct));
        var result = await backend.Profiles.DeleteAsync(SkyrimId, ModdedId);

        Assert.Equal("error.gameService.profileActive", preview.BlockedBy!.MessageId);
        Assert.Equal("error.gameService.profileActive", result.Error!.MessageId);
        Assert.Contains(Ok(await backend.Profiles.ListAsync(SkyrimId, Ct)), p => p.Id == ModdedId);
    }

    [Fact]
    public async Task Deleting_a_profile_removes_it_with_its_points()
    {
        var backend = FakeBackend.Sample();

        var result = await backend.Profiles.DeleteAsync(SkyrimId, SurvivalId);

        Assert.True(result.Ok);
        Assert.DoesNotContain(Ok(await backend.Profiles.ListAsync(SkyrimId, Ct)), p => p.Id == SurvivalId);
        Assert.DoesNotContain(Ok(await backend.History.GetAsync(SkyrimId, cancellationToken: Ct)).Points, p => p.ProfileId == SurvivalId);
    }

    [Fact]
    public async Task Optimizing_records_the_date_once_it_finishes()
    {
        var backend = FakeBackend.Sample();
        var id = Ok(await backend.Games.OptimizeStorageAsync(SkyrimId)).JobId;
        backend.Clock.Advance(TimeSpan.FromMinutes(5));

        backend.Jobs.Complete(id);

        var summary = Ok(await backend.Settings.GetStorageSummaryAsync(Ct));
        Assert.Equal(backend.Clock.Now, summary.LastOptimizedAt);
        var result = Assert.IsType<OptimizeResult>(Job(backend, id).Result);
        Assert.Equal(SkyrimId, Assert.Single(result.Games).GameId);
    }

    [Fact]
    public async Task Optimizing_everything_is_one_job_across_every_game()
    {
        var backend = FakeBackend.Sample();
        var id = Ok(await backend.Settings.OptimizeAllStorageAsync()).JobId;

        backend.Jobs.Complete(id);

        var job = Job(backend, id);
        Assert.Null(job.GameId);
        Assert.Equal<string>([SkyrimId, SatisfactoryId, CaretakerId], Assert.IsType<OptimizeResult>(job.Result).Games.Select(g => g.GameId));
    }

    [Fact]
    public async Task Saving_settings_keeps_the_storage_root_and_last_optimized_date()
    {
        var backend = FakeBackend.Sample();
        var changed = SampleScenario.Settings with { Layout = ShellLayout.Library, StorageRoot = @"E:\Elsewhere", LastOptimizedAt = null };

        var saved = Ok(await backend.Settings.SaveAsync(changed));

        Assert.Equal(ShellLayout.Library, saved.Layout);
        Assert.Equal(StorageRoot, saved.StorageRoot);
        Assert.Equal(SampleScenario.Settings.LastOptimizedAt, saved.LastOptimizedAt);
        Assert.Equal(saved, (await backend.Settings.GetAsync(Ct)).Value);
    }

    [Fact]
    public async Task A_running_game_blocks_relocation_and_clearing()
    {
        var backend = FakeBackend.Sample();
        await backend.Launch.StartAsync(SkyrimId, new LaunchTarget.ProfileTip(ModdedId));

        var preview = Ok(await backend.Settings.GetRelocationPreviewAsync(@"E:\SavePoint", Ct));
        var relocate = await backend.Settings.RelocateStorageAsync(@"E:\SavePoint");
        var clearPreview = Ok(await backend.Settings.GetClearAllDataPreviewAsync(Ct));
        var clear = await backend.Settings.ClearAllDataAsync();

        Assert.Equal("error.gameService.gameRunning", preview.BlockedBy!.MessageId);
        Assert.Equal("error.gameService.gameRunning", relocate.Error!.MessageId);
        Assert.Equal("error.gameService.gameRunning", clearPreview.BlockedBy!.MessageId);
        Assert.Equal("error.gameService.gameRunning", clear.Error!.MessageId);
    }

    [Fact]
    public async Task A_finished_relocation_points_the_settings_at_the_new_folder()
    {
        var backend = FakeBackend.Sample();
        var id = Ok(await backend.Settings.RelocateStorageAsync(@"E:\SavePoint")).JobId;

        backend.Jobs.Complete(id);

        Assert.Equal(@"E:\SavePoint", Ok(await backend.Settings.GetAsync(Ct)).StorageRoot);
        var result = Assert.IsType<StorageRelocationResult>(Job(backend, id).Result);
        Assert.Equal(StorageRoot, result.PreviousRoot);
    }

    [Fact]
    public async Task A_cancelled_relocation_leaves_the_settings_alone()
    {
        var backend = FakeBackend.Sample();
        var id = Ok(await backend.Settings.RelocateStorageAsync(@"E:\SavePoint")).JobId;

        backend.Jobs.CompleteCancelled(id);

        Assert.Equal(StorageRoot, Ok(await backend.Settings.GetAsync(Ct)).StorageRoot);
        Assert.Equal("error.storageRelocation.cancelled", Job(backend, id).Error!.MessageId);
    }

    [Fact]
    public async Task A_finished_clear_empties_the_library_and_resets_the_settings()
    {
        var backend = FakeBackend.Sample();
        var id = Ok(await backend.Settings.ClearAllDataAsync()).JobId;

        backend.Jobs.Complete(id);

        Assert.Empty(Ok(await backend.Games.ListAsync(Ct)));
        Assert.Empty(Ok(await backend.Profiles.ListAsync(cancellationToken: Ct)));
        var settings = Ok(await backend.Settings.GetAsync(Ct));
        Assert.Equal(@"C:\Users\you\AppData\Local\SavePoint\Storage", settings.StorageRoot);
        Assert.Equal(ShellLayout.Sidebar, settings.Layout);
        Assert.Equal(79_300_000_000, Assert.IsType<ClearAllDataResult>(Job(backend, id).Result).BytesFreed);
    }

    [Fact]
    public async Task Recovering_a_corrupt_catalog_file_moves_it_aside_and_clears_the_status()
    {
        var corrupt = new CorruptCatalogFile(@"C:\cat\profiles.json", @"C:\cat\profiles.json.bak", ErrorInfoFor(@"C:\cat\profiles.json"));
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithStartupStatus(StartupWith(corrupt)));

        var moved = await backend.AppStatus.RecoverCatalogFileAsync(corrupt.Path, CatalogRecovery.RestoreBackup);

        Assert.StartsWith(corrupt.Path, Ok(moved), StringComparison.Ordinal);
        Assert.NotEqual(corrupt.Path, moved.Value);
        Assert.Empty(Ok(await backend.AppStatus.CheckAsync(Ct)).Catalog.CorruptFiles);
    }

    [Fact]
    public async Task Restoring_a_backup_that_does_not_exist_fails_and_keeps_the_file_listed()
    {
        var corrupt = new CorruptCatalogFile(@"C:\cat\games.json", null, ErrorInfoFor(@"C:\cat\games.json"));
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithStartupStatus(StartupWith(corrupt)));

        var result = await backend.AppStatus.RecoverCatalogFileAsync(corrupt.Path, CatalogRecovery.RestoreBackup);

        Assert.False(result.Ok);
        Assert.Single(Ok(await backend.AppStatus.CheckAsync(Ct)).Catalog.CorruptFiles);
    }

    private static ErrorInfo ErrorInfoFor(string path) =>
        ErrorInfo.From(new CatalogCorruptException(path, null));

    private static StartupStatus StartupWith(CorruptCatalogFile corrupt) => new(
        new GitStatus(true, @"C:\Program Files\SavePoint\git\cmd\git.exe", "2.56.0.windows.2", null),
        new CatalogStatus(@"C:\cat", [corrupt]));
}
