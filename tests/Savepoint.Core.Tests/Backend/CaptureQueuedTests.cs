using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Services;

namespace Savepoint.Core.Tests.Backend;

// A save that waits for the game's lock acts on the game and profile as they are once it has the lock, not as they were when it was asked for (D31).
public class CaptureQueuedTests
{
    private static readonly TimeSpan _held = TimeSpan.FromSeconds(20);

    // The edit has to join the game's queue before the save is asked for. The queue shows no sign of a waiter, so a pause is the only way to order the two.
    private static readonly TimeSpan _queueSettle = TimeSpan.FromMilliseconds(300);

    // Holds the next storage optimization at its first free-space question, with the game's lock, until released.
    private sealed class HeldOptimization : IDisposable
    {
        private readonly ManualResetEventSlim _reached = new();
        private readonly ManualResetEventSlim _release = new();

        public HeldOptimization(BackendWorkspace workspace)
        {
            workspace.Disk.FreePerPath = path =>
            {
                if (path.StartsWith(workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase)
                    && !new System.Diagnostics.StackTrace().ToString().Contains("CaptureCoreAsync", StringComparison.Ordinal))
                {
                    _reached.Set();
                    _release.Wait(_held);
                }

                return null;
            };
        }

        public bool Reached(CancellationToken token) => _reached.Wait(_held, token);

        public void Release() => _release.Set();

        public void Dispose()
        {
            _release.Set();
            _reached.Dispose();
            _release.Dispose();
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task A_save_queued_behind_a_game_edit_uses_the_edited_ignore_patterns()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        using var held = new HeldOptimization(workspace);
        var optimizing = await workspace.Games.OptimizeStorageAsync(game.Id);
        Assert.True(optimizing.Ok, optimizing.Error?.Detail);
        Assert.True(held.Reached(workspace.Token), "the optimization never reached the engine");

        // The edit queues for the game before the save is asked for, so the save is asked for with the old patterns.
        var edit = workspace.Games.UpdateAsync(game with { IgnorePaths = ["*.log"] });
        await Task.Delay(_queueSettle, workspace.Token);
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "debug.log"), "noise");
        BackendWorkspace.WriteFile(Path.Combine(game.External.Path, "slot3.sav"), "a third save");
        var second = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "Third");
        Assert.True(second.Ok, second.Error?.Detail);
        held.Release();
        Assert.True((await edit).Ok);
        var job = await workspace.WaitForJobAsync(second.Value.JobId);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var recorded = await TreeAsync(workspace, game, RepoRole.External, profile);
        Assert.Contains("slot3.sav", recorded);
        Assert.DoesNotContain("debug.log", recorded);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_save_whose_profile_was_deleted_after_it_was_asked_for_fails_and_does_not_bring_the_branch_back()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (spare, _) = await workspace.CreateProfileAsync(game, "Spare");
        await workspace.CreateProfileAsync(game, "Active");
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        var deleted = await workspace.Profiles.DeleteAsync(game.Id, spare.Id);
        Assert.True(deleted.Ok, deleted.Error?.Detail);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));

        // The request was made while the profile still existed; the job runs once the delete has gone through.
        var capture = workspace.Backend.Services.Capture;
        var job = await workspace.WaitForJobAsync(capture.Start(game, spare, "Late", GameFolders.RolesOf(game), capture.NewEvent(), creating: false));

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.profile.notFound", job.Error?.MessageId);
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        foreach (var role in new[] { RepoRole.Install, RepoRole.External })
        {
            Assert.DoesNotContain(spare.BranchName, await storage.Engine.ListBranchesAsync(RepoOf(storage, game, role), workspace.Token));
        }
    }

    private static RepoRef RepoOf(StorageSnapshot storage, GameDefinition game, RepoRole role) =>
        new(storage.GitDirectory(game.Id, role), role == RepoRole.Install ? game.Install.Path : game.External!.Path, game.Id, role);

    private static async Task<IReadOnlyList<string>> TreeAsync(BackendWorkspace workspace, GameDefinition game, RepoRole role, Profile profile)
    {
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        return await storage.Engine.ListTreeFilesAsync(RepoOf(storage, game, role), profile.BranchName, workspace.Token);
    }
}
