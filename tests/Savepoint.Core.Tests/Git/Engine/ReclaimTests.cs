using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A routine optimization packs and never rewrites old packs; a reclaim also drops what no profile reaches any more.
// Every git process costs most of a second on a machine with a virus scanner, so each test starts from a repository state
// built once per run and asserts several facets of one outcome.
[Collection(EngineCollections.B)]
public sealed class ReclaimTests : IDisposable
{
    private const long _floor = 100L * 1024 * 1024;

    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    // profile-1 with three points (the folder is on it, with nothing unsaved) and profile-2 with a fourth that holds a file only
    // it has. Built with git's own commands: a reclaim cares about the stored state, and each engine call costs several processes.
    private static async Task<(RepoRef Repo, string Blob, string Commit)> TwoProfilesAsync(EngineWorkspace ws)
    {
        var (repo, (blob, commit)) = await RepoTemplates.CopyAsync("reclaim-two-profiles", ws, async builder =>
        {
            var (template, _) = await builder.ThreePointsRepoAsync();
            var unique = new byte[300_000];
            new Random(3).NextBytes(unique);

            var blobId = await GitOutAsync(builder, template, new GitInvocation(["hash-object", "-w", "--stdin"]) { StdinBytes = unique });
            var listing = await GitOutAsync(builder, template, new GitInvocation(["ls-tree", "refs/heads/profile-1"]));
            var tree = await GitOutAsync(builder, template, new GitInvocation(["mktree"]) { Stdin = listing + $"\n100644 blob {blobId}\tunique.bin\n" });
            var tip = (await builder.TipAsync(template, "profile-1"))!;
            var commitId = await GitOutAsync(builder, template, new GitInvocation(["commit-tree", tree, "-p", tip, "-m", "Only here"]));
            await builder.GitWriteAsync(template, "update-ref", "refs/heads/profile-2", commitId);
            return (template, (blobId, commitId));
        });
        return (repo, blob, commit);
    }

    // The folder sits on profile-2's point and profile-2 is gone: only HEAD reaches that point and its file.
    private async Task<(RepoRef Repo, string Blob, string Commit)> OnAPointOnlyHeadReachesAsync()
    {
        var (repo, (blob, commit)) = await RepoTemplates.CopyAsync("reclaim-head-only", _ws, async builder =>
        {
            var (template, blobId, commitId) = await TwoProfilesAsync(builder);
            await builder.GitWriteAsync(template, "checkout", "--quiet", "--detach", commitId);
            await builder.GitWriteAsync(template, "update-ref", "-d", "refs/heads/profile-2");
            return (template, (blobId, commitId));
        });
        return (repo, blob, commit);
    }

    private static async Task<string> GitOutAsync(EngineWorkspace ws, RepoRef repo, GitInvocation invocation) =>
        (await ws.Runner.RunAsync(invocation with { GitDir = repo.GitDir, WorkTree = repo.WorkTree }, Ct)).StandardOutput.Trim();

    // Read from the disk: the engine's own size and loose-object counts are covered by MaintenanceTests, and each call to git costs a process.
    private static long StoreBytes(RepoRef repo) =>
        Directory.EnumerateFiles(Path.Combine(repo.GitDir, "objects"), "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);

    private static int LooseObjectFiles(RepoRef repo) =>
        Directory.EnumerateDirectories(Path.Combine(repo.GitDir, "objects")).Where(folder => Path.GetFileName(folder).Length == 2).Sum(folder => Directory.GetFiles(folder).Length);

    // A repository made before reflogs were switched off has them, and every entry keeps its point alive.
    private static void PlantReflogNaming(RepoRef repo, string commit)
    {
        var log = Path.Combine(repo.GitDir, "logs", "HEAD");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        File.WriteAllText(log, $"{new string('0', 40)} {commit} SavePoint <savepoint@localhost> 1700000000 +0000\tcheckout: moving to profile-2\n");
    }

    [Fact]
    public async Task A_deleted_profiles_objects_stay_through_a_routine_run_and_go_with_a_reclaim_even_when_a_reflog_names_them()
    {
        var (repo, blob, commit) = await TwoProfilesAsync(_ws);
        PlantReflogNaming(repo, commit);
        await _ws.Engine.DeleteBranchAsync(repo, "profile-2", Ct);
        Assert.True(File.Exists(Path.Combine(repo.GitDir, "logs", "HEAD")), "the scenario needs a reflog that still names the deleted profile's point");
        var before = StoreBytes(repo);
        var from = _ws.Invocations.Count;

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        var routine = _ws.Invocations.Skip(from).ToArray();
        var keptByRoutine = await _ws.ObjectsExistAsync(repo, blob, commit);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.DoesNotContain(routine, invocation => invocation.Arguments[0] is "prune" or "reflog");
        Assert.DoesNotContain(routine.Where(invocation => invocation.Arguments[0] == "repack"), invocation => invocation.Arguments.Contains("-a"));
        Assert.Equal(new[] { true, true }, keptByRoutine);
        Assert.Equal(new[] { false, false }, await _ws.ObjectsExistAsync(repo, blob, commit));
        Assert.True(StoreBytes(repo) < before - 250_000);
    }

    [Fact]
    public async Task A_reclaim_that_is_refused_for_space_or_cancelled_changes_nothing_and_a_repeat_keeps_every_point_a_profile_reaches()
    {
        var (repo, blob, commit) = await TwoProfilesAsync(_ws);
        var tips = (await _ws.TipAsync(repo, "profile-1"), await _ws.TipAsync(repo, "profile-2"));
        var size = await _ws.Engine.GetRepoSizeAsync(repo, Ct);

        // It needs the floor and room for a rewrite of the store.
        _ws.Space.Free = _floor + size - 1;
        var refused = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct));
        Assert.Equal("error.diskSpace.low", refused.MessageId);
        Assert.Equal(_floor + size, refused.Args["requiredBytes"]);
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments[0] is "reflog" or "repack" or "prune");

        _ws.Space.Free = _floor + size;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "repack")
            {
                cancel.Cancel();
            }
        };
        var cancelled = await Assert.ThrowsAsync<OperationCancelledByUserException>(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, cancel.Token));
        Assert.Equal("error.compaction.cancelled", cancelled.MessageId);
        _ws.OnInvoke = null;

        // The repeat needs neither the game folder nor a clean git directory.
        var moved = repo.WorkTree + "-moved";
        Directory.Move(repo.WorkTree, moved);
        await File.WriteAllTextAsync(Path.Combine(repo.GitDir, "index.lock"), string.Empty, Ct);
        var from = _ws.Invocations.Count;
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);
        Directory.Move(moved, repo.WorkTree);

        var steps = _ws.Invocations.Skip(from).Where(invocation => invocation.Arguments[0] is "reflog" or "repack" or "prune").ToArray();
        Assert.Equal(["reflog", "repack", "prune"], steps.Select(step => step.Arguments[0]));
        Assert.Equal(["reflog", "expire", "--expire=now", "--all"], steps[0].Arguments);
        Assert.Equal(["repack", "-q", "-a", "-d"], steps[1].Arguments);
        Assert.Equal(["prune", "--expire=now"], steps[2].Arguments);
        Assert.All(steps, step => Assert.False(step.ReadOnly));
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments.Any(argument => argument is "--auto" or "--detach" or "--no-detach"));
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments[0] is "gc" or "maintenance");
        Assert.False(File.Exists(Path.Combine(repo.GitDir, "index.lock")));
        Assert.Equal(new[] { true, true }, await _ws.ObjectsExistAsync(repo, blob, commit));
        Assert.Equal(tips, (await _ws.TipAsync(repo, "profile-1"), await _ws.TipAsync(repo, "profile-2")));
        Assert.Equal(3, await _ws.CountAsync(repo, "profile-1"));
        Assert.Equal(4, await _ws.CountAsync(repo, "profile-2"));
        Assert.Equal("three", System.Text.Encoding.UTF8.GetString((await _ws.Engine.ReadBlobAsync(repo, "profile-1", "a.txt", Ct))!));
        Assert.Equal(0, LooseObjectFiles(repo));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task A_reclaim_keeps_the_point_the_folder_is_on_even_when_no_profile_reaches_it()
    {
        var (repo, blob, commit) = await OnAPointOnlyHeadReachesAsync();

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Equal(new[] { true, true }, await _ws.ObjectsExistAsync(repo, blob, commit));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-1", ct: Ct);
        Assert.False(File.Exists(_ws.FileOf(repo, "unique.bin")));
    }

    [Fact]
    public async Task A_reclaim_refuses_while_this_engine_is_writing_to_the_repository()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        Exception? refused = null;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "update-ref" && refused is null)
            {
                refused = Record.Exception(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct).GetAwaiter().GetResult());
            }
        };

        await _ws.Engine.CreateBranchAsync(repo, "profile-2", "profile-1", Ct);

        Assert.IsType<GitOperationException>(refused);
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments[0] == "prune");
    }

    [Fact]
    public async Task A_reclaim_of_a_repository_with_nothing_in_it_does_nothing_and_does_not_fail()
    {
        var repo = await _ws.InitializedRepoAsync();

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.Empty(await _ws.Engine.ListBranchesAsync(repo, Ct));
    }

    [Fact]
    public async Task A_repository_that_does_not_exist_cannot_be_reclaimed()
    {
        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.RepackAsync(_ws.NewRepo(), OptimizationMode.Reclaim, Ct));
    }
}
