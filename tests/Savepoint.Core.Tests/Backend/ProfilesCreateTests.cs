using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Backend;

public class ProfilesCreateTests
{
    [Fact]
    public async Task A_new_profile_is_saved_in_both_folders_as_one_capture_event()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var (profile, job) = await workspace.CreateProfileAsync(game, "Vanilla");

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(JobKind.Capture, job.Kind);
        Assert.Equal(game.Id, job.GameId);
        var result = Assert.IsType<CaptureResult>(job.Result);
        Assert.Equal(profile.Id, result.ProfileId);
        Assert.Equal([RepoRole.Install, RepoRole.External], result.Folders.Select(f => f.Role));
        Assert.All(result.Folders, folder => Assert.True(folder.Ok));
        Assert.Equal("Vanilla", profile.Name);
        Assert.Equal($"profile/{profile.Id}", profile.BranchName);

        var install = await CommitsAsync(workspace, game, RepoRole.Install, profile);
        var saves = await CommitsAsync(workspace, game, RepoRole.External, profile);
        var onlyInstall = Assert.Single(install);
        var onlySaves = Assert.Single(saves);
        Assert.Equal("Vanilla", onlyInstall.Subject);
        Assert.NotNull(onlyInstall.CaptureId);
        Assert.Equal(onlyInstall.CaptureId, onlySaves.CaptureId);
        Assert.Equal(result.PointId, new HistoryPointId(onlyInstall.CaptureId!));
    }

    [Fact]
    public async Task The_notes_are_kept_on_the_profile()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var started = await workspace.Profiles.CreateAsync(game.Id, "Modded", "my notes", "First save");
        await workspace.WaitForJobAsync(started.Value!.JobId);

        var profile = Assert.Single((await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!);
        Assert.Equal("my notes", profile.Notes);
    }

    [Fact]
    public async Task The_profile_row_appears_only_once_a_folder_has_been_saved()
    {
        var reached = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        workspace.Disk.FreePerPath = path =>
        {
            // The engine asks for free space on the storage volume just before it saves a folder.
            if (path.StartsWith(workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase))
            {
                reached.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            }

            return null;
        };

        var started = await workspace.Profiles.CreateAsync(game.Id, "Vanilla", null, "Vanilla");
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken), "the capture never started saving");
        var whileSaving = (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!;
        release.Set();
        await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Empty(whileSaving);
        Assert.Single((await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!);
    }

    [Fact]
    public async Task A_capture_that_saves_nothing_leaves_no_row_and_no_branch()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        workspace.Disk.FreePerPath = path => path.StartsWith(workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase) ? 1024 : null;

        var started = await workspace.Profiles.CreateAsync(game.Id, "Vanilla", null, "Vanilla");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.diskSpace.low", job.Error?.MessageId);
        Assert.All(Assert.IsType<CaptureResult>(job.Result).Folders, folder => Assert.False(folder.Ok));
        Assert.Empty((await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!);
        Assert.Empty(await BranchesAsync(workspace, game, RepoRole.Install));
        Assert.Empty(await BranchesAsync(workspace, game, RepoRole.External));
    }

    [Fact]
    public async Task A_cancelled_first_capture_leaves_no_row_and_ends_cancelled()
    {
        var reached = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        workspace.Disk.FreePerPath = path =>
        {
            if (path.StartsWith(workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase))
            {
                reached.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            }

            return null;
        };

        var started = await workspace.Profiles.CreateAsync(game.Id, "Vanilla", null, "Vanilla");
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        await workspace.Jobs.CancelAsync(started.Value!.JobId);
        release.Set();
        var job = await workspace.WaitForJobAsync(started.Value.JobId);

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Equal("error.capture.cancelled", job.Error?.MessageId);
        Assert.Empty((await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!);
    }

    [Fact]
    public async Task When_only_the_saves_fail_the_profile_exists_and_the_result_is_partial()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        Directory.Move(game.External!.Path, game.External.Path + "-gone");

        var started = await workspace.Profiles.CreateAsync(game.Id, "Vanilla", null, "Vanilla");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Partial, job.Status);
        var result = Assert.IsType<CaptureResult>(job.Result);
        Assert.True(result.Folders.Single(f => f.Role == RepoRole.Install).Ok);
        var failed = result.Folders.Single(f => f.Role == RepoRole.External);
        Assert.Equal("error.path.missing", failed.Error?.MessageId);
        var profile = Assert.Single((await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!);
        Assert.Equal(profile.Id, result.ProfileId);
        Assert.Empty(await BranchesAsync(workspace, game, RepoRole.External));
    }

    [Fact]
    public async Task Retrying_just_the_saves_completes_the_same_point()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var gone = game.External!.Path + "-gone";
        Directory.Move(game.External.Path, gone);
        var started = await workspace.Profiles.CreateAsync(game.Id, "Vanilla", null, "Vanilla");
        var partial = await workspace.WaitForJobAsync(started.Value!.JobId);
        var profileId = Assert.IsType<CaptureResult>(partial.Result).ProfileId;
        Directory.Move(gone, game.External.Path);

        var retry = await workspace.Profiles.CaptureAsync(game.Id, profileId, "Vanilla", [RepoRole.External]);
        var job = await workspace.WaitForJobAsync(retry.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var result = Assert.IsType<CaptureResult>(job.Result);
        Assert.Equal([RepoRole.External], result.Folders.Select(f => f.Role));
        var profile = (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!.Single();
        var install = Assert.Single(await CommitsAsync(workspace, game, RepoRole.Install, profile));
        var saves = Assert.Single(await CommitsAsync(workspace, game, RepoRole.External, profile));
        Assert.Equal(install.CaptureId, saves.CaptureId);
    }

    [Fact]
    public async Task A_second_profile_branches_from_what_is_on_disk_without_touching_the_first()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (first, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "extra.esp"), "a mod");

        var (second, job) = await workspace.CreateProfileAsync(game, "Modded");

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var firstFiles = await FilesAsync(workspace, game, RepoRole.Install, first);
        var secondFiles = await FilesAsync(workspace, game, RepoRole.Install, second);
        Assert.DoesNotContain("mods/extra.esp", firstFiles);
        Assert.Contains("mods/extra.esp", secondFiles);
    }

    [Fact]
    public async Task A_second_profile_of_an_unchanged_folder_still_exists_and_shares_the_point()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (first, _) = await workspace.CreateProfileAsync(game, "Vanilla");

        var (second, job) = await workspace.CreateProfileAsync(game, "Vanilla again");

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Null(Assert.IsType<CaptureResult>(job.Result).PointId);
        Assert.Equal(
            await FilesAsync(workspace, game, RepoRole.Install, first),
            await FilesAsync(workspace, game, RepoRole.Install, second));
    }

    [Fact]
    public async Task An_empty_saves_folder_still_gets_a_branch_so_the_profile_exists_in_both_folders()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        File.Delete(Path.Combine(game.External!.Path, "slot1.sav"));

        var (profile, job) = await workspace.CreateProfileAsync(game, "Vanilla");

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(await FilesAsync(workspace, game, RepoRole.External, profile));
        Assert.Contains(profile.BranchName, await BranchesAsync(workspace, game, RepoRole.External));
    }

    [Fact]
    public async Task Files_the_game_asks_to_ignore_are_not_saved()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(ignore: ["cache/", "*.log"]));
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "cache", "tmp.bin"), "cache");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "run.log"), "log");

        var (profile, _) = await workspace.CreateProfileAsync(game, "Vanilla");

        var files = await FilesAsync(workspace, game, RepoRole.Install, profile);
        Assert.Contains("game.exe", files);
        Assert.DoesNotContain("cache/tmp.bin", files);
        Assert.DoesNotContain("run.log", files);
    }

    [Fact]
    public async Task The_consent_marker_is_never_part_of_a_saved_point()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var (profile, _) = await workspace.CreateProfileAsync(game, "Vanilla");

        Assert.DoesNotContain(ConsentMarker.FileName, await FilesAsync(workspace, game, RepoRole.Install, profile));
    }

    [Fact]
    public async Task A_blank_profile_name_is_refused_before_a_job_starts()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var started = await workspace.Profiles.CreateAsync(game.Id, "   ", null, "Point");

        Assert.Equal("error.validation.invalidPayload", started.Error?.MessageId);
        Assert.Empty(workspace.Jobs.List());
    }

    [Fact]
    public async Task Creating_a_profile_for_an_unknown_game_is_an_error()
    {
        using var workspace = new BackendWorkspace();

        var started = await workspace.Profiles.CreateAsync("nope", "Vanilla", null, "Point");

        Assert.False(started.Ok);
    }

    [Fact]
    public async Task Profiles_survive_a_backend_restart()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game, "Vanilla");

        var restarted = workspace.Restart();

        var listed = (await restarted.Profiles.ListAsync(null, workspace.Token)).Value!;
        Assert.Equal([profile.Id], listed.Select(p => p.Id));
    }

    [Fact]
    public async Task Listing_by_game_leaves_out_other_games_profiles()
    {
        using var workspace = new BackendWorkspace();
        var alpha = await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        var beta = await workspace.AddGameAsync(workspace.NewGame("Beta"));
        await workspace.CreateProfileAsync(alpha, "A profile");
        await workspace.CreateProfileAsync(beta, "B profile");

        var listed = (await workspace.Profiles.ListAsync(beta.Id, workspace.Token)).Value!;

        Assert.Equal(["B profile"], listed.Select(p => p.Name));
        Assert.Equal(2, (await workspace.Profiles.ListAsync(null, workspace.Token)).Value!.Count);
    }

    private static async Task<IReadOnlyList<CommitInfo>> CommitsAsync(BackendWorkspace workspace, GameDefinition game, RepoRole role, Profile profile)
    {
        var (storage, repo) = await RepoAsync(workspace, game, role);
        return await storage.Engine.ListCommitsAsync(repo, profile.BranchName, null, workspace.Token);
    }

    private static async Task<IReadOnlyList<string>> BranchesAsync(BackendWorkspace workspace, GameDefinition game, RepoRole role)
    {
        var (storage, repo) = await RepoAsync(workspace, game, role);
        return await storage.Engine.ListBranchesAsync(repo, workspace.Token);
    }

    private static async Task<IReadOnlyList<string>> FilesAsync(BackendWorkspace workspace, GameDefinition game, RepoRole role, Profile profile)
    {
        var (storage, repo) = await RepoAsync(workspace, game, role);
        return await storage.Engine.ListTreeFilesAsync(repo, profile.BranchName, workspace.Token);
    }

    private static async Task<(StorageSnapshot Storage, RepoRef Repo)> RepoAsync(BackendWorkspace workspace, GameDefinition game, RepoRole role)
    {
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        var path = role == RepoRole.Install ? game.Install.Path : game.External!.Path;
        return (storage, new RepoRef(storage.GitDirectory(game.Id, role), path, game.Id, role));
    }
}
