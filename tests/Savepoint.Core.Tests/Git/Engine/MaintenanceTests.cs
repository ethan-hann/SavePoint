using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Git.Engine;

public sealed class MaintenanceTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Loose_objects_are_counted_and_packing_moves_them_into_a_pack()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.ThreePointsAsync(repo);
        Assert.True(await _ws.Engine.CountLooseObjectsAsync(repo, Ct) > 0);

        await _ws.Engine.RepackAsync(repo, Ct);

        Assert.Equal(0, await _ws.Engine.CountLooseObjectsAsync(repo, Ct));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(repo.GitDir, "objects", "pack"), "*.pack"));
    }

    [Fact]
    public async Task A_repository_that_does_not_exist_has_no_objects_and_no_size()
    {
        var repo = _ws.NewRepo();

        Assert.Equal(0, await _ws.Engine.CountLooseObjectsAsync(repo, Ct));
        Assert.Equal(0, await _ws.Engine.GetRepoSizeAsync(repo, Ct));
    }

    [Fact]
    public async Task The_size_covers_loose_objects_and_packs_and_grows_with_the_content()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "small.txt", "x");
        await _ws.CaptureAsync(repo, "profile-1");
        var small = await _ws.Engine.GetRepoSizeAsync(repo, Ct);
        var random = new byte[1_000_000];
        new Random(5).NextBytes(random);
        _ws.Write(repo, "big.bin", random);
        await _ws.CaptureAsync(repo, "profile-1");
        var withBig = await _ws.Engine.GetRepoSizeAsync(repo, Ct);
        await _ws.Engine.RepackAsync(repo, Ct);
        var packed = await _ws.Engine.GetRepoSizeAsync(repo, Ct);

        Assert.True(withBig - small >= 900_000, $"{withBig} - {small} should include the 1 MB file");
        Assert.InRange(packed, 900_000, withBig + 50_000);
    }

    [Fact]
    public async Task Repacking_uses_the_geometric_form_and_never_auto_detach_or_gc()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.ThreePointsAsync(repo);

        await _ws.Engine.RepackAsync(repo, Ct);

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
    public async Task A_second_optimization_leaves_the_large_pack_from_the_first_one_alone()
    {
        var repo = await _ws.InitializedRepoAsync();
        var random = new byte[2_000_000];
        new Random(11).NextBytes(random);
        _ws.Write(repo, "game.pak", random);
        _ws.WriteMany(repo, "Data", 40);
        await _ws.CaptureAsync(repo, "profile-1");
        await _ws.Engine.RepackAsync(repo, Ct);
        var largest = LargestPack(repo);

        _ws.Write(repo, "Data/f1.txt", "a small change");
        await _ws.CaptureAsync(repo, "profile-1");
        await _ws.Engine.RepackAsync(repo, Ct);
        _ws.Write(repo, "Data/f2.txt", "another small change");
        await _ws.CaptureAsync(repo, "profile-1");
        await _ws.Engine.RepackAsync(repo, Ct);

        Assert.Equal(largest, LargestPack(repo));
        Assert.True(Directory.GetFiles(Path.Combine(repo.GitDir, "objects", "pack"), "*.pack").Length >= 2, "the small captures should have gone into packs of their own");
        Assert.Equal(0, await _ws.Engine.CountLooseObjectsAsync(repo, Ct));
    }

    [Fact]
    public async Task A_low_storage_volume_refuses_before_git_is_started()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.ThreePointsAsync(repo);
        _ws.Space.Free = 10;

        var error = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.Engine.RepackAsync(repo, Ct));

        Assert.Equal("error.diskSpace.low", error.MessageId);
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments[0] == "repack");
        Assert.All(_ws.Space.Queries.TakeLast(1), query => Assert.StartsWith(_ws.StorageRoot, query, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_cancelled_optimization_throws_the_compaction_error_and_leaves_the_store_usable()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.ThreePointsAsync(repo);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "repack")
            {
                cancel.Cancel();
            }
        };

        var error = await Assert.ThrowsAsync<OperationCancelledByUserException>(() => _ws.Engine.RepackAsync(repo, cancel.Token));

        Assert.Equal("error.compaction.cancelled", error.MessageId);
        _ws.OnInvoke = null;
        await _ws.Engine.RepackAsync(repo, Ct);
        Assert.Equal(0, await _ws.Engine.CountLooseObjectsAsync(repo, Ct));
        Assert.Equal(3, await _ws.CountAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task Optimizing_does_not_need_the_game_folder()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.ThreePointsAsync(repo);
        Directory.Move(repo.WorkTree, repo.WorkTree + "-moved");

        await _ws.Engine.RepackAsync(repo, Ct);

        Assert.Equal(0, await _ws.Engine.CountLooseObjectsAsync(repo, Ct));
    }

    [Fact]
    public async Task A_stale_lock_does_not_stop_an_optimization()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.ThreePointsAsync(repo);
        await File.WriteAllTextAsync(Path.Combine(repo.GitDir, "index.lock"), string.Empty, Ct);

        await _ws.Engine.RepackAsync(repo, Ct);

        Assert.False(File.Exists(Path.Combine(repo.GitDir, "index.lock")));
    }

    [Fact]
    public async Task A_repository_that_does_not_exist_cannot_be_optimized()
    {
        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.RepackAsync(_ws.NewRepo(), Ct));
    }

    // Name and size of the biggest pack file.
    private static (string Name, long Length) LargestPack(Savepoint.Core.Git.RepoRef repo) =>
        Directory.GetFiles(Path.Combine(repo.GitDir, "objects", "pack"), "*.pack")
            .Select(path => (Name: Path.GetFileName(path), new FileInfo(path).Length))
            .OrderByDescending(pack => pack.Length)
            .First();
}
