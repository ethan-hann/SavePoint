using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// git prints a file's name only when its add ends, so a capture that is watched hands its files over in groups: the names of one group
// arrive as it ends, while the next copies, and the snapshot holds exactly what one add would have stored.
[Collection(EngineCollections.D)]
public sealed class CaptureBatchesTests : IDisposable
{
    private EngineWorkspace _ws = Small();

    public void Dispose() => _ws.Dispose();

    private static EngineWorkspace Small() =>
        new(options => options with { StagingLimits = new StagingLimits(BatchBytes: 100_000, BatchFiles: 3, SoloBytes: 500) });

    private static string[] Listed(GitInvocation add) => add.Stdin!.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private List<GitInvocation> Adds() => [.. _ws.Invocations.Where(invocation => invocation.Arguments[0] == "add")];

    private static List<string> Delivered(ProgressLog log) => [.. log.Reports.SelectMany(report => report.NewPaths ?? [])];

    [Fact]
    public async Task A_watched_capture_adds_its_files_in_groups_and_stores_every_one()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 10);
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        var adds = Adds();
        Assert.Equal(4, adds.Count);
        Assert.All(adds, add => Assert.True(Listed(add).Length <= 3));
        Assert.Equal(10, adds.SelectMany(Listed).Distinct().Count());
        Assert.Equal(10, (await _ws.TreeAsync(repo, "profile-1")).Length);
    }

    [Fact]
    public async Task The_closing_count_is_still_every_file_once()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 10);
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.Equal(10, progress.Last.FilesDone);
        Assert.Equal(10, progress.Last.FilesTotal);
        Assert.Equal(10, Delivered(progress).Distinct().Count());
        Assert.Equal(10, Delivered(progress).Count);
    }

    [Fact]
    public async Task A_capture_nobody_watches_is_one_add()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 10);

        await _ws.CaptureAsync(repo, "profile-1");

        Assert.Single(Adds());
    }

    [Fact]
    public async Task The_snapshot_is_the_same_as_one_add_would_make()
    {
        var watched = await _ws.InitializedRepoAsync();
        var plain = await _ws.InitializedRepoAsync("game-2");
        foreach (var repo in new[] { watched, plain })
        {
            _ws.WriteMany(repo, "Data", 10);
            _ws.Write(repo, "Data/big.bin", new byte[2_000]);
            _ws.Write(repo, "sub dir/it's [a].txt", "odd name");
        }

        await _ws.CaptureAsync(watched, "profile-1", progress: new ProgressLog());
        await _ws.CaptureAsync(plain, "profile-1");

        Assert.Equal(
            await _ws.GitOutAsync(plain, "rev-parse", "profile-1^{tree}"),
            await _ws.GitOutAsync(watched, "rev-parse", "profile-1^{tree}"));
    }

    [Fact]
    public async Task A_groups_names_have_arrived_before_the_next_group_starts()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 7);
        var progress = new ProgressLog();
        var seenAtSecondAdd = new List<string>();
        var adds = 0;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "add" && Interlocked.Increment(ref adds) == 2)
            {
                seenAtSecondAdd = Delivered(progress);
            }
        };

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.Equal(3, seenAtSecondAdd.Count);
    }

    [Fact]
    public async Task A_deleted_file_is_removed_in_its_group_and_counted()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 10);
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Delete(repo, "Data/f2.txt");
        _ws.Delete(repo, "Data/f8.txt");
        _ws.WriteMany(repo, "More", 5);
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        var tree = await _ws.TreeAsync(repo, "profile-1");
        Assert.DoesNotContain("Data/f2.txt", tree);
        Assert.DoesNotContain("Data/f8.txt", tree);
        Assert.Equal(13, tree.Length);
        Assert.Equal(7, progress.Last.FilesDone);
        Assert.Equal(7, Delivered(progress).Count);
    }

    [Fact]
    public async Task A_large_file_is_announced_before_its_add_and_added_alone()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 2);
        _ws.Write(repo, "Data/big.bin", new byte[2_000]);
        var progress = new ProgressLog();
        CopyDetail? announced = null;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "add" && Listed(invocation) is ["Data/big.bin"])
            {
                announced = progress.Reports.Select(report => report.Copying).FirstOrDefault(detail => detail is not null);
            }
        };

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.Equal(new CopyDetail("Data/big.bin", 0, 2_000, Starting: true), announced);
    }
}
