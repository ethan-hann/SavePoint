using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Tests.Git.Engine;

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
    public async Task Folders_saved_side_by_side_that_each_fit_but_not_together_are_refused_before_any_is_written()
    {
        const long floor = 1_000;
        using var workspace = new BackendWorkspace(options => options with { Engine = new GitEngineOptions { MinFreeBytesForStorageWrites = floor } });
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.pak"), new string('a', 300_000));
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), new string('b', 200_000));
        workspace.Disk.FreePerPath = path => path.StartsWith(workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase) ? floor + 400_000 : null;

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "Too much together");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.diskSpace.low", job.Error?.MessageId);
        Assert.Equal(floor + 500_000, job.Error!.Args["requiredBytes"]);
        Assert.Equal(floor + 400_000, job.Error.Args["freeBytes"]);
        Assert.All(Assert.IsType<CaptureResult>(job.Result).Folders, folder => Assert.False(folder.Ok));
        Assert.Single(await CommitsAsync(workspace, game, RepoRole.Install, profile));
        Assert.Single(await CommitsAsync(workspace, game, RepoRole.External, profile));
    }

    [Fact]
    public async Task Folders_saved_side_by_side_that_fit_together_are_both_saved()
    {
        const long floor = 1_000;
        using var workspace = new BackendWorkspace(options => options with { Engine = new GitEngineOptions { MinFreeBytesForStorageWrites = floor } });
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.pak"), new string('a', 300_000));
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), new string('b', 200_000));
        workspace.Disk.FreePerPath = path => path.StartsWith(workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase) ? floor + 500_000 : null;

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "Just enough room");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(2, (await CommitsAsync(workspace, game, RepoRole.Install, profile)).Count);
        Assert.Equal(2, (await CommitsAsync(workspace, game, RepoRole.External, profile)).Count);
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
    public async Task Capturing_a_profile_that_is_not_checked_out_is_refused_with_its_own_error_and_starts_no_job()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (first, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        await workspace.CreateProfileAsync(game, "Modded");
        var jobs = workspace.Jobs.List().Count;

        var refused = await workspace.Profiles.CaptureAsync(game.Id, first.Id, "Into the wrong one");

        Assert.False(refused.Ok);
        Assert.Equal("error.profile.notLoaded", refused.Error.MessageId);
        Assert.Equal(jobs, workspace.Jobs.List().Count);
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

        Assert.Contains(job.RecentLines, line => line is JobLine.FilePath path && path.Path.Contains("patch.pak", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_captures_of_one_game_run_one_after_the_other()
    {
        var inside = 0;
        var held = 0;
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
                    Volatile.Write(ref overlapped, true);
                }

                // The first capture stays here until the second is queued behind it (or has got in beside it), which is when overlap would show.
                if (Interlocked.Exchange(ref held, 1) == 0)
                {
                    SpinWait.SpinUntil(() => workspace.Backend.Services.Locks.WaiterCount(game.Id) > 0 || Volatile.Read(ref overlapped), TimeSpan.FromSeconds(20));
                }

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

        var optimizations = workspace.Jobs.List().Where(job => job.Kind == JobKind.PackStorage && job.GameId == game.Id).ToList();
        Assert.NotEmpty(optimizations);
        Assert.All(optimizations, job => Assert.Equal(JobStatus.Succeeded, job.Status));
        Assert.DoesNotContain(workspace.Jobs.List(), job => job.Kind == JobKind.Compact);
    }

    [Fact]
    public async Task The_routine_optimization_says_in_its_details_that_it_packs_storage_not_that_it_frees_up_space()
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

        var steps = workspace.Jobs.List().Where(job => job.Kind == JobKind.PackStorage).SelectMany(job => job.RecentLines).OfType<JobLine.Step>().Select(step => step.MessageId).ToList();
        Assert.Contains(JobSteps.PackGame, steps);
        Assert.All(steps, id => Assert.StartsWith("activity.step.pack.", id, StringComparison.Ordinal));
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

        Assert.DoesNotContain(workspace.Jobs.List(), job => job.Kind is JobKind.Compact or JobKind.PackStorage);
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

    // A mod manager's link deployment leaves file links in the game folder, and Windows git would store them as text.
    private static GameDefinition WithFileLink(BackendWorkspace workspace)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "file links are only refused on Windows");
        var game = workspace.NewGame();
        var target = Path.Combine(workspace.Root, "mod-manager", "a.esp");
        BackendWorkspace.WriteFile(target, "a");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "b.esp"), "b");
        Assert.SkipUnless(TestLinks.TryCreateFileLink(Path.Combine(game.Install.Path, "mods", "a.esp"), target), "this system cannot make file links (needs Developer Mode or an elevated account)");
        return game;
    }

    [Fact]
    public async Task A_game_folder_holding_a_file_link_is_refused_by_the_preview_and_names_the_file()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(WithFileLink(workspace));

        var preview = await workspace.Games.GetCapturePreviewAsync(game.Id, workspace.Token);

        Assert.Equal("error.capture.linkedFile", preview.Error?.MessageId);
        Assert.Equal("a.esp", preview.Error!.Args["name"]);
    }

    [Fact]
    public async Task A_game_folder_holding_a_file_link_fails_that_folders_save_and_leaves_the_other_one_saved()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(WithFileLink(workspace));

        var started = await workspace.Profiles.CreateAsync(game.Id, "Vanilla", null, "Start");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        var result = Assert.IsType<CaptureResult>(job.Result);
        Assert.Equal("error.capture.linkedFile", result.Folders.Single(folder => folder.Role == RepoRole.Install).Error?.MessageId);
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
