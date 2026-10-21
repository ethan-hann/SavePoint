using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

public class ProfilesManageTests
{
    [Fact]
    public async Task Editing_a_profile_changes_only_what_the_user_edits()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        var played = new DateTimeOffset(2026, 7, 8, 21, 30, 0, TimeSpan.Zero);
        await workspace.Backend.Services.Catalog.SaveProfileAsync(profile with { LastLaunchedAt = played }, workspace.Token);

        var updated = await workspace.Profiles.UpdateAsync(game.Id, profile.Id, new ProfileChanges("  Vanilla plus ", "notes", "1.2 custom"));

        Assert.True(updated.Ok, updated.Error?.Detail);
        Assert.Equal("Vanilla plus", updated.Value.Name);
        Assert.Equal("notes", updated.Value.Notes);
        Assert.Equal("1.2 custom", updated.Value.VersionOverride);
        Assert.Equal(profile.BranchName, updated.Value.BranchName);
        Assert.Equal(played, updated.Value.LastLaunchedAt);
        var stored = Assert.Single((await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!);
        Assert.Equal(updated.Value, stored);
    }

    [Fact]
    public async Task A_blank_name_is_refused_when_editing()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);

        var updated = await workspace.Profiles.UpdateAsync(game.Id, profile.Id, new ProfileChanges(" ", null, null));

        Assert.Equal("error.validation.invalidPayload", updated.Error?.MessageId);
        Assert.Equal("Vanilla", (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!.Single().Name);
    }

    [Fact]
    public async Task Editing_a_profile_of_another_game_is_an_error()
    {
        using var workspace = new BackendWorkspace();
        var alpha = await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        var beta = await workspace.AddGameAsync(workspace.NewGame("Beta"));
        var (profile, _) = await workspace.CreateProfileAsync(alpha);

        Assert.False((await workspace.Profiles.UpdateAsync(beta.Id, profile.Id, new ProfileChanges("Hijack", null, null))).Ok);
    }

    [Fact]
    public async Task A_duplicate_starts_at_the_sources_install_point_and_with_fresh_saves()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (source, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        await workspace.Backend.Services.Catalog.SaveProfileAsync(source with { Notes = "keep me", VersionOverride = "9.9" }, workspace.Token);

        var duplicate = await workspace.Profiles.DuplicateAsync(game.Id, source.Id, "Vanilla copy");

        Assert.True(duplicate.Ok, duplicate.Error?.Detail);
        Assert.Equal("Vanilla copy", duplicate.Value.Name);
        Assert.NotEqual(source.Id, duplicate.Value.Id);
        Assert.Equal("keep me", duplicate.Value.Notes);
        Assert.Equal("9.9", duplicate.Value.VersionOverride);
        var (storage, install, saves) = await ReposAsync(workspace, game);
        Assert.Equal(
            await storage.Engine.GetBranchTipAsync(install, source.BranchName, workspace.Token),
            await storage.Engine.GetBranchTipAsync(install, duplicate.Value.BranchName, workspace.Token));
        Assert.Empty(await storage.Engine.ListTreeFilesAsync(saves, duplicate.Value.BranchName, workspace.Token));
        Assert.Contains(duplicate.Value, (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!);
    }

    [Fact]
    public async Task A_duplicate_shows_only_its_own_new_points_in_history()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (source, _) = await workspace.CreateProfileAsync(game, "Vanilla");

        var duplicate = (await workspace.Profiles.DuplicateAsync(game.Id, source.Id, "Vanilla copy")).Value!;
        var history = (await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!;

        Assert.Equal(["Vanilla"], history.Points.Select(p => p.Name));
        Assert.Equal(source.Id, history.Points[0].ProfileId);
        Assert.Empty((await workspace.History.GetAsync(game.Id, duplicate.Id, workspace.Token)).Value!.Points);
    }

    [Fact]
    public async Task A_duplicate_of_a_game_without_saves_only_branches_the_install_folder()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));
        var (source, _) = await workspace.CreateProfileAsync(game);

        var duplicate = await workspace.Profiles.DuplicateAsync(game.Id, source.Id, "Copy");

        Assert.True(duplicate.Ok, duplicate.Error?.Detail);
    }

    [Fact]
    public async Task A_duplicate_of_an_unknown_source_is_refused_and_leaves_no_row()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);

        var duplicate = await workspace.Profiles.DuplicateAsync(game.Id, "missing", "Copy");

        Assert.False(duplicate.Ok);
        Assert.Single((await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!);
    }

    [Fact]
    public async Task A_duplicate_waits_for_a_capture_in_flight_on_the_same_game()
    {
        var reached = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));
        var (source, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "a.txt"), "a");
        workspace.Disk.FreePerPath = path =>
        {
            if (path.StartsWith(workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase))
            {
                reached.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            }

            return null;
        };
        var capture = await workspace.Profiles.CaptureAsync(game.Id, source.Id, "Busy");
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        var duplicating = workspace.Profiles.DuplicateAsync(game.Id, source.Id, "Copy");
        await Task.Delay(300, workspace.Token);
        var waiting = !duplicating.IsCompleted;
        release.Set();
        await workspace.WaitForJobAsync(capture.Value!.JobId);

        Assert.True(waiting, "the duplicate did not wait for the lock");
        Assert.True((await duplicating).Ok);
    }

    [Fact]
    public async Task Deleting_a_profile_that_is_not_checked_out_removes_its_branches_and_row()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (first, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        var (second, _) = await workspace.CreateProfileAsync(game, "Modded");

        var deleted = await workspace.Profiles.DeleteAsync(game.Id, first.Id);

        Assert.True(deleted.Ok, deleted.Error?.Detail);
        Assert.Equal([second.Id], (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!.Select(p => p.Id));
        var (storage, install, saves) = await ReposAsync(workspace, game);
        Assert.DoesNotContain(first.BranchName, await storage.Engine.ListBranchesAsync(install, workspace.Token));
        Assert.DoesNotContain(first.BranchName, await storage.Engine.ListBranchesAsync(saves, workspace.Token));
        Assert.Contains(second.BranchName, await storage.Engine.ListBranchesAsync(install, workspace.Token));
    }

    [Fact]
    public async Task The_profile_the_folders_are_on_cannot_be_deleted_and_nothing_changes()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game, "Vanilla");
        var (second, _) = await workspace.CreateProfileAsync(game, "Modded");

        var deleted = await workspace.Profiles.DeleteAsync(game.Id, second.Id);

        Assert.Equal("error.gameService.profileActive", deleted.Error?.MessageId);
        Assert.Equal(second.Id, deleted.Error!.Args["profileId"]);
        Assert.Equal(2, (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!.Count);
    }

    [Fact]
    public async Task A_profile_checked_out_in_only_the_saves_folder_blocks_the_delete_before_the_install_branch_goes()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (first, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        await workspace.CreateProfileAsync(game, "Modded");
        var (storage, _, saves) = await ReposAsync(workspace, game);
        await storage.Engine.CheckoutBranchAsync(saves, first.BranchName, null, workspace.Token);

        var deleted = await workspace.Profiles.DeleteAsync(game.Id, first.Id);

        Assert.Equal("error.gameService.profileActive", deleted.Error?.MessageId);
        var (_, install, _) = await ReposAsync(workspace, game);
        Assert.Contains(first.BranchName, await storage.Engine.ListBranchesAsync(install, workspace.Token));
        Assert.Equal(2, (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!.Count);
    }

    [Fact]
    public async Task A_running_game_blocks_the_delete()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (first, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        await workspace.CreateProfileAsync(game, "Modded");
        workspace.Sessions.Start(game.Id, "something");

        var deleted = await workspace.Profiles.DeleteAsync(game.Id, first.Id);

        Assert.Equal("error.gameService.gameRunning", deleted.Error?.MessageId);
        Assert.Equal(game.Id, deleted.Error!.Args["gameId"]);
        Assert.Equal(2, (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!.Count);
    }

    [Fact]
    public async Task Deleting_queues_a_reclaim_that_gives_back_the_space_only_that_profile_used()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));
        var (light, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        var big = new byte[3_000_000];
        new Random(7).NextBytes(big);
        BackendWorkspace.WriteBytes(Path.Combine(game.Install.Path, "data", "big.bin"), big);
        var (heavy, _) = await workspace.CreateProfileAsync(game, "Heavy");
        await workspace.WaitForIdleAsync();
        var (storage, install, _) = await ReposAsync(workspace, game);
        await storage.Engine.CheckoutBranchAsync(install, light.BranchName, null, workspace.Token);
        var gitDir = Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git");
        var before = await Savepoint.Core.Services.Storage.DirectorySize.GetBytesAsync(gitDir, workspace.Token);

        var deleted = await workspace.Profiles.DeleteAsync(game.Id, heavy.Id);
        await workspace.WaitForIdleAsync();

        Assert.True(deleted.Ok, deleted.Error?.Detail);
        var reclaims = workspace.Jobs.List().Where(job => job.Kind == JobKind.Compact && job.GameId == game.Id).ToList();
        Assert.NotEmpty(reclaims);
        Assert.All(reclaims, job => Assert.Equal(JobStatus.Succeeded, job.Status));
        var after = await Savepoint.Core.Services.Storage.DirectorySize.GetBytesAsync(gitDir, workspace.Token);
        Assert.True(before - after > 2_000_000, $"expected the git directory to give back the 3 MB file, but it went from {before} to {after} bytes");
    }

    [Fact]
    public async Task The_delete_preview_counts_points_only_this_profile_holds_in_each_folder()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.pak"), "patched");
        await CaptureAsync(workspace, game, profile, "Patched", [RepoRole.Install]);
        await workspace.Profiles.DuplicateAsync(game.Id, profile.Id, "Copy");

        var preview = (await workspace.Profiles.GetDeletePreviewAsync(game.Id, profile.Id, workspace.Token)).Value!;

        Assert.Equal(profile.Id, preview.ProfileId);
        Assert.Equal("error.gameService.profileActive", preview.BlockedBy?.MessageId);
        var installLoss = preview.Folders.Single(f => f.Role == RepoRole.Install);
        var savesLoss = preview.Folders.Single(f => f.Role == RepoRole.External);

        // The copy continues from Vanilla's latest install point, so nothing of its install history goes with it; its saves start over.
        Assert.Equal(0, installLoss.PointCount);
        Assert.Equal(1, savesLoss.PointCount);
        Assert.Equal(1, preview.PointsOnlyHere);
    }

    [Fact]
    public async Task The_delete_preview_of_a_profile_nothing_else_reaches_counts_every_capture_event_once()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.pak"), "patched");
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "second");
        await CaptureAsync(workspace, game, profile, "Both changed");

        var preview = (await workspace.Profiles.GetDeletePreviewAsync(game.Id, profile.Id, workspace.Token)).Value!;

        Assert.Equal(2, preview.PointsOnlyHere);
        Assert.Equal(2, preview.Folders.Single(f => f.Role == RepoRole.Install).PointCount);
        Assert.Equal(2, preview.Folders.Single(f => f.Role == RepoRole.External).PointCount);
    }

    [Fact]
    public async Task The_delete_preview_says_why_a_delete_would_fail()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);

        var active = (await workspace.Profiles.GetDeletePreviewAsync(game.Id, profile.Id, workspace.Token)).Value!;
        workspace.Sessions.Start(game.Id, profile.Id);
        var running = (await workspace.Profiles.GetDeletePreviewAsync(game.Id, profile.Id, workspace.Token)).Value!;

        Assert.Equal("error.gameService.profileActive", active.BlockedBy?.MessageId);
        Assert.Equal("error.gameService.gameRunning", running.BlockedBy?.MessageId);
    }

    private static async Task CaptureAsync(BackendWorkspace workspace, GameDefinition game, Profile profile, string name, IReadOnlyCollection<RepoRole>? folders = null)
    {
        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, name, folders);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    private static async Task<(StorageSnapshot Storage, RepoRef Install, RepoRef Saves)> ReposAsync(BackendWorkspace workspace, GameDefinition game)
    {
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        return (
            storage,
            new RepoRef(storage.GitDirectory(game.Id, RepoRole.Install), game.Install.Path, game.Id, RepoRole.Install),
            new RepoRef(storage.GitDirectory(game.Id, RepoRole.External), game.External?.Path ?? game.Install.Path + "-saves", game.Id, RepoRole.External));
    }
}
