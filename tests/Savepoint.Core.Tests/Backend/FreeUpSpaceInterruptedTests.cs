using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Tests.Backend;

// A free-up deletes the records of one game in two repositories, one ref transaction each. A SavePoint that stops between them (a kill, a
// power cut, a disk that stops answering, a put-back that fails too) must not leave half a snapshot listed. The run writes down what it is
// about to delete before the first record goes; the listing treats that as gone, and the next start (or the next free-up) finishes it.
public class FreeUpSpaceInterruptedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Where the stop lands: 1 = the note is written and no record has gone, 2 = the install folder's records are gone, 3 = both folders'
    // records are gone and the note was not yet removed.
    [Theory(Timeout = 240_000)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task A_stop_at_any_step_of_the_clear_is_finished_by_the_next_start_and_never_shows_half_a_snapshot(int step)
    {
        using var world = await RemovedWorld.AfterContinueAsync(new GitHooks());
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;
        var gameDirectory = await GameDirectoryAsync(world);
        await StopAfterAsync(world, preview.Token, step);

        // What the note names is gone as far as the listing and the preview go, whatever the folders still hold.
        Assert.Empty(await world.ListAsync());
        Assert.Equal(0, (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!.RecentlyRemovedSnapshots);

        world.Workspace.Restart();
        var status = await world.Workspace.AppStatus.CheckAsync(Ct);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.False(File.Exists(FreeUpIntent.PathIn(gameDirectory)));
        Assert.Empty(await world.RecordedCommitsAsync(RepoRole.Install));
        Assert.Empty(await world.RecordedCommitsAsync(RepoRole.External));
        Assert.Empty(await world.ListAsync());
    }

    [Fact(Timeout = 240_000)]
    public async Task A_run_that_cannot_clear_the_second_folder_or_put_the_first_back_is_finished_by_the_next_start()
    {
        var hooks = new GitHooks();
        using var world = await RemovedWorld.AfterContinueAsync(hooks);
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;
        var gameDirectory = await GameDirectoryAsync(world);

        // From the saves folder's clear on, nothing more can be written, which is what a kill at that moment leaves behind.
        hooks.OnGit = invocation =>
        {
            if (invocation.Arguments[0] == "update-ref" && (invocation.Arguments.Contains("savepoint: free up space undone") || (invocation.Arguments.Contains("savepoint: free up space") && IsFolder(invocation, "external.git"))))
            {
                throw new IOException("The machine lost power.");
            }
        };
        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, preview.Token);
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.True(File.Exists(FreeUpIntent.PathIn(gameDirectory)));
        Assert.Empty(await world.ListAsync());

        hooks.OnGit = null;
        world.Workspace.Restart();
        AssertOk(await world.Workspace.AppStatus.CheckAsync(Ct));

        Assert.False(File.Exists(FreeUpIntent.PathIn(gameDirectory)));
        Assert.Empty(await world.RecordedCommitsAsync(RepoRole.Install));
        Assert.Empty(await world.RecordedCommitsAsync(RepoRole.External));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_run_that_cannot_clear_the_first_folder_changes_nothing_and_leaves_no_note()
    {
        var hooks = new GitHooks();
        using var world = await RemovedWorld.AfterContinueAsync(hooks);
        var before = await world.ListAsync();
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;
        var gameDirectory = await GameDirectoryAsync(world);
        var noteWhenTheFirstFolderFailed = false;
        hooks.OnGit = invocation =>
        {
            if (invocation.Arguments[0] == "update-ref" && invocation.Arguments.Contains("savepoint: free up space") && IsFolder(invocation, "install.git"))
            {
                noteWhenTheFirstFolderFailed = File.Exists(FreeUpIntent.PathIn(gameDirectory));
                throw new IOException("The disk reported an error.");
            }
        };

        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, preview.Token);
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.True(noteWhenTheFirstFolderFailed);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.False(File.Exists(FreeUpIntent.PathIn(gameDirectory)));
        Assert.Equal(before.Select(snapshot => (snapshot.Id, snapshot.Kind)), (await world.ListAsync()).Select(snapshot => (snapshot.Id, snapshot.Kind)));
    }

    [Fact(Timeout = 240_000)]
    public async Task The_next_free_up_finishes_a_clear_that_was_cut_off_even_when_it_has_nothing_of_its_own_to_delete()
    {
        using var world = await RemovedWorld.AfterContinueAsync(new GitHooks());
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;
        var gameDirectory = await GameDirectoryAsync(world);
        await StopAfterAsync(world, preview.Token, 2);

        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id);
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.False(File.Exists(FreeUpIntent.PathIn(gameDirectory)));
        Assert.Empty(await world.RecordedCommitsAsync(RepoRole.Install));
        Assert.Empty(await world.RecordedCommitsAsync(RepoRole.External));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_start_that_cannot_finish_the_clear_still_opens_and_keeps_the_note_for_the_next_try()
    {
        var hooks = new GitHooks();
        using var world = await RemovedWorld.AfterContinueAsync(hooks);
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;
        var gameDirectory = await GameDirectoryAsync(world);
        await StopAfterAsync(world, preview.Token, 1);
        hooks.OnGit = invocation =>
        {
            if (invocation.Arguments[0] == "update-ref" && invocation.Arguments.Contains("savepoint: free up space"))
            {
                throw new IOException("The disk reported an error.");
            }
        };
        world.Workspace.Restart();

        var status = await world.Workspace.AppStatus.CheckAsync(Ct);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(File.Exists(FreeUpIntent.PathIn(gameDirectory)));

        hooks.OnGit = null;
        world.Workspace.Restart();
        AssertOk(await world.Workspace.AppStatus.CheckAsync(Ct));

        Assert.False(File.Exists(FreeUpIntent.PathIn(gameDirectory)));
        Assert.Empty(await world.RecordedCommitsAsync(RepoRole.Install));
    }

    // The folders as a run leaves them when it stops at that step, made with the engine's own calls: the note first, then the folders in turn.
    private static async Task StopAfterAsync(RemovedWorld world, FreeUpSpaceToken token, int step)
    {
        var storage = await world.Workspace.Backend.Services.Storage.GetAsync(Ct);
        var plans = new Dictionary<RepoRole, IReadOnlyList<ClearedRef>>();
        foreach (var role in new[] { RepoRole.Install, RepoRole.External })
        {
            var names = token.Records.Where(record => record.Folder == role).Select(record => record.RefName).ToList();
            plans[role] = await storage.Engine.FindClearableAsync(world.RepoOf(storage, role), names, Ct);
        }

        FreeUpIntent.Write(storage.GameDirectory(world.Game.Id), plans);
        if (step >= 2)
        {
            await storage.Engine.ClearExactAsync(world.RepoOf(storage, RepoRole.Install), plans[RepoRole.Install], Ct);
        }

        if (step >= 3)
        {
            await storage.Engine.ClearExactAsync(world.RepoOf(storage, RepoRole.External), plans[RepoRole.External], Ct);
        }
    }

    private static async Task<string> GameDirectoryAsync(RemovedWorld world) =>
        (await world.Workspace.Backend.Services.Storage.GetAsync(Ct)).GameDirectory(world.Game.Id);

    private static void AssertOk(OperationResult<StartupStatus> status) => Assert.True(status.Ok, status.Error?.Detail);

    private static bool IsFolder(GitInvocation invocation, string gitDirectoryName) =>
        invocation.GitDir is { } gitDir && string.Equals(Path.GetFileName(gitDir), gitDirectoryName, StringComparison.Ordinal);
}
