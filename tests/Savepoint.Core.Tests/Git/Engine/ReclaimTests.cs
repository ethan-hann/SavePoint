using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A routine optimization packs and never rewrites old packs; a reclaim also drops what no profile reaches any more.
[Collection(EngineCollections.B)]
public sealed class ReclaimTests : IDisposable
{
    private const long _floor = 100L * 1024 * 1024;

    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    // profile-1 with three points, and profile-2 with one more point holding a file only it has; the folder is on profile-1.
    private async Task<(RepoRef Repo, string Blob, string Commit)> TwoProfilesAsync()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        await _ws.Engine.CreateBranchAsync(repo, "profile-2", "profile-1", Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-2", ct: Ct);
        var unique = new byte[300_000];
        new Random(3).NextBytes(unique);
        _ws.Write(repo, "unique.bin", unique);
        var commit = (await _ws.CaptureAsync(repo, "profile-2", "Only here")).TipCommitId!;
        var blob = (await _ws.Engine.GetBlobIdAsync(repo, "profile-2", "unique.bin", Ct))!;
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-1", ct: Ct);
        return (repo, blob, commit);
    }

    [Fact]
    public async Task A_deleted_profiles_objects_stay_through_a_routine_run_and_go_with_a_reclaim()
    {
        var (repo, blob, commit) = await TwoProfilesAsync();
        await _ws.Engine.DeleteBranchAsync(repo, "profile-2", Ct);
        var before = await _ws.Engine.GetRepoSizeAsync(repo, Ct);

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        var keptByRoutine = (await _ws.ObjectExistsAsync(repo, blob), await _ws.ObjectExistsAsync(repo, commit));
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Equal((true, true), keptByRoutine);
        Assert.False(await _ws.ObjectExistsAsync(repo, blob), "the file only the deleted profile had is gone");
        Assert.False(await _ws.ObjectExistsAsync(repo, commit), "its point is gone");
        Assert.True(await _ws.Engine.GetRepoSizeAsync(repo, Ct) < before - 250_000);
    }

    [Fact]
    public async Task A_reclaim_keeps_every_point_a_profile_still_reaches()
    {
        var (repo, blob, commit) = await TwoProfilesAsync();
        var tips = (await _ws.TipAsync(repo, "profile-1"), await _ws.TipAsync(repo, "profile-2"));

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.True(await _ws.ObjectExistsAsync(repo, blob));
        Assert.True(await _ws.ObjectExistsAsync(repo, commit));
        Assert.Equal(tips, (await _ws.TipAsync(repo, "profile-1"), await _ws.TipAsync(repo, "profile-2")));
        Assert.Equal(3, await _ws.Engine.CountCommitsAsync(repo, "profile-1", Ct));
        Assert.Equal(4, await _ws.Engine.CountCommitsAsync(repo, "profile-2", Ct));
        Assert.Equal("three", System.Text.Encoding.UTF8.GetString((await _ws.Engine.ReadBlobAsync(repo, "profile-1", "a.txt", Ct))!));
        Assert.Equal(0, await _ws.Engine.CountLooseObjectsAsync(repo, Ct));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task A_reclaim_keeps_the_point_the_folder_is_on_even_when_no_profile_reaches_it()
    {
        var (repo, blob, commit) = await TwoProfilesAsync();
        await _ws.Engine.CheckoutCommitAsync(repo, commit, ct: Ct);
        await _ws.GitWriteAsync(repo, "update-ref", "-d", "refs/heads/profile-2");

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.True(await _ws.ObjectExistsAsync(repo, blob));
        Assert.True(await _ws.ObjectExistsAsync(repo, commit));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-1", ct: Ct);
        Assert.False(File.Exists(_ws.FileOf(repo, "unique.bin")));
    }

    [Fact]
    public async Task A_reclaim_drops_what_an_older_repository_kept_alive_in_its_reflogs()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();

        // Repositories made before reflogs were switched off have them.
        await _ws.GitWriteAsync(repo, "config", "--local", "core.logAllRefUpdates", "true");
        await _ws.Engine.CreateBranchAsync(repo, "profile-2", "profile-1", Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-2", ct: Ct);
        var unique = new byte[200_000];
        new Random(4).NextBytes(unique);
        _ws.Write(repo, "unique.bin", unique);
        await _ws.CaptureAsync(repo, "profile-2", "Only here");
        var blob = (await _ws.Engine.GetBlobIdAsync(repo, "profile-2", "unique.bin", Ct))!;
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-1", ct: Ct);
        await _ws.Engine.DeleteBranchAsync(repo, "profile-2", Ct);
        Assert.True(File.Exists(Path.Combine(repo.GitDir, "logs", "HEAD")), "the scenario needs a reflog that still names the deleted profile's point");

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.False(await _ws.ObjectExistsAsync(repo, blob));
    }

    [Fact]
    public async Task A_reclaim_expires_reflogs_then_packs_everything_then_prunes_and_never_detaches()
    {
        var (repo, _, _) = await TwoProfilesAsync();
        var from = _ws.Invocations.Count;

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        var steps = _ws.Invocations.Skip(from).Where(invocation => invocation.Arguments[0] is "reflog" or "repack" or "prune").ToArray();
        Assert.Equal(["reflog", "repack", "prune"], steps.Select(step => step.Arguments[0]));
        Assert.Equal(["reflog", "expire", "--expire=now", "--all"], steps[0].Arguments);
        Assert.Equal(["repack", "-q", "-a", "-d"], steps[1].Arguments);
        Assert.Equal(["prune", "--expire=now"], steps[2].Arguments);
        Assert.All(steps, step => Assert.False(step.ReadOnly));
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments.Any(argument => argument is "--auto" or "--detach" or "--no-detach"));
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments[0] is "gc" or "maintenance");
    }

    [Fact]
    public async Task A_routine_run_never_prunes_or_rewrites_everything()
    {
        var (repo, _, _) = await TwoProfilesAsync();
        var from = _ws.Invocations.Count;

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);

        var later = _ws.Invocations.Skip(from).ToArray();
        Assert.DoesNotContain(later, invocation => invocation.Arguments[0] is "prune" or "reflog");
        Assert.DoesNotContain(later.Where(invocation => invocation.Arguments[0] == "repack"), invocation => invocation.Arguments.Contains("-a"));
    }

    [Fact]
    public async Task A_reclaim_needs_room_for_the_floor_and_for_a_rewrite_of_the_store()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        var size = await _ws.Engine.GetRepoSizeAsync(repo, Ct);
        _ws.Space.Free = _floor + size - 1;

        var error = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct));

        Assert.Equal("error.diskSpace.low", error.MessageId);
        Assert.Equal(_floor + size, error.Args["requiredBytes"]);
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments[0] is "reflog" or "repack" or "prune");

        _ws.Space.Free = _floor + size;
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);
        Assert.Contains(_ws.Invocations, invocation => invocation.Arguments[0] == "prune");
    }

    [Fact]
    public async Task A_cancelled_reclaim_throws_the_compaction_error_and_a_repeat_finishes()
    {
        var (repo, _, _) = await TwoProfilesAsync();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "repack")
            {
                cancel.Cancel();
            }
        };

        var error = await Assert.ThrowsAsync<OperationCancelledByUserException>(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, cancel.Token));

        Assert.Equal("error.compaction.cancelled", error.MessageId);
        _ws.OnInvoke = null;
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);
        Assert.Equal(3, await _ws.CountAsync(repo, "profile-1"));
        Assert.Equal(4, await _ws.CountAsync(repo, "profile-2"));
    }

    [Fact]
    public async Task A_reclaim_refuses_while_this_engine_is_writing_to_the_repository()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        Exception? refused = null;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments.Contains("add") && refused is null)
            {
                refused = Record.Exception(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct).GetAwaiter().GetResult());
            }
        };

        await _ws.CaptureAsync(repo, "profile-1");

        Assert.IsType<GitOperationException>(refused);
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments[0] == "prune");
    }

    [Fact]
    public async Task A_reclaim_does_not_need_the_game_folder()
    {
        var (repo, _, _) = await TwoProfilesAsync();
        Directory.Move(repo.WorkTree, repo.WorkTree + "-moved");

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Equal(0, await _ws.Engine.CountLooseObjectsAsync(repo, Ct));
    }

    [Fact]
    public async Task A_reclaim_of_a_repository_with_nothing_in_it_does_nothing_and_does_not_fail()
    {
        var repo = await _ws.InitializedRepoAsync();

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Empty(await _ws.Engine.ListBranchesAsync(repo, Ct));
    }

    [Fact]
    public async Task A_stale_lock_does_not_stop_a_reclaim()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.GitDir, "index.lock"), string.Empty, Ct);

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.False(File.Exists(Path.Combine(repo.GitDir, "index.lock")));
    }

    [Fact]
    public async Task A_repository_that_does_not_exist_cannot_be_reclaimed()
    {
        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.RepackAsync(_ws.NewRepo(), OptimizationMode.Reclaim, Ct));
    }

    [Fact]
    public async Task The_points_a_move_forward_dropped_go_with_a_reclaim_and_not_before()
    {
        var (repo, points) = await _ws.OlderPointRepoAsync();
        var request = new CaptureRequest("profile-1", "Continued", "cap-move")
        {
            Mode = CaptureMode.MoveForward,
            ExpectedStartCommit = points[0],
        };
        await _ws.Engine.CaptureAsync(repo, request, ct: Ct);
        var droppedTip = points[2];

        var keptAfterRoutine = await RoutineThenCheckAsync(repo, droppedTip);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.True(keptAfterRoutine);
        Assert.False(await _ws.ObjectExistsAsync(repo, droppedTip));
    }

    private async Task<bool> RoutineThenCheckAsync(RepoRef repo, string commit)
    {
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        return await _ws.ObjectExistsAsync(repo, commit);
    }
}
