using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

// The profile table and History read three stores: a mods-only change is a point, the Mod files column counts the mods folder, and the
// loaded profile needs its mods folder on the branch and clean.
public class ModsHistoryTests
{
    private static async Task<(BackendWorkspace Workspace, GameDefinition Game, Profile Vanilla)> WorldAsync(IReadOnlyList<string>? hints = null)
    {
        var workspace = new BackendWorkspace();
        try
        {
            var game = await workspace.AddGameAsync(workspace.NewGameWithMods() with { ModPathHints = hints ?? ["data"] });
            var (vanilla, _) = await workspace.CreateProfileAsync(game, "Vanilla");
            return (workspace, game, vanilla);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static async Task CaptureAsync(BackendWorkspace workspace, GameDefinition game, Profile profile, string name, params RepoRole[] folders)
    {
        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, name, folders);
        Assert.True(started.Ok, started.Error?.Detail);
        Assert.Equal(JobStatus.Succeeded, (await workspace.WaitForJobAsync(started.Value.JobId)).Status);
    }

    private static async Task<IReadOnlyList<HistoryPoint>> PointsAsync(BackendWorkspace workspace, GameDefinition game) =>
        (await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points;

    private static async Task<ProfileRowStats> RowAsync(BackendWorkspace workspace, GameDefinition game, Profile profile) =>
        (await workspace.Profiles.GetRowStatsAsync(game.Id, workspace.Token)).Value!.Single(row => row.ProfileId == profile.Id);

    [Fact]
    public async Task A_change_only_the_mods_folder_recorded_is_a_version_and_mods_point_with_the_mod_files_it_changed()
    {
        var (workspace, game, vanilla) = await WorldAsync();
        using var _ = workspace;
        BackendWorkspace.WriteFile(Path.Combine(game.Mods!.Path, "modC", "info.xml"), "mod c");
        BackendWorkspace.WriteFile(Path.Combine(game.Mods.Path, "modA", "info.xml"), "mod a, changed");
        await CaptureAsync(workspace, game, vanilla, "Added a mod", RepoRole.Mods);

        var points = await PointsAsync(workspace, game);

        var modsOnly = points.Single(point => point.Name == "Added a mod");
        Assert.Equal(HistoryPointKind.VersionAndMods, modsOnly.Kind);
        Assert.Equal(2, modsOnly.ModFilesChanged);
        Assert.Equal(vanilla.Id, modsOnly.ProfileId);
        Assert.Equal(HistoryPointKind.VersionModsAndSave, points.Single(point => point.Name == "Vanilla").Kind);
    }

    [Fact]
    public async Task A_point_that_touches_only_the_saves_is_still_a_save_only_point_without_mod_files_changed()
    {
        var (workspace, game, vanilla) = await WorldAsync();
        using var _ = workspace;
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "second save");
        await CaptureAsync(workspace, game, vanilla, "More saves", RepoRole.External);

        var saveOnly = (await PointsAsync(workspace, game)).Single(point => point.Name == "More saves");

        Assert.Equal(HistoryPointKind.SaveOnly, saveOnly.Kind);
        Assert.Null(saveOnly.ModFilesChanged);
        Assert.False(saveOnly.InUse);
    }

    [Fact]
    public async Task A_point_is_in_use_only_while_the_install_and_the_mods_folder_are_both_at_its_commits()
    {
        var (workspace, game, vanilla) = await WorldAsync();
        using var _ = workspace;
        BackendWorkspace.WriteFile(Path.Combine(game.Mods!.Path, "modC", "info.xml"), "mod c");
        await CaptureAsync(workspace, game, vanilla, "Added a mod", RepoRole.Mods);

        var points = await PointsAsync(workspace, game);

        // The first point's mods commit is behind the folder now, though its install commit is still the install folder's.
        Assert.False(points.Single(point => point.Name == "Vanilla").InUse);
        Assert.True(points.Single(point => point.Name == "Added a mod").InUse);
    }

    [Fact]
    public async Task An_install_only_point_is_not_in_use_once_a_later_mods_change_leaves_the_mods_folder_ahead_of_it()
    {
        var (workspace, game, vanilla) = await WorldAsync();
        using var _ = workspace;
        await GitClock.NextSecondAsync(workspace.Token);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.bin"), "patched");
        await CaptureAsync(workspace, game, vanilla, "Patched", RepoRole.Install);
        await GitClock.NextSecondAsync(workspace.Token);
        BackendWorkspace.WriteFile(Path.Combine(game.Mods!.Path, "modC", "info.xml"), "mod c");
        await CaptureAsync(workspace, game, vanilla, "Added a mod", RepoRole.Mods);

        var points = await PointsAsync(workspace, game);

        // Going back to the install-only point would restore the first point's mods, so it is not what the folders hold.
        Assert.False(points.Single(point => point.Name == "Patched").InUse);
        Assert.False(points.Single(point => point.Name == "Vanilla").InUse);
        Assert.True(points.Single(point => point.Name == "Added a mod").InUse);
    }

    [Fact]
    public async Task A_mods_only_point_is_not_in_use_once_a_later_install_change_leaves_the_install_folder_ahead_of_it()
    {
        var (workspace, game, vanilla) = await WorldAsync();
        using var _ = workspace;
        await GitClock.NextSecondAsync(workspace.Token);
        BackendWorkspace.WriteFile(Path.Combine(game.Mods!.Path, "modC", "info.xml"), "mod c");
        await CaptureAsync(workspace, game, vanilla, "Added a mod", RepoRole.Mods);
        await GitClock.NextSecondAsync(workspace.Token);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.bin"), "patched");
        await CaptureAsync(workspace, game, vanilla, "Patched", RepoRole.Install);

        var points = await PointsAsync(workspace, game);

        Assert.False(points.Single(point => point.Name == "Added a mod").InUse);
        Assert.True(points.Single(point => point.Name == "Patched").InUse);
    }

    [Fact]
    public async Task A_point_with_every_folder_at_its_commits_is_in_use_and_its_saves_are_loaded()
    {
        var (workspace, _, _) = await WorldAsync();
        using var _ = workspace;

        var first = Assert.Single(await PointsAsync(workspace, (await workspace.Games.ListAsync(workspace.Token)).Value!.Single()));

        Assert.True(first.InUse);
        Assert.True(first.LoadedNow);
    }

    [Fact]
    public async Task The_row_counts_every_mods_file_beside_the_install_files_the_hints_name()
    {
        var (workspace, game, vanilla) = await WorldAsync();
        using var _ = workspace;

        var row = await RowAsync(workspace, game, vanilla);

        // data/base.pak is the one install file under the hint, and the mods folder holds two files.
        Assert.Equal(3, row.ModFileCount);
    }

    [Fact]
    public async Task A_game_that_names_no_hints_counts_just_its_mods_folder()
    {
        var (workspace, game, vanilla) = await WorldAsync(hints: []);
        using var _ = workspace;

        Assert.Equal(2, (await RowAsync(workspace, game, vanilla)).ModFileCount);
    }

    [Fact]
    public async Task The_loaded_profile_is_active_until_the_mods_folder_has_an_unsaved_change()
    {
        var (workspace, game, vanilla) = await WorldAsync();
        using var _ = workspace;
        var before = await RowAsync(workspace, game, vanilla);
        BackendWorkspace.WriteFile(Path.Combine(game.Mods!.Path, "modC", "info.xml"), "mod c, not saved");

        var after = await RowAsync(workspace, game, vanilla);

        Assert.True(before.IsActive);
        Assert.Empty(before.UnsavedChanges);
        Assert.False(after.IsActive);
        Assert.Equal([RepoRole.Mods], after.UnsavedChanges);
    }

    // A game that gained its mods folder after its profile's last snapshot, so no profile has a snapshot of that folder yet.
    private static async Task<(BackendWorkspace Workspace, GameDefinition Game, Profile Vanilla)> ModsAddedLaterAsync()
    {
        var workspace = new BackendWorkspace();
        try
        {
            var game = await workspace.AddGameAsync();
            var (vanilla, _) = await workspace.CreateProfileAsync(game, "Vanilla");
            var withMods = workspace.NewGameWithMods("Other") with { Id = game.Id, Name = game.Name, Install = game.Install, External = game.External, Launch = game.Launch };
            Assert.True((await workspace.Games.UpdateAsync(withMods)).Ok);
            return (workspace, withMods, vanilla);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    [Fact]
    public async Task A_mods_folder_added_after_the_profiles_last_snapshot_leaves_the_profile_active_and_offers_the_first_snapshot_of_it()
    {
        var (workspace, game, vanilla) = await ModsAddedLaterAsync();
        using var _ = workspace;

        var row = await RowAsync(workspace, game, vanilla);

        Assert.True(row.IsActive);
        Assert.Empty(row.UnsavedChanges);
        Assert.Equal(SnapshotNowAvailability.FirstSnapshot, row.SnapshotNow);
    }

    [Fact]
    public async Task A_mods_folder_added_later_does_not_stop_the_profiles_points_from_being_in_use()
    {
        var (workspace, game, _) = await ModsAddedLaterAsync();
        using var _ = workspace;

        var points = await PointsAsync(workspace, game);

        Assert.True(Assert.Single(points).InUse);
    }

    [Fact]
    public async Task A_mods_folder_added_later_is_part_of_the_profile_once_its_first_mods_snapshot_is_taken()
    {
        var (workspace, game, vanilla) = await ModsAddedLaterAsync();
        using var _ = workspace;
        await GitClock.NextSecondAsync(workspace.Token);
        await CaptureAsync(workspace, game, vanilla, "Mods in", RepoRole.Mods);
        BackendWorkspace.WriteFile(Path.Combine(game.Mods!.Path, "modC", "info.xml"), "mod c, not saved");

        var row = await RowAsync(workspace, game, vanilla);

        Assert.False(row.IsActive);
        Assert.Equal([RepoRole.Mods], row.UnsavedChanges);
    }

    [Fact]
    public async Task A_mods_folder_another_profile_has_taken_over_keeps_a_profile_with_no_mods_snapshot_from_being_active()
    {
        var (workspace, game, vanilla) = await ModsAddedLaterAsync();
        using var _ = workspace;
        await workspace.CreateProfileAsync(game, "Modded");
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        RepoRef Repo(RepoRole role) => new(storage.GitDirectory(game.Id, role), ModsWorld.FolderOf(game, role), game.Id, role);

        // The first profile is back in the game and saves folders, while the mods folder stays on the second profile's branch.
        await storage.Engine.CheckoutBranchAsync(Repo(RepoRole.Install), vanilla.BranchName, ct: workspace.Token);
        await storage.Engine.CheckoutBranchAsync(Repo(RepoRole.External), vanilla.BranchName, ct: workspace.Token);

        var row = await RowAsync(workspace, game, vanilla);

        Assert.False(row.IsActive);
    }

    [Fact]
    public async Task Duplicating_a_profile_copies_its_mods_and_starts_the_saves_over()
    {
        var (workspace, game, vanilla) = await WorldAsync();
        using var _ = workspace;

        var copy = await workspace.Profiles.DuplicateAsync(game.Id, vanilla.Id, "Copy");

        Assert.True(copy.Ok, copy.Error?.Detail);
        var modsOfCopy = await workspace.CommitsAsync(game, RepoRole.Mods, copy.Value);
        var modsOfSource = await workspace.CommitsAsync(game, RepoRole.Mods, vanilla);
        Assert.Equal(modsOfSource.Select(commit => commit.Id), modsOfCopy.Select(commit => commit.Id));
        Assert.Equal(["modA/info.xml", "modB/info.xml"], (await workspace.FilesAsync(game, RepoRole.Mods, copy.Value)).Order(StringComparer.Ordinal));
        Assert.Empty(await workspace.FilesAsync(game, RepoRole.External, copy.Value));
    }

    [Fact]
    public async Task Duplicating_a_profile_that_never_saved_the_mods_folder_leaves_the_copy_without_one_too()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (vanilla, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        var withMods = workspace.NewGameWithMods("Other") with { Id = game.Id, Name = game.Name, Install = game.Install, External = game.External, Launch = game.Launch };
        Assert.True((await workspace.Games.UpdateAsync(withMods)).Ok);

        var copy = await workspace.Profiles.DuplicateAsync(game.Id, vanilla.Id, "Copy");

        Assert.True(copy.Ok, copy.Error?.Detail);
        Assert.Empty(await workspace.CommitsAsync(withMods, RepoRole.Mods, copy.Value));
    }

    [Fact]
    public async Task The_delete_preview_counts_the_points_lost_in_each_of_the_three_folders()
    {
        var (workspace, game, vanilla) = await WorldAsync();
        using var _ = workspace;
        var (modded, _) = await workspace.CreateProfileAsync(game, "Modded");
        BackendWorkspace.WriteFile(Path.Combine(game.Mods!.Path, "modC", "info.xml"), "mod c");
        await CaptureAsync(workspace, game, modded, "Added a mod", RepoRole.Mods);

        var preview = (await workspace.Profiles.GetDeletePreviewAsync(game.Id, modded.Id, workspace.Token)).Value!;

        Assert.Equal([RepoRole.Install, RepoRole.Mods, RepoRole.External], preview.Folders.Select(folder => folder.Role));
        Assert.Equal(1, preview.Folders.Single(folder => folder.Role == RepoRole.Mods).PointCount);
        Assert.NotEqual(vanilla.Id, modded.Id);
    }
}
