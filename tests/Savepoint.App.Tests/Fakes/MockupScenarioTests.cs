using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using static Savepoint.App.Tests.Fakes.FakeAssert;
using static Savepoint.App.Tests.Fakes.MockupScenario;

namespace Savepoint.App.Tests.Fakes;

// The default scenario must show what the mockup screens show, read back through the Api the screens use.
public class MockupScenarioTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_rail_lists_three_games_with_their_profile_counts()
    {
        var backend = FakeBackend.Mockup();

        var games = Ok(await backend.Games.ListAsync(Ct));
        var profiles = Ok(await backend.Profiles.ListAsync(cancellationToken: Ct));

        Assert.Equal<string>(["Skyrim Special Edition", "Satisfactory", "The Last Caretaker"], games.Select(g => g.Name));
        Assert.Equal<int>([3, 2, 1], games.Select(g => profiles.Count(p => p.GameId == g.Id)));
    }

    [Fact]
    public async Task The_profile_table_matches_3a()
    {
        var backend = FakeBackend.Mockup();

        var profiles = Ok(await backend.Profiles.ListAsync(SkyrimId, Ct));
        var stats = Ok(await backend.Profiles.GetRowStatsAsync(SkyrimId, Ct));

        Assert.Equal<string>(["Modded playthrough - 7-2-26", "Vanilla - latest update", "Survival test - 5-14-26"], profiles.Select(p => p.Name));
        Assert.Equal<string>([ModdedId, VanillaId, SurvivalId], stats.Select(s => s.ProfileId));
        Assert.Equal<bool>([true, false, false], stats.Select(s => s.IsActive));
        Assert.Equal<int?>([6, 1, 2], stats.Select(s => s.SaveCount));
        Assert.Equal<int?>([212, 0, 189], stats.Select(s => s.ModFileCount));
        Assert.Equal<string?>(["1.6.640", "1.6.1170", "1.6.640"], stats.Select(s => s.DetectedVersion));
        Assert.Equal<RepoRole>([RepoRole.External], stats[2].UnsavedChanges);
        Assert.Equal<double>([3, 42, 84], stats.Select(s => (ScenarioBuilder.DefaultNow - s.LastPlayedAt!.Value).TotalDays));
    }

    [Fact]
    public async Task Storage_matches_the_footer_and_settings()
    {
        var backend = FakeBackend.Mockup();

        var usage = Ok(await backend.Games.GetStorageUsageAsync(SkyrimId, Ct));
        var summary = Ok(await backend.Settings.GetStorageSummaryAsync(Ct));

        Assert.Equal(41_200_000_000, usage.BytesUsed);
        Assert.Equal(210 * Gb, usage.VolumeTotalBytes);
        Assert.Equal(StorageRoot, summary.StorageRoot);
        Assert.Equal(79_300_000_000, summary.BytesUsed);
        Assert.Equal(3, summary.Games.Count);
        Assert.Equal(28_700_000_000, summary.Games.Single(g => g.GameId == SatisfactoryId).Bytes);
        Assert.Equal(131 * Gb, summary.VolumeFreeBytes);
        Assert.Equal(new DateOnly(2026, 7, 1), DateOnly.FromDateTime(summary.LastOptimizedAt!.Value.DateTime));
    }

    [Fact]
    public async Task History_holds_the_drawn_points_newest_first()
    {
        var backend = FakeBackend.Mockup();

        var history = Ok(await backend.History.GetAsync(SkyrimId, cancellationToken: Ct));

        Assert.Equal<string>(
            ["Before the final boss", "Cleared Blackreach", "14 mod files updated", "Fresh start with Nolvus", "Vanilla 1.6.1170", "Deep cave run", "First night", "Survival test - 5-14-26"],
            history.Points.Select(p => p.Name));
        Assert.Equal(BeforeFinalBoss, history.Points.Single(p => p.LoadedNow).Id);
        Assert.Equal(ModsUpdated, history.Points.Single(p => p.InUse).Id);
        var fresh = history.Points.Single(p => p.Id == FreshStart);
        Assert.Equal(HistoryPointKind.VersionModsAndSave, fresh.Kind);
        Assert.Equal(new InstallState("1.6.640", 198), fresh.Install);
        Assert.Equal(41_200_000_000, fresh.Bytes);
        Assert.Equal(14, history.Points.Single(p => p.Id == ModsUpdated).ModFilesChanged);
    }

    [Fact]
    public async Task History_can_be_filtered_to_one_profile()
    {
        var backend = FakeBackend.Mockup();

        var history = Ok(await backend.History.GetAsync(SkyrimId, SurvivalId, Ct));

        Assert.Equal(SurvivalId, history.ProfileId);
        Assert.Equal<HistoryPointId>([DeepCaveRun, FirstNight, SurvivalStart], history.Points.Select(p => p.Id));
    }

    [Fact]
    public async Task Playing_the_active_profile_needs_no_dialog_as_in_2a()
    {
        var backend = FakeBackend.Mockup();

        var preview = Ok(await backend.Launch.GetPreviewAsync(SkyrimId, new LaunchTarget.ProfileTip(ModdedId), Ct));

        Assert.True(preview.ShowBanner);
        Assert.Equal("Before the final boss", preview.AvailableSaves.Single(s => s.Id == preview.SelectedSaveId).Name);
    }

    [Fact]
    public async Task Switching_to_the_survival_profile_shows_the_2b_dialog()
    {
        var backend = FakeBackend.Mockup();

        var preview = Ok(await backend.Launch.GetPreviewAsync(SkyrimId, new LaunchTarget.ProfileTip(SurvivalId), Ct));

        Assert.False(preview.ShowBanner);
        Assert.Equal(new InstallState("1.6.1170", 0), preview.CurrentInstall);
        Assert.Equal(new InstallState("1.6.640", 189), preview.TargetInstall);
        Assert.Equal(new SwitchSummary(1_311, 189, 6_100_000_000, TimeSpan.FromMinutes(2)), preview.Install.Switch);
        Assert.True(preview.External!.Dirty);
        Assert.Equal(3, preview.External.DirtyFileCount);
        Assert.Equal(SurvivalId, preview.External.DirtyOwnerProfileId);
        Assert.Equal(2, preview.AvailableSaves.Count);
        Assert.True(preview.AvailableSaves[0].IsMostRecent);
        Assert.Equal(DeepCaveRun, preview.SelectedSaveId);
    }

    [Fact]
    public async Task The_wizard_preview_matches_2f()
    {
        var backend = FakeBackend.Mockup();

        var preview = Ok(await backend.Games.GetCapturePreviewAsync(SkyrimId, Ct));

        var install = preview.Folders.Single(f => f.Role == RepoRole.Install);
        var saves = preview.Folders.Single(f => f.Role == RepoRole.External);
        Assert.Equal((SkyrimInstall, 212, 41_200_000_000), (install.Path, install.ModFileCount, install.Bytes));
        Assert.Equal((SkyrimSaves, 18), (saves.Path, saves.FileCount));
        Assert.Equal(2_400_000_000, preview.NewBytesEstimate);
    }

    [Fact]
    public async Task The_editor_shows_confirmed_folders_as_in_2g()
    {
        var backend = FakeBackend.Mockup();

        var folders = Ok(await backend.Games.GetFolderStatusAsync(SkyrimId, Ct));

        var install = folders.Single(f => f.Role == RepoRole.Install);
        var saves = folders.Single(f => f.Role == RepoRole.External);
        Assert.Equal(new DateOnly(2026, 7, 2), DateOnly.FromDateTime(install.Consent.ConfirmedAt!.Value.DateTime));
        Assert.Equal(SkyrimSavesRaw, saves.Path);
        Assert.Equal(SkyrimSaves, saves.ResolvedPath);
    }

    [Fact]
    public async Task Settings_match_2k()
    {
        var backend = FakeBackend.Mockup();

        var settings = Ok(await backend.Settings.GetAsync(Ct));

        Assert.Equal(StorageRoot, settings.StorageRoot);
        Assert.Equal(ShellLayout.Sidebar, settings.Layout);
        Assert.Equal(AccentColorId.Blue, settings.AccentColorId);
        Assert.True(settings.ConfirmSessionCapture);
        Assert.False(settings.SkipConfirmWhenOnlySaveChanges);
    }

    [Fact]
    public async Task An_empty_scenario_is_the_first_run()
    {
        var backend = FakeBackend.Empty();

        Assert.Empty(Ok(await backend.Games.ListAsync(Ct)));
        Assert.Equal(@"C:\Users\you\AppData\Local\SavePoint\Storage", Ok(await backend.Settings.GetAsync(Ct)).StorageRoot);
    }
}
