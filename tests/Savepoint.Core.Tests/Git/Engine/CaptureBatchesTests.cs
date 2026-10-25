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
        new(options => options with { StagingLimits = new StagingLimits(BatchBytes: 100_000, BatchFiles: 3, BigBytes: 1_000) });

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

    // Changes nine of ten recorded files and adds one, so a watched capture of it takes four adds of three.
    private async Task<RepoRef> ChangedFolderAsync()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 10);
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.WriteMany(repo, "Data", 9, content: "modified");
        _ws.Write(repo, "Data/new.txt", "new");
        return repo;
    }

    [Fact]
    public async Task A_failure_in_a_later_group_leaves_the_next_capture_with_a_complete_snapshot_and_count()
    {
        var repo = await ChangedFolderAsync();
        var adds = 0;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "add" && Interlocked.Increment(ref adds) == 3)
            {
                throw new IOException("the disk went away");
            }
        };
        await Assert.ThrowsAsync<IOException>(() => _ws.CaptureAsync(repo, "profile-1", progress: new ProgressLog()));
        _ws.OnInvoke = null;
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.Equal(10, progress.Last.FilesDone);
        Assert.Equal(10, progress.Last.FilesTotal);
        var tree = await _ws.TreeAsync(repo, "profile-1");
        Assert.Equal(11, tree.Length);
        Assert.Equal("modified 3", await _ws.GitOutAsync(repo, "show", "profile-1:Data/f3.txt"));
        Assert.Equal("modified 8", await _ws.GitOutAsync(repo, "show", "profile-1:Data/f8.txt"));
    }

    [Fact]
    public async Task A_held_file_in_a_later_group_is_named_and_a_retry_once_it_is_free_is_complete()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 10);
        var held = _ws.FileOf(repo, "Data/f9.txt");
        string? error;
        using (HeldFiles.Hold(held, HeldFileKind.InUse))
        {
            var thrown = await Assert.ThrowsAnyAsync<Savepoint.Core.Errors.SavepointException>(() => _ws.CaptureAsync(repo, "profile-1", progress: new ProgressLog()));
            error = thrown.Args["path"]?.ToString();
        }

        var progress = new ProgressLog();
        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.Equal(held, error);
        Assert.Equal(10, (await _ws.TreeAsync(repo, "profile-1")).Length);
        Assert.Equal(10, progress.Last.FilesDone);
    }

    [Fact]
    public async Task A_capture_stopped_in_a_later_group_can_be_discarded_and_leaves_the_folder_as_recorded()
    {
        var repo = await ChangedFolderAsync();
        using var stop = new CancellationTokenSource();
        var adds = 0;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "add" && Interlocked.Increment(ref adds) == 2)
            {
                stop.Cancel();
            }
        };
        await Assert.ThrowsAsync<Savepoint.Core.Errors.OperationCancelledByUserException>(
            () => _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-1", "A point", Guid.NewGuid().ToString()), new ProgressLog(), stop.Token));
        _ws.OnInvoke = null;

        Assert.False((await _ws.Engine.GetStatusAsync(repo, TestContext.Current.CancellationToken)).IsClean);
        await _ws.Engine.DiscardChangesAsync(repo, TestContext.Current.CancellationToken);
        await _ws.Engine.CleanAsync(repo, TestContext.Current.CancellationToken);

        Assert.True((await _ws.Engine.GetStatusAsync(repo, TestContext.Current.CancellationToken)).IsClean);
        Assert.Equal("content 3", File.ReadAllText(_ws.FileOf(repo, "Data/f3.txt")));
        Assert.False(File.Exists(_ws.FileOf(repo, "Data/new.txt")));
    }

    [Fact]
    public async Task A_capture_stopped_in_a_later_group_is_finished_by_the_next_one()
    {
        var repo = await ChangedFolderAsync();
        using var stop = new CancellationTokenSource();
        var adds = 0;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "add" && Interlocked.Increment(ref adds) == 2)
            {
                stop.Cancel();
            }
        };
        await Assert.ThrowsAsync<Savepoint.Core.Errors.OperationCancelledByUserException>(
            () => _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-1", "A point", Guid.NewGuid().ToString()), new ProgressLog(), stop.Token));
        _ws.OnInvoke = null;
        var progress = new ProgressLog();

        var outcome = await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.True(outcome.Changed);
        Assert.Equal(10, progress.Last.FilesDone);
        Assert.Equal("modified 5", await _ws.GitOutAsync(repo, "show", "profile-1:Data/f5.txt"));
        Assert.Equal(11, (await _ws.TreeAsync(repo, "profile-1")).Length);
    }

    [Fact]
    public async Task A_new_file_that_vanishes_before_its_group_is_left_out_and_the_capture_goes_on()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 10);
        var adds = 0;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "add" && Interlocked.Increment(ref adds) == 2)
            {
                File.Delete(_ws.FileOf(repo, "Data/f9.txt"));
            }
        };
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        var tree = await _ws.TreeAsync(repo, "profile-1");
        Assert.Equal(9, tree.Length);
        Assert.DoesNotContain("Data/f9.txt", tree);
        Assert.Equal(progress.Last.FilesTotal, progress.Last.FilesDone);
    }

    [Fact]
    public async Task A_recorded_file_that_vanishes_before_its_group_is_still_removed_from_the_snapshot()
    {
        var repo = await ChangedFolderAsync();
        var adds = 0;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "add" && Interlocked.Increment(ref adds) == 2)
            {
                File.Delete(_ws.FileOf(repo, "Data/f8.txt"));
            }
        };

        await _ws.CaptureAsync(repo, "profile-1", progress: new ProgressLog());

        var tree = await _ws.TreeAsync(repo, "profile-1");
        Assert.DoesNotContain("Data/f8.txt", tree);
        Assert.Equal(10, tree.Length);
    }

    // Adding the file that replaces a folder drops the folder's recorded file from the index, so what is recorded is asked again for each group.
    [Fact]
    public async Task A_recorded_file_a_new_file_replaced_is_dropped_even_after_an_earlier_group_asked_what_is_recorded()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Folder/inner.txt", "inner");
        await _ws.CaptureAsync(repo, "profile-1");
        Directory.Delete(_ws.FileOf(repo, "Folder"), recursive: true);
        _ws.Write(repo, "Folder", "now a file");
        foreach (var name in new[] { "n0", "n1", "v0", "v1", "v2" })
        {
            _ws.Write(repo, $"Data/{name}.txt", name);
        }

        var adds = 0;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "add" && Interlocked.Increment(ref adds) == 1)
            {
                File.Delete(_ws.FileOf(repo, "Data/v2.txt"));
            }
        };

        await _ws.CaptureAsync(repo, "profile-1", progress: new ProgressLog());

        var tree = await _ws.TreeAsync(repo, "profile-1");
        Assert.Contains("Folder", tree);
        Assert.DoesNotContain("Folder/inner.txt", tree);
        Assert.DoesNotContain("Data/v2.txt", tree);
    }

    [Fact]
    public async Task A_file_that_cannot_be_looked_at_stays_in_its_group_so_git_answers_for_it()
    {
        _ws.Dispose();
        _ws = new EngineWorkspace(options => options with
        {
            StagingLimits = new StagingLimits(BatchBytes: 100_000, BatchFiles: 3, BigBytes: 1_000),
            PresenceProbe = path => path.EndsWith("v1.txt", StringComparison.Ordinal) ? Presence.Unknown : PathPresence.Look(path),
        });
        var repo = await _ws.InitializedRepoAsync();
        foreach (var name in new[] { "n0", "n1", "n2", "v1", "v2" })
        {
            _ws.Write(repo, $"Data/{name}.txt", name);
        }

        var adds = 0;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "add" && Interlocked.Increment(ref adds) == 1)
            {
                File.Delete(_ws.FileOf(repo, "Data/v1.txt"));
                File.Delete(_ws.FileOf(repo, "Data/v2.txt"));
            }
        };

        // v1 cannot be looked at, so the group that holds it still names it and git is left to say what is wrong; v2 is plainly gone.
        var error = await Record.ExceptionAsync(() => _ws.CaptureAsync(repo, "profile-1", progress: new ProgressLog()));

        Assert.NotNull(error);
        var lists = Adds().Select(Listed).ToList();
        Assert.Contains(lists, list => list.Contains("Data/v1.txt"));
        Assert.DoesNotContain(lists, list => list.Contains("Data/v2.txt"));
    }

    [Fact]
    public async Task The_big_files_are_given_to_git_with_the_recorded_ones_first()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/big_b.bin", Noise(2_000, 1));
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "Data/big_b.bin", Noise(2_500, 2));
        _ws.Write(repo, "Data/big_a.bin", Noise(3_000, 3));
        _ws.Write(repo, "Data/small.txt", "small");

        await _ws.CaptureAsync(repo, "profile-1", progress: new ProgressLog());

        var bigAdd = Adds().Select(Listed).Last(list => list.Contains("Data/big_a.bin"));
        Assert.Equal(["Data/big_b.bin", "Data/big_a.bin"], bigAdd);
    }

    [Fact]
    public async Task A_link_is_planned_by_its_own_size_not_the_size_of_what_it_points_to()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/real.bin", new byte[50_000]);
        try
        {
            File.CreateSymbolicLink(_ws.FileOf(repo, "Data/link.bin"), "real.bin");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            Assert.Skip("This system does not let the test account make links.");
        }

        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], TestContext.Current.CancellationToken);

        Assert.Equal(50_000, candidates.Single(candidate => candidate.Path == "Data/real.bin").Bytes);
        Assert.True(candidates.Single(candidate => candidate.Path == "Data/link.bin").Bytes < 100);
    }

    // A first snapshot, then deletions, a folder that becomes a file and a file that becomes a folder, excluded files, an empty folder,
    // changed contents and new files: enough for several groups of three.
    private async Task<(RepoRef Repo, IReadOnlyList<string> Ignore)> MessyFolderAsync(string game)
    {
        var repo = await _ws.InitializedRepoAsync(game);
        _ws.WriteMany(repo, "Data", 12);
        _ws.Write(repo, "Folder/inner.txt", "inner");
        _ws.Write(repo, "Plain.txt", "plain");
        await _ws.CaptureAsync(repo, "profile-1", ignore: ["Skip/"]);
        _ws.Delete(repo, "Data/f2.txt");
        _ws.Delete(repo, "Data/f7.txt");
        Directory.Delete(_ws.FileOf(repo, "Folder"), recursive: true);
        _ws.Write(repo, "Folder", "now a file");
        File.Delete(_ws.FileOf(repo, "Plain.txt"));
        _ws.Write(repo, "Plain.txt/under.txt", "now a folder");
        _ws.Write(repo, "Skip/a.txt", "excluded");
        _ws.Write(repo, "Data/f0.txt", "rewritten with another length");
        _ws.WriteMany(repo, "More", 8);
        Directory.CreateDirectory(_ws.FileOf(repo, "Empty/Deeper"));
        return (repo, ["Skip/"]);
    }

    [Fact]
    public async Task A_messy_change_makes_the_same_snapshot_in_groups_as_in_one_add()
    {
        var (watched, ignore) = await MessyFolderAsync("game-w");
        var (plain, _) = await MessyFolderAsync("game-p");

        await _ws.CaptureAsync(watched, "profile-1", ignore: ignore, progress: new ProgressLog());
        await _ws.CaptureAsync(plain, "profile-1", ignore: ignore);

        Assert.True(Adds().Count(add => add.GitDir == watched.GitDir) >= 5, "the change was not split into groups");
        Assert.Equal(
            await _ws.GitOutAsync(plain, "rev-parse", "profile-1^{tree}"),
            await _ws.GitOutAsync(watched, "rev-parse", "profile-1^{tree}"));
        Assert.Equal(
            await _ws.GitOutAsync(plain, "log", "-1", "--format=%(trailers:key=Savepoint-Empty-Folders,valueonly)", "profile-1"),
            await _ws.GitOutAsync(watched, "log", "-1", "--format=%(trailers:key=Savepoint-Empty-Folders,valueonly)", "profile-1"));
        Assert.DoesNotContain("Skip/a.txt", await _ws.TreeAsync(watched, "profile-1"));
    }

    private static string[] Packs(RepoRef repo)
    {
        var folder = Path.Combine(repo.GitDir, "objects", "pack");
        return Directory.Exists(folder) ? [.. Directory.GetFiles(folder, "*.pack").Select(Path.GetFileName).Order(StringComparer.Ordinal)!] : [];
    }

    private static byte[] Noise(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    [Fact]
    public async Task Every_big_file_goes_into_one_pack_that_a_routine_repack_leaves_in_place()
    {
        var repo = await _ws.InitializedRepoAsync();
        for (var i = 0; i < 4; i++)
        {
            _ws.Write(repo, $"Data/big{i}.bin", Noise(4_000, i));
        }

        _ws.WriteMany(repo, "Small", 8);

        await _ws.CaptureAsync(repo, "profile-1", progress: new ProgressLog());

        var packs = Packs(repo);
        Assert.Single(packs);
        await _ws.Engine.RepackAsync(repo, OptimizationMode.Routine, TestContext.Current.CancellationToken);
        Assert.Contains(packs[0], Packs(repo));
    }

    [Fact]
    public async Task The_big_files_are_announced_before_their_one_add()
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
