using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A snapshot has to leave room for the bytes it adds, and what an earlier failed one left behind must not count against it.
[Collection(EngineCollections.F)]
public sealed class CaptureSpaceTests : IDisposable
{
    private const long _floor = 1_000;

    private readonly EngineWorkspace _ws = new(options => options with { MinFreeBytesForStorageWrites = _floor });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private static int ObjectFiles(RepoRef repo) =>
        Directory.EnumerateFiles(Path.Combine(repo.GitDir, "objects"), "*", SearchOption.AllDirectories).Count();

    private static long TemporaryPackBytes(RepoRef repo)
    {
        var pack = Path.Combine(repo.GitDir, "objects", "pack");
        return Directory.Exists(pack) ? Directory.EnumerateFiles(pack, "tmp_pack_*").Sum(file => new FileInfo(file).Length) : 0;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_snapshot_larger_than_the_free_space_is_refused_before_any_object_is_written(bool watched)
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", new byte[300_000]);
        _ws.Write(repo, "small.txt", "x");
        _ws.Space.Free = _floor + 200_000;
        var objectsBefore = ObjectFiles(repo);

        var error = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.CaptureAsync(repo, progress: watched ? new Progress<FileProgress>(_ => { }) : null));

        Assert.Equal("error.diskSpace.low", error.MessageId);
        Assert.Equal(_floor + 300_001, error.Args["requiredBytes"]);
        Assert.Equal(_floor + 200_000, error.Args["freeBytes"]);
        Assert.Equal(objectsBefore, ObjectFiles(repo));
        Assert.Null(await _ws.ResolveAsync(repo, "HEAD"));
    }

    // What the capture tells the hook it is about to add, with nothing stored yet.
    private async Task<long?> ReportedBytesAsync(RepoRef repo, string branch)
    {
        long? reported = null;
        var objectsBefore = ObjectFiles(repo);
        var request = new CaptureRequest(branch, "A point", Guid.NewGuid().ToString())
        {
            BeforeWriting = (bytes, _, _) =>
            {
                reported = bytes;
                Assert.Equal(objectsBefore, ObjectFiles(repo));
                return Task.CompletedTask;
            },
        };
        await _ws.Engine.CaptureAsync(repo, request, null, Ct);
        return reported;
    }

    [Fact]
    public async Task A_capture_reports_the_new_and_changed_files_it_would_add_before_it_stores_any_and_not_the_ones_already_recorded()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", new byte[300_000]);
        Assert.Equal(300_000, await ReportedBytesAsync(repo, "profile-1"));
        _ws.Write(repo, "small.txt", "xyz");
        _ws.Write(repo, "big.bin", new byte[300_005]);

        var reported = await ReportedBytesAsync(repo, "profile-1");

        Assert.Equal(300_008, reported);
    }

    private static byte[] Filled(int length, byte value) => Enumerable.Repeat(value, length).ToArray();

    // Same length, different bytes from Filled(length, 7): every value differs, so no stored file can share its content.
    private static byte[] Ramp(int length, int seed) => Enumerable.Range(0, length).Select(i => (byte)((i * 31 + seed) % 251)).ToArray();

    [Fact]
    public async Task Switching_back_to_a_known_snapshot_leaves_nothing_for_the_next_snapshot_to_add()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", Ramp(300_000, 1));
        var first = (await _ws.CaptureAsync(repo, "profile-1", "First")).TipCommitId!;
        _ws.Write(repo, "big.bin", Ramp(310_000, 2));
        var fork = new CaptureRequest("profile-2", "Other", Guid.NewGuid().ToString()) { Mode = CaptureMode.Fork, ExpectedStartCommit = first };
        await _ws.Engine.CaptureAsync(repo, fork, null, Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-1", ct: Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-2", ct: Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-1", ct: Ct);
        _ws.Write(repo, "small.txt", "x");

        Assert.Equal(1, await ReportedBytesAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task A_file_edited_and_put_back_as_it_was_recorded_does_not_count()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", Ramp(300_000, 1));
        await _ws.CaptureAsync(repo, "profile-1", "First");
        _ws.Write(repo, "big.bin", Ramp(300_000, 2));
        _ws.Write(repo, "big.bin", Ramp(300_000, 1));

        Assert.Equal(0, await ReportedBytesAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task A_file_put_back_as_an_older_snapshot_held_it_does_not_count_even_when_its_length_differs_from_the_current_one()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", Ramp(300_000, 1));
        await _ws.CaptureAsync(repo, "profile-1", "First");
        _ws.Write(repo, "big.bin", Ramp(310_000, 2));
        await _ws.CaptureAsync(repo, "profile-1", "Second");
        _ws.Write(repo, "big.bin", Ramp(300_000, 1));

        // Files are only read for stored content when the raw size does not fit.
        _ws.Space.Free = _floor + 100_000;

        Assert.Equal(0, await ReportedBytesAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task A_new_file_whose_content_is_already_stored_under_another_name_does_not_count()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", Ramp(300_000, 1));
        await _ws.CaptureAsync(repo, "profile-1", "First");
        _ws.Write(repo, "copy.bin", Ramp(300_000, 1));
        _ws.Space.Free = _floor + 100_000;

        Assert.Equal(0, await ReportedBytesAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task A_changed_file_of_the_same_length_but_different_content_counts_in_full()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", Ramp(300_000, 1));
        await _ws.CaptureAsync(repo, "profile-1", "First");
        _ws.Write(repo, "big.bin", Ramp(300_000, 2));

        Assert.Equal(300_000, await ReportedBytesAsync(repo, "profile-1"));
    }

    // A stored file put back over a newer one, which the space check can only know about by reading it.
    private async Task<long?> ReportedAfterPuttingBackAsync(EngineWorkspace ws)
    {
        var repo = await ws.InitializedRepoAsync();
        ws.Write(repo, "big.bin", Ramp(300_000, 1));
        await ws.CaptureAsync(repo, "profile-1", "First");
        ws.Write(repo, "big.bin", Ramp(310_000, 2));
        await ws.CaptureAsync(repo, "profile-1", "Second");
        ws.Write(repo, "big.bin", Ramp(300_000, 1));
        long? reported = null;
        var request = new CaptureRequest("profile-1", "Third", Guid.NewGuid().ToString())
        {
            BeforeWriting = (bytes, _, _) =>
            {
                reported = bytes;
                return Task.CompletedTask;
            },
        };
        await ws.Engine.CaptureAsync(repo, request, null, Ct);
        return reported;
    }

    // Counts the files the space check reads in full while putting a stored file back over a newer one.
    private async Task<int> ReadsWithFreeSpaceAsync(long? freeAboveFloor)
    {
        var reads = 0;
        using var counting = new EngineWorkspace(options => options with { MinFreeBytesForStorageWrites = _floor, FileHashed = _ => Interlocked.Increment(ref reads) });
        var repo = await counting.InitializedRepoAsync();
        counting.Write(repo, "big.bin", Ramp(300_000, 1));
        await counting.CaptureAsync(repo, "profile-1", "First");
        counting.Write(repo, "big.bin", Ramp(310_000, 2));
        await counting.CaptureAsync(repo, "profile-1", "Second");
        counting.Write(repo, "big.bin", Ramp(300_000, 1));
        if (freeAboveFloor is { } room)
        {
            counting.Space.Free = _floor + room;
        }

        var before = Volatile.Read(ref reads);
        await counting.CaptureAsync(repo);
        return Volatile.Read(ref reads) - before;
    }

    [Fact]
    public async Task A_folder_that_fit_on_its_raw_size_can_still_be_asked_for_the_figure_without_content_the_store_holds()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", Ramp(300_000, 1));
        await _ws.CaptureAsync(repo, "profile-1", "First");
        _ws.Write(repo, "big.bin", Ramp(310_000, 2));
        await _ws.CaptureAsync(repo, "profile-1", "Second");
        _ws.Write(repo, "big.bin", Ramp(300_000, 1));
        _ws.Write(repo, "fresh.bin", Ramp(20_000, 3));
        long? first = null;
        long? reduced = null;
        var request = new CaptureRequest("profile-1", "Third", Guid.NewGuid().ToString())
        {
            BeforeWriting = async (bytes, reduce, token) =>
            {
                first = bytes;
                reduced = await reduce(token);
            },
        };

        await _ws.Engine.CaptureAsync(repo, request, null, Ct);

        Assert.Equal(320_000, first);
        Assert.Equal(20_000, reduced);
    }

    [Fact]
    public async Task The_byte_total_of_a_watched_capture_leaves_out_content_the_store_holds_once_the_check_has_found_it()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", Ramp(300_000, 1));
        await _ws.CaptureAsync(repo, "profile-1", "First");
        _ws.Write(repo, "big.bin", Ramp(310_000, 2));
        await _ws.CaptureAsync(repo, "profile-1", "Second");
        _ws.Write(repo, "big.bin", Ramp(300_000, 1));
        _ws.Write(repo, "fresh.bin", Ramp(20_000, 3));
        _ws.Space.Free = _floor + 100_000;
        var progress = new ProgressLog();

        await _ws.CaptureAsync(repo, "profile-1", progress: progress);

        Assert.Equal(20_000, progress.Reports[0].BytesTotal);
    }

    [Fact]
    public async Task A_snapshot_that_fits_without_looking_at_what_is_stored_reads_no_file_content()
    {
        Assert.Equal(0, await ReadsWithFreeSpaceAsync(freeAboveFloor: null));
        Assert.Equal(0, await ReadsWithFreeSpaceAsync(freeAboveFloor: 1_000_000));
    }

    [Fact]
    public async Task A_snapshot_that_does_not_fit_by_its_raw_size_reads_files_to_find_what_is_stored()
    {
        Assert.Equal(1, await ReadsWithFreeSpaceAsync(freeAboveFloor: 100_000));
    }

    [Fact]
    public async Task Files_past_the_read_limit_count_in_full_even_when_their_content_is_stored()
    {
        using var limited = new EngineWorkspace(options => options with { MinFreeBytesForStorageWrites = _floor, StoredContentReadBytes = 100_000 });

        Assert.Equal(300_000, await ReportedAfterPuttingBackAsync(limited));
    }

    [Fact]
    public async Task Files_git_streams_into_a_pack_count_in_full_even_when_their_content_is_stored()
    {
        using var streaming = new EngineWorkspace(options => options with
        {
            MinFreeBytesForStorageWrites = _floor,
            StagingLimits = new StagingLimits(BatchBytes: 100_000, BatchFiles: 50, BigBytes: 50_000),
        });

        Assert.Equal(300_000, await ReportedAfterPuttingBackAsync(streaming));
    }

    [Fact]
    public async Task A_new_file_next_to_files_already_stored_counts_in_full_and_a_snapshot_that_cannot_hold_it_is_refused()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", Ramp(300_000, 1));
        await _ws.CaptureAsync(repo, "profile-1", "First");
        _ws.Write(repo, "big.bin", Ramp(310_000, 2));
        await _ws.CaptureAsync(repo, "profile-1", "Second");
        _ws.Write(repo, "big.bin", Ramp(300_000, 1));
        _ws.Write(repo, "fresh.bin", Ramp(200_000, 3));
        _ws.Space.Free = _floor + 150_000;

        var error = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.CaptureAsync(repo));

        Assert.Equal(_floor + 200_000, error.Args["requiredBytes"]);
    }

    [Fact]
    public async Task A_snapshot_of_files_already_stored_is_not_refused_on_a_nearly_full_drive()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", Ramp(300_000, 1));
        await _ws.CaptureAsync(repo, "profile-1", "First");
        _ws.Write(repo, "big.bin", Ramp(310_000, 2));
        await _ws.CaptureAsync(repo, "profile-1", "Second");
        _ws.Write(repo, "big.bin", Ramp(300_000, 1));
        _ws.Space.Free = _floor + 100;

        var outcome = await _ws.CaptureAsync(repo);

        Assert.True(outcome.Changed);
    }

    [Fact]
    public async Task A_capture_with_nothing_to_store_still_reports_nothing_to_add()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-1", "First");

        Assert.Equal(0, await ReportedBytesAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task A_capture_the_hook_refuses_stores_nothing()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var objectsBefore = ObjectFiles(repo);
        var request = new CaptureRequest("profile-1", "A point", Guid.NewGuid().ToString())
        {
            BeforeWriting = (_, _, _) => throw new DiskSpaceException(repo.GitDir, 10, 5),
        };

        await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.Engine.CaptureAsync(repo, request, null, Ct));

        Assert.Equal(objectsBefore, ObjectFiles(repo));
        Assert.Null(await _ws.ResolveAsync(repo, "HEAD"));
    }

    [Fact]
    public async Task Room_for_several_folders_is_refused_with_the_figures_for_all_of_them_together()
    {
        var install = await _ws.InitializedRepoAsync(role: RepoRole.Install);
        var saves = await _ws.InitializedRepoAsync(role: RepoRole.External);
        _ws.Space.Free = _floor + 400_000;

        var error = Assert.Throws<DiskSpaceException>(() => _ws.Engine.RequireCaptureRoom([install, saves], 500_000));

        Assert.Equal(_floor + 500_000, error.Args["requiredBytes"]);
        Assert.Equal(_floor + 400_000, error.Args["freeBytes"]);
    }

    [Fact]
    public async Task Room_for_several_folders_is_granted_when_they_fit_together()
    {
        var install = await _ws.InitializedRepoAsync(role: RepoRole.Install);
        var saves = await _ws.InitializedRepoAsync(role: RepoRole.External);
        _ws.Space.Free = _floor + 500_000;

        _ws.Engine.RequireCaptureRoom([install, saves], 500_000);
    }

    [Fact]
    public async Task A_temporary_pack_left_in_any_of_the_folders_does_not_block_the_save_of_all_of_them()
    {
        var install = await _ws.InitializedRepoAsync(role: RepoRole.Install);
        var saves = await _ws.InitializedRepoAsync(role: RepoRole.External);
        var leftover = Path.Combine(saves.GitDir, "objects", "pack", "tmp_pack_stopped");
        Directory.CreateDirectory(Path.GetDirectoryName(leftover)!);
        await File.WriteAllBytesAsync(leftover, new byte[50_000], Ct);
        _ws.Space.PerPath = _ => _floor + 500_000 - TemporaryPackBytes(saves);

        _ws.Engine.RequireCaptureRoom([install, saves], 500_000);

        Assert.False(File.Exists(leftover));
    }

    [Fact]
    public async Task A_snapshot_that_fits_the_free_space_is_recorded()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", new byte[300_000]);
        _ws.Space.Free = _floor + 300_000;

        var outcome = await _ws.CaptureAsync(repo);

        Assert.True(outcome.Changed);
    }

    [Fact]
    public async Task Only_the_files_that_changed_count_toward_the_space_a_snapshot_needs()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "big.bin", new byte[300_000]);
        await _ws.CaptureAsync(repo, "profile-1", "First");
        _ws.Write(repo, "small.txt", "x");
        _ws.Space.Free = _floor + 5_000;

        var outcome = await _ws.CaptureAsync(repo, "profile-1", "Second");

        Assert.True(outcome.Changed);
    }

    [Fact]
    public async Task A_temporary_pack_a_stopped_snapshot_left_does_not_block_the_next_one()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var leftover = Path.Combine(repo.GitDir, "objects", "pack", "tmp_pack_stopped");
        Directory.CreateDirectory(Path.GetDirectoryName(leftover)!);
        await File.WriteAllBytesAsync(leftover, new byte[50_000], Ct);

        // The volume is as full as the leftover makes it: room for the snapshot only once the leftover is gone.
        _ws.Space.PerPath = _ => _floor + 10 - TemporaryPackBytes(repo);

        var outcome = await _ws.CaptureAsync(repo);

        Assert.True(outcome.Changed);
        Assert.False(File.Exists(leftover));
    }

    [Fact]
    public async Task A_snapshot_is_still_refused_when_the_volume_stays_low_after_the_leftover_is_cleared()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var leftover = Path.Combine(repo.GitDir, "objects", "pack", "tmp_pack_stopped");
        Directory.CreateDirectory(Path.GetDirectoryName(leftover)!);
        await File.WriteAllBytesAsync(leftover, new byte[50_000], Ct);
        _ws.Space.Free = 10;

        var error = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.CaptureAsync(repo));

        Assert.Equal(_floor, error.Args["requiredBytes"]);
        Assert.False(File.Exists(leftover), "the leftover was meant to be cleared before the volume was judged again");
        Assert.Null(await _ws.ResolveAsync(repo, "HEAD"));
    }
}
