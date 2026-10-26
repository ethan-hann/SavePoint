using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Tests.Launch.Orchestration;

namespace Savepoint.Core.Tests.Backend;

// A read made without the game's lock never sets a damaged index aside (a snapshot or a switch might be writing it). The screens that read
// that way show a soft answer, and the next operation that holds the lock repairs the file.
public class IndexDamageInReadsTests
{
    private static string[] Backups(string gitDir) => Directory.GetFiles(gitDir, "index.damaged-*");

    private static async Task<string> InstallGitDirAsync(BackendWorkspace workspace, GameDefinition game) =>
        await workspace.Backend.Services.Storage.ReadAsync(storage => Task.FromResult(storage.GitDirectory(game.Id, RepoRole.Install)));

    private static void Damage(string gitDir) => File.WriteAllBytes(Path.Combine(gitDir, "index"), []);

    [Fact]
    public async Task The_profile_rows_are_still_listed_while_the_index_is_damaged()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        var gitDir = await InstallGitDirAsync(workspace, game);
        Damage(gitDir);

        var rows = await workspace.Profiles.GetRowStatsAsync(game.Id, workspace.Token);

        Assert.True(rows.Ok, rows.Error?.Detail);
        Assert.Single(rows.Value);
        Assert.Empty(Backups(gitDir));
        Assert.Equal(0, new FileInfo(Path.Combine(gitDir, "index")).Length);
    }

    [Fact]
    public async Task The_snapshot_preview_says_it_could_not_read_instead_of_repairing()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        var gitDir = await InstallGitDirAsync(workspace, game);
        Damage(gitDir);

        var preview = await workspace.Games.GetCapturePreviewAsync(game.Id, workspace.Token);

        Assert.False(preview.Ok);
        Assert.Equal("error.gitOperation.failed", preview.Error!.MessageId);
        Assert.Empty(Backups(gitDir));
    }

    [Fact]
    public async Task The_next_snapshot_repairs_the_index_the_reads_left()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        var gitDir = await InstallGitDirAsync(workspace, game);
        Damage(gitDir);
        Assert.False((await workspace.Games.GetCapturePreviewAsync(game.Id, workspace.Token)).Ok);

        var (_, job) = await workspace.CreateProfileAsync(game, "Second");

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Single(Backups(gitDir));
        Assert.True((await workspace.Games.GetCapturePreviewAsync(game.Id, workspace.Token)).Ok);
    }

    [Fact]
    public async Task A_launch_preview_repairs_a_damaged_index_under_the_lock_and_reads_again()
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using (rig)
        {
            var gitDir = await rig.WithEngineAsync((_, repo) => Task.FromResult(repo.GitDir), RepoRole.Install);
            Damage(gitDir);

            var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(modded.Id));

            Assert.True(preview.ShowBanner);
            Assert.Single(Backups(gitDir));
        }
    }
}
