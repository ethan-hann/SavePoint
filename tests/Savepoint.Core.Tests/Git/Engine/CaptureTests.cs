using System.Text.RegularExpressions;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

[Collection(EngineCollections.B)]
public sealed partial class CaptureTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task The_first_capture_commits_the_folder_on_the_named_branch_and_returns_the_full_commit_id()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/mod.esp", "mod");
        _ws.Write(repo, "game.exe", "exe");

        var outcome = await _ws.CaptureAsync(repo, "profile-1");

        Assert.True(outcome.Changed);
        Assert.Matches(FortyHex(), outcome.TipCommitId);
        Assert.Equal(outcome.TipCommitId, await _ws.TipAsync(repo, "profile-1"));
        Assert.Equal("refs/heads/profile-1", await _ws.HeadRefAsync(repo));
        Assert.Equal(["Data/mod.esp", "game.exe"], (await _ws.TreeAsync(repo, "profile-1")).Order());
    }

    [Fact]
    public async Task The_first_capture_leaves_no_branch_behind_under_git_s_default_name()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");

        await _ws.CaptureAsync(repo, "profile-1");

        Assert.Equal("refs/heads/profile-1", (await _ws.GitOutAsync(repo, "for-each-ref", "--format=%(refname)")).Trim());
    }

    [Fact]
    public async Task A_first_capture_of_a_folder_with_nothing_to_record_creates_no_branch()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "only.log", "ignored");

        var outcome = await _ws.CaptureAsync(repo, "profile-1", ignore: ["*.log"]);

        Assert.False(outcome.Changed);
        Assert.Null(outcome.TipCommitId);
        Assert.Equal(string.Empty, await _ws.GitOutAsync(repo, "for-each-ref", "--format=%(refname)"));
        Assert.NotEqual("refs/heads/profile-1", await _ws.HeadRefAsync(repo));
    }

    [Fact]
    public async Task Capturing_an_unchanged_folder_reports_nothing_changed_and_adds_no_commit()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var first = await _ws.CaptureAsync(repo, "profile-1");

        var second = await _ws.CaptureAsync(repo, "profile-1");

        Assert.False(second.Changed);
        Assert.Equal(first.TipCommitId, second.TipCommitId);
        Assert.Equal(1, await _ws.CountAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task A_second_capture_adds_a_commit_on_top_of_the_first()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var first = await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "a.txt", "changed");
        _ws.Write(repo, "b.txt", "new");

        var second = await _ws.CaptureAsync(repo, "profile-1");

        Assert.True(second.Changed);
        Assert.NotEqual(first.TipCommitId, second.TipCommitId);
        Assert.Equal(first.TipCommitId, await _ws.ResolveAsync(repo, "profile-1~1"));
        Assert.Equal(2, await _ws.CountAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task A_deleted_file_is_recorded_as_a_change()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        _ws.Write(repo, "b.txt", "b");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Delete(repo, "b.txt");

        var outcome = await _ws.CaptureAsync(repo, "profile-1");

        Assert.True(outcome.Changed);
        Assert.Equal(["a.txt"], await _ws.TreeAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task The_capture_id_is_written_as_a_trailer_and_the_message_is_the_subject()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var request = new CaptureRequest("profile-1", "Before the big patch", "5d2c7a0e-3f4b-4b0e-9d84-0a1b2c3d4e5f");

        await _ws.Engine.CaptureAsync(repo, request, ct: Ct);

        Assert.Equal("Before the big patch", await _ws.LogAsync(repo, "profile-1", "%s"));
        Assert.Equal(
            "5d2c7a0e-3f4b-4b0e-9d84-0a1b2c3d4e5f",
            await _ws.LogAsync(repo, "profile-1", "%(trailers:key=Savepoint-Capture,valueonly)"));
        Assert.EndsWith("\n\nSavepoint-Capture: 5d2c7a0e-3f4b-4b0e-9d84-0a1b2c3d4e5f", await _ws.LogAsync(repo, "profile-1", "%B"));
    }

    [Theory]
    [InlineData("#1 before the patch", "#1 before the patch")]
    [InlineData("Caf\u00e9 \u2013 \u65e5\u672c\u8a9e save", "Caf\u00e9 \u2013 \u65e5\u672c\u8a9e save")]
    [InlineData("  spaced   out  ", "spaced   out")]
    [InlineData("with \"quotes\" and 'apostrophes' and $dollars and %percent", "with \"quotes\" and 'apostrophes' and $dollars and %percent")]
    public async Task The_message_is_stored_without_surrounding_whitespace_and_otherwise_as_given(string message, string expected)
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");

        await _ws.CaptureAsync(repo, "profile-1", message);

        Assert.Equal(expected, await _ws.LogAsync(repo, "profile-1", "%s"));
    }

    [Fact]
    public async Task A_message_with_several_lines_is_stored_verbatim()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");

        await _ws.CaptureAsync(repo, "profile-1", "first line\n\n\n  indented   \nlast line");

        Assert.StartsWith(
            "first line\n\n\n  indented   \nlast line\n\nSavepoint-Capture: ",
            await _ws.LogAsync(repo, "profile-1", "%B"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_consent_marker_and_its_temp_file_are_never_recorded()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        _ws.Write(repo, ".savepoint-managed.json.tmp", "half written");

        await _ws.CaptureAsync(repo, "profile-1");

        Assert.Equal(["a.txt"], await _ws.TreeAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task Ignore_patterns_keep_matching_files_out_and_the_work_tree_gets_no_ignore_file()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Saves/slot1.sav", "s");
        _ws.Write(repo, "Logs/today.log", "l");
        _ws.Write(repo, "Cache/tmp.bin", "c");

        await _ws.CaptureAsync(repo, "profile-1", ignore: ["*.log", "Cache/"]);

        Assert.Equal(["Saves/slot1.sav"], await _ws.TreeAsync(repo, "profile-1"));
        Assert.False(File.Exists(_ws.FileOf(repo, ".gitignore")));
    }

    [Fact]
    public async Task A_shorter_ignore_list_applies_to_the_next_capture_with_no_stale_rules()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Saves/slot1.sav", "s");
        _ws.Write(repo, "Logs/today.log", "l");
        await _ws.CaptureAsync(repo, "profile-1", ignore: ["*.log"]);

        await _ws.CaptureAsync(repo, "profile-1", ignore: []);

        Assert.Equal(["Logs/today.log", "Saves/slot1.sav"], (await _ws.TreeAsync(repo, "profile-1")).Order());
    }

    [Fact]
    public async Task A_pattern_that_tries_to_re_include_the_marker_does_not_get_it_recorded()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");

        await _ws.CaptureAsync(repo, "profile-1", ignore: ["!.savepoint-managed.json", "!*"]);

        Assert.Equal(["a.txt"], await _ws.TreeAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task A_gitattributes_file_in_the_game_folder_does_not_change_what_is_stored()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, ".gitattributes", "* text eol=crlf\n*.txt ident\n");
        var lf = "line one\nline two\n$Id$\n"u8.ToArray();
        var crlf = "dos\r\nfile\r\n"u8.ToArray();
        _ws.Write(repo, "lf.txt", lf);
        _ws.Write(repo, "crlf.dat", crlf);

        await _ws.CaptureAsync(repo, "profile-1");

        Assert.Equal(lf, await ReadBlobAsync(repo, "profile-1:lf.txt"));
        Assert.Equal(crlf, await ReadBlobAsync(repo, "profile-1:crlf.dat"));
    }

    [Fact]
    public async Task Capturing_a_branch_that_is_not_checked_out_is_refused_and_touches_no_file()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "one");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "a.txt", "edited on disk");

        var error = await Assert.ThrowsAsync<GitOperationException>(() => _ws.CaptureAsync(repo, "profile-2"));

        Assert.Contains("profile-2", error.Message, StringComparison.Ordinal);
        Assert.Equal("edited on disk", await File.ReadAllTextAsync(_ws.FileOf(repo, "a.txt"), Ct));
        Assert.Equal("refs/heads/profile-1", await _ws.HeadRefAsync(repo));
        Assert.Null(await _ws.TipAsync(repo, "profile-2"));
    }

    [Fact]
    public async Task Capturing_from_an_older_point_is_refused_for_a_normal_capture()
    {
        var repo = await _ws.InitializedRepoAsync("game-1", RepoRole.External);
        _ws.Write(repo, "a.txt", "one");
        var first = await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "a.txt", "two");
        await _ws.CaptureAsync(repo, "profile-1");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "--detach", first.TipCommitId!);

        var error = await Assert.ThrowsAsync<DetachedCaptureNotSupportedException>(() => _ws.CaptureAsync(repo, "profile-1"));

        Assert.Equal("error.launch.detachedCaptureNotSupported", error.MessageId);
        Assert.Equal(RepoRole.External, error.Args["role"]);
    }

    [Fact]
    public async Task A_stale_index_lock_is_cleared_and_the_capture_succeeds()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var lockFile = Path.Combine(repo.GitDir, "index.lock");
        await File.WriteAllTextAsync(lockFile, string.Empty, Ct);

        var outcome = await _ws.CaptureAsync(repo, "profile-1");

        Assert.True(outcome.Changed);
        Assert.False(File.Exists(lockFile));
    }

    [Fact]
    public async Task A_capture_re_applies_the_repo_config_on_a_repo_nobody_stamped()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.GitAsync(repo, "config", "--local", "--unset", "savepoint.repoConfigVersion");
        await _ws.GitAsync(repo, "config", "--local", "maintenance.auto", "true");
        await _ws.GitAsync(repo, "config", "--local", "core.untrackedCache", "false");
        _ws.Write(repo, "a.txt", "a");

        await _ws.CaptureAsync(repo, "profile-1");

        Assert.Equal("false", await _ws.ConfigAsync(repo, "maintenance.auto"));
        Assert.Equal("true", await _ws.ConfigAsync(repo, "core.untrackedCache"));
        Assert.Equal("2", await _ws.ConfigAsync(repo, "savepoint.repoConfigVersion"));
    }

    [Fact]
    public async Task A_capture_never_starts_git_maintenance_or_a_detached_process()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");

        await _ws.CaptureAsync(repo, "profile-1");

        var arguments = _ws.Invocations.SelectMany(invocation => invocation.Arguments).ToArray();
        Assert.DoesNotContain("--detach", arguments);
        Assert.DoesNotContain("--auto", arguments);
        Assert.DoesNotContain(arguments, argument => argument is "gc" or "maintenance");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a..b")]
    [InlineData("-leading-dash")]
    [InlineData("has space")]
    [InlineData("ends.lock")]
    [InlineData("trailing/")]
    [InlineData("/leading")]
    [InlineData("a//b")]
    [InlineData("tilde~1")]
    [InlineData("colon:name")]
    [InlineData("HEAD")]
    [InlineData("@")]
    [InlineData("new\nline")]
    [InlineData("at@{brace")]
    [InlineData("what?")]
    [InlineData("star*")]
    [InlineData("open[bracket")]
    [InlineData("back\\slash")]
    [InlineData("caret^")]
    [InlineData("ends-with-dot.")]
    [InlineData(".starts-with-dot")]
    [InlineData("folder/.hidden")]
    [InlineData("folder/part.lock/more")]
    [InlineData("tab\there")]
    public void A_branch_name_git_would_refuse_is_rejected_before_any_work(string branch)
    {
        Assert.Throws<ArgumentException>(() => new CaptureRequest(branch, "message", "id"));
    }

    [Theory]
    [InlineData("profile-1")]
    [InlineData("profiles/a1b2c3")]
    [InlineData("Caf\u00e9")]
    [InlineData("a.b")]
    public void A_branch_name_git_accepts_is_accepted(string branch)
    {
        var request = new CaptureRequest(branch, "message", "id");

        Assert.Equal(branch, request.Branch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("line\nbreak")]
    public void A_capture_id_that_cannot_be_a_trailer_value_is_rejected(string captureId)
    {
        Assert.Throws<ArgumentException>(() => new CaptureRequest("profile-1", "message", captureId));
    }

    [Fact]
    public void A_message_with_a_nul_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new CaptureRequest("profile-1", "bad\0message", "id"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_message_is_rejected_because_the_trailer_would_become_the_subject(string message)
    {
        Assert.Throws<ArgumentException>(() => new CaptureRequest("profile-1", message, "id"));
    }

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FortyHex();

    private async Task<byte[]> ReadBlobAsync(RepoRef repo, string spec)
    {
        var result = await _ws.Runner.RunAsync(
            new GitInvocation(["cat-file", "blob", spec]) { GitDir = repo.GitDir, WorkTree = repo.WorkTree, ReadOnly = true, BinaryStdout = true },
            Ct);
        return result.StandardOutputBytes;
    }
}
