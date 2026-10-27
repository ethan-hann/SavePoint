using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Backend;

// A snapshot of a game with a mods folder writes a point in each of the three stores under one capture id.
public class ModsCaptureTests
{
    [Fact]
    public async Task A_new_profile_saves_the_mods_folder_beside_the_install_and_the_saves()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGameWithMods());

        var (profile, job) = await workspace.CreateProfileAsync(game);

        var result = Assert.IsType<CaptureResult>(job.Result);
        Assert.Equal([RepoRole.Install, RepoRole.External, RepoRole.Mods], result.Folders.Select(folder => folder.Role).Order());
        Assert.All(result.Folders, folder => Assert.True(folder.Ok));
        Assert.Equal(["modA/info.xml", "modB/info.xml"], (await workspace.FilesAsync(game, RepoRole.Mods, profile)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_later_capture_adds_a_point_to_all_three_folders_under_one_capture_id()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGameWithMods());
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.pak"), "patched");
        BackendWorkspace.WriteFile(Path.Combine(game.Mods!.Path, "modC", "info.xml"), "mod c");
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "second save");

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "After the patch");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var install = (await workspace.CommitsAsync(game, RepoRole.Install, profile)).First();
        var mods = (await workspace.CommitsAsync(game, RepoRole.Mods, profile)).First();
        var saves = (await workspace.CommitsAsync(game, RepoRole.External, profile)).First();
        Assert.Equal("After the patch", mods.Subject);
        Assert.Equal(install.CaptureId, mods.CaptureId);
        Assert.Equal(install.CaptureId, saves.CaptureId);
        Assert.Equal(new HistoryPointId(install.CaptureId!), Assert.IsType<CaptureResult>(job.Result).PointId);
        Assert.Contains("modC/info.xml", await workspace.FilesAsync(game, RepoRole.Mods, profile));
    }

    [Fact]
    public async Task The_mods_folder_can_be_saved_on_its_own()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGameWithMods());
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.pak"), "patched");
        BackendWorkspace.WriteFile(Path.Combine(game.Mods!.Path, "modC", "info.xml"), "mod c");
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "second save");

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "Mods only", [RepoRole.Mods]);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(["Mods only", "Vanilla"], (await workspace.CommitsAsync(game, RepoRole.Mods, profile)).Select(commit => commit.Subject));
        Assert.Single(await workspace.CommitsAsync(game, RepoRole.Install, profile));
        Assert.Single(await workspace.CommitsAsync(game, RepoRole.External, profile));
        Assert.Equal([RepoRole.Mods], Assert.IsType<CaptureResult>(job.Result).Folders.Select(folder => folder.Role));
    }

    [Fact]
    public async Task When_only_the_mods_folder_fails_the_profile_exists_and_the_result_is_partial_with_the_mods_outcome()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGameWithMods());
        Directory.Move(game.Mods!.Path, game.Mods.Path + "-gone");

        var started = await workspace.Profiles.CreateAsync(game.Id, "Vanilla", null, "Vanilla");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Partial, job.Status);
        var result = Assert.IsType<CaptureResult>(job.Result);
        Assert.True(result.Folders.Single(folder => folder.Role == RepoRole.Install).Ok);
        Assert.True(result.Folders.Single(folder => folder.Role == RepoRole.External).Ok);
        var failed = result.Folders.Single(folder => folder.Role == RepoRole.Mods);
        Assert.Equal("error.path.missing", failed.Error?.MessageId);
        Assert.Equal(game.Mods.Path, failed.Error!.Args["path"]);
        Assert.Single((await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!);
    }

    [Fact]
    public async Task Retrying_just_the_mods_folder_completes_the_same_point()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGameWithMods());
        Directory.Move(game.Mods!.Path, game.Mods.Path + "-gone");
        var started = await workspace.Profiles.CreateAsync(game.Id, "Vanilla", null, "Vanilla");
        var partial = await workspace.WaitForJobAsync(started.Value!.JobId);
        var profileId = Assert.IsType<CaptureResult>(partial.Result).ProfileId;
        var firstPoint = Assert.IsType<CaptureResult>(partial.Result).PointId;
        Directory.Move(game.Mods.Path + "-gone", game.Mods.Path);

        var retry = await workspace.Profiles.CaptureAsync(game.Id, profileId, "Vanilla", [RepoRole.Mods]);
        var job = await workspace.WaitForJobAsync(retry.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var profile = (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!.Single();
        var install = Assert.Single(await workspace.CommitsAsync(game, RepoRole.Install, profile));
        var mods = Assert.Single(await workspace.CommitsAsync(game, RepoRole.Mods, profile));
        Assert.Equal(install.CaptureId, mods.CaptureId);
        Assert.Equal(firstPoint, Assert.IsType<CaptureResult>(job.Result).PointId);
    }

    [Fact]
    public async Task The_preview_lists_the_mods_folder_and_counts_every_file_in_it_as_a_mod_file()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGameWithMods());

        var preview = (await workspace.Games.GetCapturePreviewAsync(game.Id, workspace.Token)).Value!;

        var mods = preview.Folders.Single(folder => folder.Role == RepoRole.Mods);
        Assert.Equal(game.Mods!.Path, mods.Path);
        Assert.Equal(2, mods.FileCount);
        Assert.Equal(2, mods.ModFileCount);
        Assert.Equal(preview.Folders.Sum(folder => folder.Bytes), preview.NewBytesEstimate);
    }

    [Fact]
    public async Task The_mods_folder_leaves_out_the_game_wide_rules_but_not_the_install_only_ones()
    {
        using var workspace = new BackendWorkspace();
        var definition = workspace.NewGameWithMods() with { IgnorePaths = ["**/*.tmp"], InstallIgnorePaths = ["**/*.log"] };
        var game = await workspace.AddGameAsync(definition);
        BackendWorkspace.WriteFile(Path.Combine(game.Mods!.Path, "modA", "cache.tmp"), "scratch");
        BackendWorkspace.WriteFile(Path.Combine(game.Mods.Path, "modA", "run.log"), "a log a mod wrote");

        var (profile, _) = await workspace.CreateProfileAsync(game);

        var files = await workspace.FilesAsync(game, RepoRole.Mods, profile);
        Assert.DoesNotContain("modA/cache.tmp", files);
        Assert.Contains("modA/run.log", files);
    }
}
