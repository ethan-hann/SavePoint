using System.Runtime.Versioning;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

[Collection(EngineCollections.C)]
public sealed class PreviewTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Candidates_are_the_files_a_capture_would_record_with_their_sizes()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/sub dir/mod.esp", "12");
        _ws.Write(repo, "caf\u00e9.txt", "12345");
        _ws.Write(repo, "game.log", "ignored");

        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, ["*.log"], Ct);

        Assert.Equal(
            [new CaptureCandidate("Data/sub dir/mod.esp", 2), new CaptureCandidate("caf\u00e9.txt", 5)],
            candidates.OrderBy(candidate => candidate.Path, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Candidates_include_files_that_are_already_recorded()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "old.txt", "old");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "new.txt", "newer");

        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);

        Assert.Equal([new CaptureCandidate("new.txt", 5), new CaptureCandidate("old.txt", 3)], candidates.OrderBy(candidate => candidate.Path, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Candidates_leave_out_the_marker_and_write_nothing_into_the_work_tree()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var before = Directory.GetFileSystemEntries(repo.WorkTree).Order().ToArray();

        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, ["*.log", "Cache/"], Ct);

        Assert.Equal(["a.txt"], candidates.Select(candidate => candidate.Path));
        Assert.Equal(before, Directory.GetFileSystemEntries(repo.WorkTree).Order().ToArray());
        Assert.Contains("*.log", EngineWorkspace.GitDirLines(repo, "info/exclude"));
    }

    [Fact]
    public async Task A_recorded_file_that_is_gone_from_disk_is_left_out()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "stays.txt", "x");
        _ws.Write(repo, "vanishes.txt", "y");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Delete(repo, "vanishes.txt");

        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);

        Assert.Equal(["stays.txt"], candidates.Select(candidate => candidate.Path));
    }

    [Fact]
    public async Task Previews_refuse_a_folder_without_this_games_marker_and_a_protected_one()
    {
        var repo = await _ws.InitializedRepoAsync();
        var protectedRepo = repo with { WorkTree = _ws.Platform.Directories.HomeDir };
        Directory.CreateDirectory(protectedRepo.WorkTree);

        await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.ListCaptureCandidatesAsync(EngineWorkspace.Claim(repo, "other", RepoRole.External), [], Ct));
        await Assert.ThrowsAsync<PathSafetyException>(() => _ws.Engine.ListCaptureCandidatesAsync(protectedRepo, [], Ct));
        await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.EstimateNewBytesAsync(EngineWorkspace.Claim(repo, "other", RepoRole.External), [new CaptureCandidate("a", 1)], [], Ct));
        await Assert.ThrowsAsync<PathSafetyException>(() => _ws.Engine.EstimateNewBytesAsync(protectedRepo, [new CaptureCandidate("a", 1)], [], Ct));
    }

    [Fact]
    public async Task With_nothing_to_record_the_estimate_is_zero()
    {
        var repo = await _ws.InitializedRepoAsync();

        Assert.Equal(0, await _ws.Engine.EstimateNewBytesAsync(repo, [], [], Ct));
    }

    [Fact]
    public async Task With_no_point_yet_every_byte_is_new_and_nothing_is_hashed()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "12345");
        _ws.Write(repo, "b.txt", "12345");
        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);

        var bytes = await _ws.Engine.EstimateNewBytesAsync(repo, candidates, [], Ct);

        Assert.Equal(10, bytes);
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments[0] is "cat-file" or "hash-object");
    }

    [Fact]
    public async Task Only_the_new_file_costs_anything_when_the_rest_is_already_recorded()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "old.txt", "already recorded content");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "new.txt", "brand new!");
        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);

        Assert.Equal(10, await _ws.Engine.EstimateNewBytesAsync(repo, candidates, [], Ct));
    }

    [Fact]
    public async Task Unchanged_files_are_not_read_at_all()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 30);
        _ws.AgeFiles(repo);
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "new.txt", "123456789");
        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "a privileged user reads any file");
        using var closed = MakeUnreadable(_ws.FileOf(repo, "Data/f3.txt"));

        var bytes = await _ws.Engine.EstimateNewBytesAsync(repo, candidates, [], Ct);

        Assert.Equal(9, bytes);
    }

    // Permission changes, backup tools and virus scanners move a file's change time without touching its content; git must not
    // take that for an edit, or the estimate reads the file (and counts it in full when it cannot).
    [Fact]
    public async Task A_file_whose_change_time_moved_but_whose_content_did_not_is_not_read()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 30);
        _ws.AgeFiles(repo);
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "new.txt", "123456789");
        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "a privileged user reads any file");
        var file = _ws.FileOf(repo, "Data/f3.txt");
        await MoveChangeTimeAsync(file);
        using var closed = MakeUnreadable(file);

        var bytes = await _ws.Engine.EstimateNewBytesAsync(repo, candidates, [], Ct);

        Assert.Equal(9, bytes);
    }

    // Windows keeps the creation time where git looks for the change time; elsewhere a rewrite of the same stamp moves it, but git
    // compares whole seconds, so the move has to land in a later second than the capture's.
    private static async Task MoveChangeTimeAsync(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-3));
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(1100), Ct);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task The_same_content_under_another_name_costs_nothing()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "original.txt", "shared content of some length");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "copy.txt", "shared content of some length");
        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);

        Assert.Equal(0, await _ws.Engine.EstimateNewBytesAsync(repo, candidates, [], Ct));
    }

    [Fact]
    public async Task Content_that_exists_only_on_another_branch_costs_nothing()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "profile one content");
        await _ws.CaptureAsync(repo, "profile-1");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-2");
        _ws.Write(repo, "b.txt", "only on profile two");
        await _ws.CaptureAsync(repo, "profile-2");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "profile-1");
        _ws.Write(repo, "c.txt", "only on profile two");
        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);

        Assert.Equal(0, await _ws.Engine.EstimateNewBytesAsync(repo, candidates, [], Ct));
    }

    [Fact]
    public async Task Identical_new_files_are_paid_for_once()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "base.txt", "base");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "one.bin", "twelve bytes");
        _ws.Write(repo, "two.bin", "twelve bytes");
        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);

        Assert.Equal(12, await _ws.Engine.EstimateNewBytesAsync(repo, candidates, [], Ct));
    }

    [Fact]
    public async Task A_modified_file_costs_its_new_content_unless_the_store_already_has_it()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "first version");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "a.txt", "second version, longer");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "a.txt", "third and brand new");
        var changed = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);
        var changedBytes = await _ws.Engine.EstimateNewBytesAsync(repo, changed, [], Ct);

        _ws.Write(repo, "a.txt", "first version");
        var reverted = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);
        var revertedBytes = await _ws.Engine.EstimateNewBytesAsync(repo, reverted, [], Ct);

        Assert.Equal("third and brand new".Length, changedBytes);
        Assert.Equal(0, revertedBytes);
    }

    [Fact]
    public async Task A_file_that_vanishes_before_it_is_read_is_skipped_not_an_error()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "base.txt", "base");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "stays.txt", "123");
        _ws.Write(repo, "vanishes.txt", "4567");
        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);
        _ws.Delete(repo, "vanishes.txt");

        Assert.Equal(3, await _ws.Engine.EstimateNewBytesAsync(repo, candidates, [], Ct));
    }

    [Fact]
    public async Task A_file_another_program_holds_is_counted_as_new_because_it_cannot_be_compared()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows can hold a file closed to every reader");
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "base.txt", "base");
        await _ws.CaptureAsync(repo, "profile-1");
        var held = _ws.Write(repo, "held.sav", "123456");
        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);
        using var stream = new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Equal(6, await _ws.Engine.EstimateNewBytesAsync(repo, candidates, [], Ct));
    }

    [Fact]
    public async Task The_estimate_hashes_files_itself_so_git_hash_object_is_never_started()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "base.txt", "base");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "new.txt", "new");
        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);

        await _ws.Engine.EstimateNewBytesAsync(repo, candidates, [], Ct);

        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments[0] == "hash-object");
        Assert.All(_ws.Invocations.Where(invocation => invocation.Arguments[0] is "ls-files" or "diff" or "cat-file"), invocation => Assert.True(invocation.ReadOnly));
    }

    // Makes reading the file fail until the returned object is disposed.
    private static IDisposable MakeUnreadable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        return RemoveReadAccess(path);
    }

    [UnsupportedOSPlatform("windows")]
    private static IDisposable RemoveReadAccess(string path)
    {
        var original = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, UnixFileMode.None);
        return new Restore(() => File.SetUnixFileMode(path, original));
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
