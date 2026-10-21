using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

public class ProfilesCaptureTests
{
    [Fact]
    public async Task A_later_capture_adds_a_point_to_both_folders_under_one_capture_id()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.pak"), "patched");
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "second save");

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "After the patch");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var result = Assert.IsType<CaptureResult>(job.Result);
        Assert.Equal(profile.Id, result.ProfileId);
        var install = (await CommitsAsync(workspace, game, RepoRole.Install, profile)).First();
        var saves = (await CommitsAsync(workspace, game, RepoRole.External, profile)).First();
        Assert.Equal("After the patch", install.Subject);
        Assert.Equal(install.CaptureId, saves.CaptureId);
        Assert.Equal(new HistoryPointId(install.CaptureId!), result.PointId);
        Assert.Equal(2, (await CommitsAsync(workspace, game, RepoRole.Install, profile)).Count);
    }

    [Fact]
    public async Task A_capture_that_finds_nothing_new_succeeds_without_a_point()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "Nothing changed");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var result = Assert.IsType<CaptureResult>(job.Result);
        Assert.Null(result.PointId);
        Assert.All(result.Folders, folder => Assert.True(folder.Ok));
        Assert.Single(await CommitsAsync(workspace, game, RepoRole.Install, profile));
    }

    [Fact]
    public async Task One_folder_can_be_saved_on_its_own()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.pak"), "patched");
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "second save");

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "Saves only", [RepoRole.External]);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal([RepoRole.External], Assert.IsType<CaptureResult>(job.Result).Folders.Select(f => f.Role));
        Assert.Single(await CommitsAsync(workspace, game, RepoRole.Install, profile));
        Assert.Equal(2, (await CommitsAsync(workspace, game, RepoRole.External, profile)).Count);
    }

    [Fact]
    public async Task A_game_without_saves_ignores_a_request_for_the_saves_folder()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));
        var (profile, _) = await workspace.CreateProfileAsync(game);

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "Again", [RepoRole.External]);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Empty(Assert.IsType<CaptureResult>(job.Result).Folders);
    }

    [Fact]
    public async Task Capturing_a_profile_that_is_not_checked_out_reports_that_folder_as_failed()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (first, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        await workspace.CreateProfileAsync(game, "Modded");

        var started = await workspace.Profiles.CaptureAsync(game.Id, first.Id, "Into the wrong one");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.All(Assert.IsType<CaptureResult>(job.Result).Folders, folder => Assert.False(folder.Ok));
    }

    [Fact]
    public async Task A_profile_of_another_game_is_not_found()
    {
        using var workspace = new BackendWorkspace();
        var alpha = await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        var beta = await workspace.AddGameAsync(workspace.NewGame("Beta"));
        var (profile, _) = await workspace.CreateProfileAsync(alpha);

        var started = await workspace.Profiles.CaptureAsync(beta.Id, profile.Id, "Wrong game");

        Assert.False(started.Ok);
        Assert.DoesNotContain(workspace.Jobs.List(), job => job.GameId == beta.Id);
    }

    [Fact]
    public async Task A_capture_progress_report_names_the_files_it_saved()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.pak"), "patched");

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "With a patch");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Contains(job.RecentLines, line => line.Contains("patch.pak", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_captures_of_one_game_run_one_after_the_other()
    {
        var inside = 0;
        var overlapped = false;
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));
        var (profile, _) = await workspace.CreateProfileAsync(game);
        workspace.Disk.FreePerPath = path =>
        {
            if (path.StartsWith(workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase))
            {
                if (Interlocked.Increment(ref inside) > 1)
                {
                    overlapped = true;
                }

                Thread.Sleep(300);
                Interlocked.Decrement(ref inside);
            }

            return null;
        };
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "a.txt"), "a");

        var first = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "First");
        var second = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "Second");
        await workspace.WaitForJobAsync(first.Value!.JobId);
        await workspace.WaitForJobAsync(second.Value!.JobId);

        Assert.False(overlapped, "two captures of the same game were saving at once");
    }

    [Fact]
    public async Task A_capture_past_the_loose_object_threshold_queues_a_routine_optimization()
    {
        using var workspace = new BackendWorkspace(options => options with { LooseObjectThreshold = 1 });
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));
        var (profile, _) = await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync();
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "a.txt"), "a");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "b.txt"), "b");

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "More files");
        await workspace.WaitForJobAsync(started.Value!.JobId);
        await workspace.WaitForIdleAsync();

        var optimizations = workspace.Jobs.List().Where(job => job.Kind == JobKind.Compact && job.GameId == game.Id).ToList();
        Assert.NotEmpty(optimizations);
        Assert.All(optimizations, job => Assert.Equal(JobStatus.Succeeded, job.Status));
    }

    [Fact]
    public async Task A_capture_below_the_threshold_queues_nothing()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "a.txt"), "a");

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "A file");
        await workspace.WaitForJobAsync(started.Value!.JobId);
        await workspace.WaitForIdleAsync();

        Assert.DoesNotContain(workspace.Jobs.List(), job => job.Kind == JobKind.Compact);
    }

    // A folder inside the game that keeps its own history cannot be saved inside another one.
    private static GameDefinition WithNestedRepository(BackendWorkspace workspace)
    {
        var game = workspace.NewGame();
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "a.esp"), "a");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", ".git"), "gitdir: ../elsewhere");
        return game;
    }

    [Fact]
    public async Task A_game_folder_that_keeps_its_own_history_is_refused_by_the_preview_and_names_how_many()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(WithNestedRepository(workspace));

        var preview = await workspace.Games.GetCapturePreviewAsync(game.Id, workspace.Token);

        Assert.Equal("error.capture.nestedRepository", preview.Error?.MessageId);
        Assert.Equal(1, preview.Error!.Args["count"]);
    }

    [Fact]
    public async Task A_game_folder_that_keeps_its_own_history_fails_that_folders_save_and_leaves_the_other_one_saved()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(WithNestedRepository(workspace));

        var started = await workspace.Profiles.CreateAsync(game.Id, "Vanilla", null, "Start");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        var result = Assert.IsType<CaptureResult>(job.Result);
        Assert.Equal("error.capture.nestedRepository", result.Folders.Single(folder => folder.Role == RepoRole.Install).Error?.MessageId);
        Assert.True(result.Folders.Single(folder => folder.Role == RepoRole.External).Ok);
        Assert.NotEqual(JobStatus.Succeeded, job.Status);
    }

    private static async Task<IReadOnlyList<CommitInfo>> CommitsAsync(BackendWorkspace workspace, GameDefinition game, RepoRole role, Profile profile)
    {
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        var path = role == RepoRole.Install ? game.Install.Path : game.External!.Path;
        return await storage.Engine.ListCommitsAsync(new RepoRef(storage.GitDirectory(game.Id, role), path, game.Id, role), profile.BranchName, null, workspace.Token);
    }
}
