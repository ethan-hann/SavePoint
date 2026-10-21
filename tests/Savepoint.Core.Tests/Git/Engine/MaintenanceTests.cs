using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Every git process costs most of a second on a machine with a virus scanner, so a test starts from a repository state built
// once per run and asserts the facets of one optimization together.
[Collection(EngineCollections.B)]
public sealed class MaintenanceTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Packing_moves_the_loose_objects_into_a_pack_in_the_geometric_form_without_the_game_folder_and_past_a_stale_lock()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        Assert.True(await _ws.Engine.CountLooseObjectsAsync(repo, Ct) > 0);
        Directory.Move(repo.WorkTree, repo.WorkTree + "-moved");
        await File.WriteAllTextAsync(Path.Combine(repo.GitDir, "index.lock"), string.Empty, Ct);

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);

        Assert.Equal(0, await _ws.Engine.CountLooseObjectsAsync(repo, Ct));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(repo.GitDir, "objects", "pack"), "*.pack"));
        Assert.False(File.Exists(Path.Combine(repo.GitDir, "index.lock")));
        var repack = Assert.Single(_ws.Invocations, invocation => invocation.Arguments[0] == "repack");
        Assert.Contains("-d", repack.Arguments);
        Assert.Contains("--geometric=2", repack.Arguments);
        Assert.DoesNotContain("-a", repack.Arguments);
        Assert.DoesNotContain("-A", repack.Arguments);
        Assert.DoesNotContain("--auto", repack.Arguments);
        Assert.DoesNotContain("--detach", repack.Arguments);
        Assert.DoesNotContain("--no-detach", repack.Arguments);
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments[0] is "gc" or "maintenance");
    }

    [Fact]
    public async Task A_repository_that_does_not_exist_has_no_objects_and_no_size()
    {
        var repo = _ws.NewRepo();

        Assert.Equal(0, await _ws.Engine.CountLooseObjectsAsync(repo, Ct));
        Assert.Equal(0, await _ws.Engine.GetRepoSizeAsync(repo, Ct));
    }

    [Fact]
    public async Task The_size_covers_loose_objects_and_packs_and_a_second_optimization_leaves_the_large_pack_from_the_first_one_alone()
    {
        var repo = await _ws.InitializedRepoAsync();
        var small = await _ws.Engine.GetRepoSizeAsync(repo, Ct);
        var random = new byte[2_000_000];
        new Random(11).NextBytes(random);
        _ws.Write(repo, "game.pak", random);
        _ws.WriteMany(repo, "Data", 40);
        await _ws.CaptureAsync(repo, "profile-1");
        var withBig = await _ws.Engine.GetRepoSizeAsync(repo, Ct);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        var packed = await _ws.Engine.GetRepoSizeAsync(repo, Ct);
        var largest = LargestPack(repo);

        _ws.Write(repo, "Data/f1.txt", "a small change");
        await _ws.CaptureAsync(repo, "profile-1");
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        _ws.Write(repo, "Data/f2.txt", "another small change");
        await _ws.CaptureAsync(repo, "profile-1");
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);

        Assert.True(withBig - small >= 1_800_000, $"{withBig} - {small} should include the 2 MB file");
        Assert.InRange(packed, 1_800_000, withBig + 50_000);
        Assert.Equal(largest, LargestPack(repo));
        Assert.True(Directory.GetFiles(Path.Combine(repo.GitDir, "objects", "pack"), "*.pack").Length >= 2, "the small captures should have gone into packs of their own");
        Assert.Equal(0, await _ws.Engine.CountLooseObjectsAsync(repo, Ct));
    }

    [Fact]
    public async Task A_low_storage_volume_refuses_before_git_is_started()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        _ws.Space.Free = 10;

        var error = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct));

        Assert.Equal("error.diskSpace.low", error.MessageId);
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments[0] == "repack");
        Assert.All(_ws.Space.Queries.TakeLast(1), query => Assert.StartsWith(_ws.StorageRoot, query, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_cancellation_before_git_starts_throws_the_compaction_error_and_runs_nothing()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "repack")
            {
                cancel.Cancel();
            }
        };

        var error = await Assert.ThrowsAsync<OperationCancelledByUserException>(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, cancel.Token));

        Assert.Equal("error.compaction.cancelled", error.MessageId);
        _ws.OnInvoke = null;
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        Assert.Equal(0, await _ws.Engine.CountLooseObjectsAsync(repo, Ct));
        Assert.Equal(3, await _ws.CountAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task A_repack_killed_while_it_writes_its_pack_leaves_a_sound_store()
    {
        var repo = await _ws.InitializedRepoAsync();
        var random = new byte[60_000_000];
        new Random(21).NextBytes(random);
        _ws.Write(repo, "big.bin", random);
        await _ws.CaptureAsync(repo, "profile-1");
        var packs = Path.Combine(repo.GitDir, "objects", "pack");
        Directory.CreateDirectory(packs);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "repack")
            {
                // Cancels once git has begun writing, which is the moment the earlier test never reached.
                _ = Task.Run(async () =>
                {
                    for (var i = 0; i < 5000 && !cancel.IsCancellationRequested; i++)
                    {
                        if (Directory.EnumerateFiles(packs, "tmp_pack_*").Any())
                        {
                            await cancel.CancelAsync();
                            return;
                        }

                        await Task.Delay(2, CancellationToken.None);
                    }
                }, CancellationToken.None);
            }
        };

        var error = await Record.ExceptionAsync(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, cancel.Token));
        _ws.OnInvoke = null;
        Assert.SkipWhen(error is null, "the repack finished before the cancellation landed");

        Assert.IsType<OperationCancelledByUserException>(error);
        var check = await _ws.Runner.RunAsync(new GitInvocation(["fsck", "--no-dangling"]) { GitDir = repo.GitDir, WorkTree = repo.WorkTree, ReadOnly = true }, Ct);
        Assert.Equal(0, check.ExitCode);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, Ct);
        Assert.Equal(1, await _ws.CountAsync(repo, "profile-1"));
        Assert.Equal(0, await _ws.Engine.CountLooseObjectsAsync(repo, Ct));
    }

    [Fact]
    public async Task A_repository_that_does_not_exist_cannot_be_optimized()
    {
        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.RepackAsync(_ws.NewRepo(), OptimizationMode.Routine, Ct));
    }

    // Name and size of the biggest pack file.
    private static (string Name, long Length) LargestPack(RepoRef repo) =>
        Directory.GetFiles(Path.Combine(repo.GitDir, "objects", "pack"), "*.pack")
            .Select(path => (Name: Path.GetFileName(path), new FileInfo(path).Length))
            .OrderByDescending(pack => pack.Length)
            .First();
}
