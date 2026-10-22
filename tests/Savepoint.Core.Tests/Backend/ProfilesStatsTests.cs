using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Backend;

public class ProfilesStatsTests
{
    [Fact]
    public async Task A_fresh_profile_is_active_with_one_snapshot_and_nothing_unsaved()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);

        var row = await RowAsync(workspace, game, profile);

        Assert.True(row.IsActive);
        Assert.Empty(row.UnsavedChanges);
        Assert.Equal(1, row.SnapshotCount);
        Assert.Null(row.ModFileCount);
        Assert.Null(row.DetectedVersion);
        Assert.Null(row.VersionOverride);
        Assert.Null(row.LastPlayedAt);
        Assert.False(row.IsRunning);
    }

    [Fact]
    public async Task A_game_without_saves_counts_the_snapshots_of_its_install()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));
        var (profile, _) = await workspace.CreateProfileAsync(game);

        Assert.Equal(1, (await RowAsync(workspace, game, profile)).SnapshotCount);
    }

    [Fact]
    public async Task Every_snapshot_counts_whichever_folders_it_took()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "two");
        await CaptureAsync(workspace, game, profile, "Second");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "only-install.pak"), "x");
        await CaptureAsync(workspace, game, profile, "Install change", [RepoRole.Install]);

        Assert.Equal(3, (await RowAsync(workspace, game, profile)).SnapshotCount);
    }

    [Fact]
    public async Task A_copy_counts_only_the_snapshots_it_took_itself_the_way_History_lists_them()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (source, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "two");
        await CaptureAsync(workspace, game, source, "Second");
        var copy = (await workspace.Profiles.DuplicateAsync(game.Id, source.Id, "Vanilla copy")).Value!;

        var rows = (await workspace.Profiles.GetRowStatsAsync(game.Id, workspace.Token)).Value!;

        Assert.Equal(2, rows.Single(row => row.ProfileId == source.Id).SnapshotCount);
        Assert.Equal(0, rows.Single(row => row.ProfileId == copy.Id).SnapshotCount);
        foreach (var profile in new[] { source, copy })
        {
            var listed = (await workspace.History.GetAsync(game.Id, profile.Id, workspace.Token)).Value!.Points.Count;
            Assert.Equal(listed, rows.Single(row => row.ProfileId == profile.Id).SnapshotCount);
        }
    }

    [Fact]
    public async Task Only_the_profile_the_folders_are_on_is_active()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (first, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        var (second, _) = await workspace.CreateProfileAsync(game, "Modded");

        var rows = (await workspace.Profiles.GetRowStatsAsync(game.Id, workspace.Token)).Value!;

        Assert.False(rows.Single(r => r.ProfileId == first.Id).IsActive);
        Assert.True(rows.Single(r => r.ProfileId == second.Id).IsActive);
    }

    [Fact]
    public async Task Edits_in_the_install_folder_are_unsaved_changes_of_the_active_profile()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        File.WriteAllText(Path.Combine(game.Install.Path, "data", "base.pak"), "edited");

        var row = await RowAsync(workspace, game, profile);

        Assert.False(row.IsActive);
        Assert.Equal([RepoRole.Install], row.UnsavedChanges);
    }

    [Fact]
    public async Task A_new_save_file_is_an_unsaved_change_in_the_saves_folder()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "autosave.sav"), "new");

        var row = await RowAsync(workspace, game, profile);

        Assert.False(row.IsActive);
        Assert.Equal([RepoRole.External], row.UnsavedChanges);
    }

    [Fact]
    public async Task A_profile_made_while_the_saves_folder_was_empty_is_active_with_nothing_unsaved()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        File.Delete(Path.Combine(game.External!.Path, "slot1.sav"));
        var (profile, _) = await workspace.CreateProfileAsync(game);

        var row = await RowAsync(workspace, game, profile);

        Assert.True(row.IsActive);
        Assert.Empty(row.UnsavedChanges);
        Assert.Equal(1, row.SnapshotCount);
    }

    [Fact]
    public async Task A_save_the_game_writes_after_a_profile_was_made_from_an_empty_saves_folder_is_that_profiles_unsaved_change()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        File.Delete(Path.Combine(game.External!.Path, "slot1.sav"));
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "first.sav"), "new");

        var row = await RowAsync(workspace, game, profile);

        Assert.False(row.IsActive);
        Assert.Equal([RepoRole.External], row.UnsavedChanges);
    }

    [Fact]
    public async Task Unsaved_changes_belong_to_the_profile_whose_branch_the_folder_is_on_not_to_the_others()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (first, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        var (second, _) = await workspace.CreateProfileAsync(game, "Modded");
        File.WriteAllText(Path.Combine(game.Install.Path, "data", "base.pak"), "edited");

        var rows = (await workspace.Profiles.GetRowStatsAsync(game.Id, workspace.Token)).Value!;

        Assert.Empty(rows.Single(r => r.ProfileId == first.Id).UnsavedChanges);
        Assert.Equal([RepoRole.Install], rows.Single(r => r.ProfileId == second.Id).UnsavedChanges);
    }

    [Fact]
    public async Task A_folder_that_is_gone_makes_the_profile_inactive_without_failing_the_list()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        Directory.Move(game.External!.Path, game.External.Path + "-gone");

        var row = await RowAsync(workspace, game, profile);

        Assert.False(row.IsActive);
        Assert.Equal(1, row.SnapshotCount);
    }

    [Fact]
    public async Task Mod_files_are_counted_at_the_profiles_latest_point()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(modHints: ["mods", "plugins.txt"]));
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "a.esp"), "a");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "deep", "b.esp"), "b");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "plugins.txt"), "list");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "modsnot", "c.esp"), "not a mod folder");
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "later.esp"), "not captured yet");

        var row = await RowAsync(workspace, game, profile);

        Assert.Equal(3, row.ModFileCount);
    }

    [Fact]
    public async Task The_detected_version_comes_from_the_exe_at_the_latest_point()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame();
        BackendWorkspace.WriteBytes(
            Path.Combine(game.Install.Path, "game.exe"),
            PeFixture.WithVersion(new Version(1, 6, 640, 0), null, PeStringTable.English(("ProductVersion", "1.6.640"))));
        await workspace.AddGameAsync(game);
        var (profile, _) = await workspace.CreateProfileAsync(game);

        var row = await RowAsync(workspace, game, profile);

        Assert.Equal("1.6.640", row.DetectedVersion);
        Assert.Null(row.VersionOverride);
    }

    [Fact]
    public async Task A_version_override_is_reported_next_to_the_detected_version()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame();
        BackendWorkspace.WriteBytes(
            Path.Combine(game.Install.Path, "game.exe"),
            PeFixture.WithVersion(new Version(2, 0, 0, 0), null, PeStringTable.English(("ProductVersion", "2.0"))));
        await workspace.AddGameAsync(game);
        var (profile, _) = await workspace.CreateProfileAsync(game);
        await workspace.Backend.Services.Catalog.SaveProfileAsync(profile with { VersionOverride = "2.0 (cracked)" }, workspace.Token);

        var row = await RowAsync(workspace, game, profile);

        Assert.Equal("2.0", row.DetectedVersion);
        Assert.Equal("2.0 (cracked)", row.VersionOverride);
    }

    [Fact]
    public async Task A_game_launched_by_uri_has_no_detected_version()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame() with { Launch = new LaunchConfig(LaunchMethod.Uri, Uri: "steam://rungameid/1") };
        await workspace.AddGameAsync(game);
        var (profile, _) = await workspace.CreateProfileAsync(game);

        Assert.Null((await RowAsync(workspace, game, profile)).DetectedVersion);
    }

    [Fact]
    public async Task The_version_of_an_unchanged_exe_is_read_once_for_all_profiles()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame();
        BackendWorkspace.WriteBytes(
            Path.Combine(game.Install.Path, "game.exe"),
            PeFixture.WithVersion(new Version(3, 1, 0, 0), null, PeStringTable.English(("ProductVersion", "3.1"))));
        await workspace.AddGameAsync(game);
        await workspace.CreateProfileAsync(game, "One");
        await workspace.CreateProfileAsync(game, "Two");

        var rows = (await workspace.Profiles.GetRowStatsAsync(game.Id, workspace.Token)).Value!;

        Assert.All(rows, row => Assert.Equal("3.1", row.DetectedVersion));
        Assert.Equal(1, workspace.Backend.Services.ExeVersions.Count);
    }

    [Fact]
    public async Task The_last_played_time_and_the_running_marker_come_from_the_catalog_and_the_sessions()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        var played = new DateTimeOffset(2026, 7, 8, 21, 30, 0, TimeSpan.Zero);
        await workspace.Backend.Services.Catalog.SaveProfileAsync(profile with { LastLaunchedAt = played }, workspace.Token);
        workspace.Sessions.Start(game.Id, profile.Id);

        var row = await RowAsync(workspace, game, profile);

        Assert.Equal(played, row.LastPlayedAt);
        Assert.True(row.IsRunning);
    }

    [Fact]
    public async Task Rows_follow_the_order_profiles_were_created_in()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (first, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        var (second, _) = await workspace.CreateProfileAsync(game, "Modded");

        var rows = (await workspace.Profiles.GetRowStatsAsync(game.Id, workspace.Token)).Value!;

        Assert.Equal([first.Id, second.Id], rows.Select(r => r.ProfileId));
    }

    [Fact]
    public async Task Row_stats_of_an_unknown_game_are_an_error()
    {
        using var workspace = new BackendWorkspace();

        Assert.False((await workspace.Profiles.GetRowStatsAsync("nope", workspace.Token)).Ok);
    }

    private static async Task<ProfileRowStats> RowAsync(BackendWorkspace workspace, GameDefinition game, Profile profile)
    {
        var rows = await workspace.Profiles.GetRowStatsAsync(game.Id, workspace.Token);
        Assert.True(rows.Ok, rows.Error?.Detail);
        return rows.Value.Single(row => row.ProfileId == profile.Id);
    }

    private static async Task CaptureAsync(BackendWorkspace workspace, GameDefinition game, Profile profile, string name, IReadOnlyCollection<RepoRole>? folders = null)
    {
        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, name, folders);
        await workspace.WaitForJobAsync(started.Value!.JobId);
    }
}
