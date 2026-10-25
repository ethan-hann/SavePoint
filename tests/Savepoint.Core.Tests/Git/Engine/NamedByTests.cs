using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Asking git which files a pattern would leave out of a folder, without touching the folder, its saved patterns or its history.
[Collection(EngineCollections.F)]
public sealed class NamedByTests : IDisposable
{
    private static readonly string[] _rules = ["**/*.log", "**/*.dmp", "**/*.mdmp"];

    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private static string[] Of(IReadOnlyDictionary<string, IReadOnlyList<string>> found, string rule) => [.. found[rule]];

    [Fact]
    public async Task Each_pattern_gets_the_unrecorded_files_it_names()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.log", "x");
        _ws.Write(repo, "deep/er/b.dmp", "x");
        _ws.Write(repo, "c.txt", "x");

        var found = await _ws.Engine.ListNamedByAsync(repo, _rules, Ct);

        Assert.Equal(["a.log"], Of(found, "**/*.log"));
        Assert.Equal(["deep/er/b.dmp"], Of(found, "**/*.dmp"));
        Assert.Empty(Of(found, "**/*.mdmp"));
        Assert.Equal(_rules.Order(), found.Keys.Order());
    }

    [Fact]
    public async Task A_file_a_point_recorded_is_named_even_after_it_left_the_folder()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "journal.log", "recorded");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Delete(repo, "journal.log");
        await _ws.GitWriteAsync(repo, "update-index", "--force-remove", "--", "journal.log");
        Assert.Equal(string.Empty, await _ws.GitOutAsync(repo, "ls-files", "-z"));

        var found = await _ws.Engine.ListNamedByAsync(repo, ["**/*.log"], Ct);

        Assert.Equal(["journal.log"], Of(found, "**/*.log"));
    }

    [Fact]
    public async Task A_file_only_another_profile_recorded_is_named_while_this_profile_is_loaded()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "save.sav", "one");
        _ws.Write(repo, "journal.log", "only profile B has this");
        await _ws.CaptureAsync(repo, "profile-b");
        _ws.Delete(repo, "journal.log");
        await _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-a", "A", Guid.NewGuid().ToString()) { Mode = CaptureMode.Root }, null, Ct);
        Assert.DoesNotContain("journal.log", await _ws.TreeAsync(repo, "profile-a"));
        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));

        var found = await _ws.Engine.ListNamedByAsync(repo, ["**/*.log"], Ct);

        Assert.Equal(["journal.log"], Of(found, "**/*.log"));
    }

    [Fact]
    public async Task Names_with_spaces_and_non_ascii_letters_come_back_as_they_are()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "my saves/Spieler Üñí ☃.log", "x");
        _ws.Write(repo, "日本語/データ.dmp", "x");

        var found = await _ws.Engine.ListNamedByAsync(repo, _rules, Ct);

        Assert.Equal(["my saves/Spieler Üñí ☃.log"], Of(found, "**/*.log"));
        Assert.Equal(["日本語/データ.dmp"], Of(found, "**/*.dmp"));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public async Task A_name_in_another_letter_case_is_named_only_where_the_folder_ignores_case(string ignoreCase)
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.GitWriteAsync(repo, "config", "core.ignorecase", ignoreCase);
        _ws.Write(repo, "PLAYER.LOG", "x");
        var expected = ignoreCase == "true" ? new[] { "PLAYER.LOG" } : [];

        var listed = await _ws.Engine.ListNamedByAsync(repo, ["**/*.log"], Ct);
        var among = await _ws.Engine.NamedByAmongAsync(repo, ["**/*.log"], ["PLAYER.LOG"], Ct);

        Assert.Equal(expected, Of(listed, "**/*.log"));
        Assert.Equal(expected, among.Order());
    }

    [Fact]
    public async Task The_consent_marker_is_never_listed_even_when_a_pattern_would_match_it()
    {
        var repo = await _ws.InitializedRepoAsync();
        Assert.True(File.Exists(EngineWorkspace.MarkerPath(repo)));
        _ws.Write(repo, "settings.json", "x");

        var found = await _ws.Engine.ListNamedByAsync(repo, ["**/*.json", "*"], Ct);

        Assert.Equal(["settings.json"], Of(found, "**/*.json"));
        Assert.DoesNotContain(".savepoint-managed.json", found.Values.SelectMany(files => files));
    }

    [Fact]
    public async Task Asking_leaves_the_saved_patterns_byte_for_byte_and_no_working_file_behind()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.CaptureAsync(repo, "profile-1", "first", ignore: ["Saves/*.tmp"]);
        _ws.Write(repo, "x.log", "x");
        var before = File.ReadAllBytes(Path.Combine(repo.GitDir, "info", "exclude"));

        await _ws.Engine.ListNamedByAsync(repo, _rules, Ct);
        await _ws.Engine.NamedByAmongAsync(repo, _rules, ["x.log", "y.dmp"], Ct);

        Assert.Equal(before, File.ReadAllBytes(Path.Combine(repo.GitDir, "info", "exclude")));
        Assert.Empty(Directory.GetFileSystemEntries(repo.GitDir, "savepoint-classify-*"));
    }

    [Fact]
    public async Task A_failure_part_way_also_leaves_no_working_file_behind()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "x.log", "x");
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments.Contains("-o") && invocation.Arguments.Contains("-i"))
            {
                throw new IOException("the drive went away");
            }
        };

        await Assert.ThrowsAsync<IOException>(() => _ws.Engine.ListNamedByAsync(repo, _rules, Ct));

        Assert.Empty(Directory.GetFileSystemEntries(repo.GitDir, "savepoint-classify-*"));
    }

    [Fact]
    public async Task Blank_and_repeated_patterns_are_ignored_and_no_patterns_name_nothing()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.log", "x");

        var found = await _ws.Engine.ListNamedByAsync(repo, ["", "**/*.log", "**/*.log"], Ct);
        var none = await _ws.Engine.ListNamedByAsync(repo, [], Ct);

        Assert.Equal(["**/*.log"], found.Keys);
        Assert.Empty(none);
    }

    [Fact]
    public async Task A_pattern_with_a_line_break_is_refused()
    {
        var repo = await _ws.InitializedRepoAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _ws.Engine.ListNamedByAsync(repo, ["a\nb"], Ct));
    }
}
