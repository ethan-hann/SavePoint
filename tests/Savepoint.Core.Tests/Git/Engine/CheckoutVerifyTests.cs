using System.Diagnostics;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// What happens when git says it switched but a path did not follow: the verify pass finds it and re-syncs it.
public sealed class CheckoutVerifyTests : IDisposable
{
    private static readonly TimeSpan[] _quickDelays = [TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(60)];

    private readonly EngineWorkspace _ws = new(options => options with { ResyncDelays = _quickDelays });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public void The_default_waits_between_re_sync_passes_are_150_and_300_milliseconds()
    {
        Assert.Equal([TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(300)], new GitEngineOptions().ResyncDelays);
    }

    [Fact]
    public async Task A_path_that_stayed_behind_is_re_synced_and_the_switch_completes()
    {
        var (repo, a) = await TwoProfilesAsync();
        var stale = _ws.FileOf(repo, "Data/blob.bin");
        CorruptOnFirstVerify(stale);

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
        Assert.Equal(2, VerifyPasses());
    }

    [Fact]
    public async Task The_stale_paths_go_to_git_on_stdin_never_on_the_command_line()
    {
        var (repo, _) = await TwoProfilesAsync();
        CorruptOnFirstVerify(_ws.FileOf(repo, "Data/blob.bin"));

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        var resync = Assert.Single(_ws.Invocations, invocation => invocation.Arguments.Contains("--pathspec-from-file=-"));
        Assert.Contains("--pathspec-file-nul", resync.Arguments);
        Assert.Equal("Data/blob.bin\0", resync.Stdin);
        Assert.DoesNotContain("Data/blob.bin", resync.Arguments);
        Assert.Equal("1", resync.ExtraEnvironment!["GIT_LITERAL_PATHSPECS"]);
    }

    [Fact]
    public async Task A_stale_name_with_non_ascii_characters_is_found_and_re_synced_unquoted()
    {
        var (repo, a) = await TwoProfilesAsync();
        CorruptOnFirstVerify(_ws.FileOf(repo, "caf\u00e9/cr\u00e8me br\u00fbl\u00e9e.esp"));

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
        var resync = Assert.Single(_ws.Invocations, invocation => invocation.Arguments.Contains("--pathspec-from-file=-"));
        Assert.Equal("caf\u00e9/cr\u00e8me br\u00fbl\u00e9e.esp\0", resync.Stdin);
    }

    [Fact]
    public async Task Thousands_of_stale_paths_re_sync_in_one_call_without_overflowing_the_command_line()
    {
        var repo = await _ws.InitializedRepoAsync();
        const int count = 900;
        var folder = "Data/" + new string('d', 30);
        _ws.WriteMany(repo, folder, count, "a-file-with-a-rather-long-name-number-", "a");
        await _ws.CaptureAsync(repo, "profile-a");
        var a = TreeSnapshot.Take(repo.WorkTree);
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.WriteMany(repo, folder, count, "a-file-with-a-rather-long-name-number-", "b");
        await _ws.CaptureAsync(repo, "profile-b");
        var corrupted = false;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "diff" && !corrupted)
            {
                corrupted = true;
                Parallel.For(0, count, i => File.WriteAllText(_ws.FileOf(repo, $"{folder}/a-file-with-a-rather-long-name-number-{i}.txt"), "garbage"));
            }
        };

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
        var resync = Assert.Single(_ws.Invocations, invocation => invocation.Arguments.Contains("--pathspec-from-file=-"));
        Assert.True(resync.Stdin!.Length > 32_768, "the list must be longer than a Windows command line allows");
        Assert.True(resync.Arguments.Sum(argument => argument.Length) < 500);
    }

    [Fact]
    public async Task A_path_that_never_follows_is_named_after_three_passes_with_the_waits_between()
    {
        var (repo, _) = await TwoProfilesAsync();
        var stale = _ws.FileOf(repo, "Data/blob.bin");
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "diff")
            {
                File.WriteAllText(stale, "garbage again");
            }
        };
        var clock = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.Equal(stale, error.Args["path"]);
        Assert.Contains("Data/blob.bin", error.Message, StringComparison.Ordinal);
        Assert.Equal(3, VerifyPasses());
        Assert.Equal(2, _ws.Invocations.Count(invocation => invocation.Arguments.Contains("--pathspec-from-file=-")));
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(80), "the waits (30 ms and 60 ms) must happen between the passes");
    }

    [Fact]
    public async Task A_file_git_failed_to_delete_is_removed_by_the_verify_pass()
    {
        var (repo, a) = await TwoProfilesAsync();
        var gone = _ws.FileOf(repo, "only-b.txt");
        CreateOnFirstVerify(gone, "left behind");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.False(File.Exists(gone));
        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
    }

    [Fact]
    public async Task A_file_that_cannot_be_deleted_is_named_and_the_switch_stays_unfinished_until_repeated()
    {
        var (repo, a) = await TwoProfilesAsync();
        var gone = _ws.FileOf(repo, "only-b.txt");
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "diff" && !File.Exists(gone))
            {
                File.WriteAllText(gone, "back again");
            }
        };

        var error = await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        Assert.Equal(gone, error.Args["path"]);
        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        Assert.NotNull(status.InterruptedSwitch);
        Assert.Equal(await _ws.TipAsync(repo, "profile-a"), status.InterruptedSwitch.ToCommit);

        _ws.OnInvoke = null;
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
        Assert.Null((await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch);
    }

    [Fact]
    public async Task A_completed_switch_leaves_no_unfinished_marker()
    {
        var (repo, _) = await TwoProfilesAsync();

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Null((await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch);
        Assert.False(File.Exists(Path.Combine(repo.GitDir, "savepoint-switch.json")));
    }

    [Fact]
    public async Task An_unfinished_switch_reports_where_it_came_from_and_where_it_was_going()
    {
        var (repo, _) = await TwoProfilesAsync();
        var from = await _ws.TipAsync(repo, "profile-b");
        var to = await _ws.TipAsync(repo, "profile-a");
        var stale = _ws.FileOf(repo, "Data/blob.bin");
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "diff")
            {
                File.WriteAllText(stale, "garbage again");
            }
        };

        await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

        var interrupted = (await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch;
        Assert.Equal(new InterruptedSwitch(from, to!), interrupted);
    }

    [Fact]
    public async Task A_file_held_open_with_no_sharing_fails_the_switch_naming_it_and_a_repeat_after_release_succeeds()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to read or replace a file another program holds open");
        var (repo, a) = await TwoProfilesAsync();
        var locked = _ws.FileOf(repo, "Data/blob.bin");
        var before = await File.ReadAllBytesAsync(locked, Ct);

        var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var error = await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

            Assert.Equal(locked, error.Args["path"]);
        }
        finally
        {
            await held.DisposeAsync();
        }

        Assert.Equal(before, await File.ReadAllBytesAsync(locked, Ct));
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
    }

    [Fact]
    public async Task A_file_held_open_for_reading_stays_stale_so_the_switch_names_it_and_a_repeat_after_release_succeeds()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to replace a file another program holds open");
        var (repo, a) = await TwoProfilesAsync();
        var locked = _ws.FileOf(repo, "Data/blob.bin");
        var before = await File.ReadAllBytesAsync(locked, Ct);

        var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            var error = await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

            Assert.Equal(locked, error.Args["path"]);
        }
        finally
        {
            await held.DisposeAsync();
        }

        Assert.Equal(before, await File.ReadAllBytesAsync(locked, Ct));
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
    }

    [Fact]
    public async Task A_file_to_be_deleted_that_is_held_open_is_named_and_removed_by_the_repeat()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to delete a file another program holds open");
        var (repo, a) = await TwoProfilesAsync();
        var locked = _ws.FileOf(repo, "only-b.txt");

        var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            var error = await Assert.ThrowsAsync<GitOperationException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct));

            Assert.Equal(locked, error.Args["path"]);
        }
        finally
        {
            await held.DisposeAsync();
        }

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
    }

    private int VerifyPasses() => _ws.Invocations.Count(invocation => invocation.Arguments[0] == "diff");

    private void CorruptOnFirstVerify(string path) => CreateOnFirstVerify(path, "garbage from a failed write");

    private void CreateOnFirstVerify(string path, string content)
    {
        var done = false;
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "diff" && !done)
            {
                done = true;
                File.WriteAllText(path, content);
            }
        };
    }

    // profile-a and profile-b; the folder is left on profile-b.
    private async Task<(RepoRef Repo, SortedDictionary<string, string> A)> TwoProfilesAsync()
    {
        var repo = await _ws.InitializedRepoAsync();
        var random = new Random(7);
        var blob = new byte[48 * 1024];
        random.NextBytes(blob);
        _ws.Write(repo, "readme.txt", "version A\n");
        _ws.Write(repo, "Data/blob.bin", blob);
        _ws.Write(repo, "caf\u00e9/cr\u00e8me br\u00fbl\u00e9e.esp", "accent a");
        await _ws.CaptureAsync(repo, "profile-a");
        var a = TreeSnapshot.Take(repo.WorkTree);

        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        random.NextBytes(blob);
        _ws.Write(repo, "readme.txt", "version B\n");
        _ws.Write(repo, "Data/blob.bin", blob);
        _ws.Write(repo, "caf\u00e9/cr\u00e8me br\u00fbl\u00e9e.esp", "accent b");
        _ws.Write(repo, "only-b.txt", "B only");
        await _ws.CaptureAsync(repo, "profile-b");
        return (repo, a);
    }
}
