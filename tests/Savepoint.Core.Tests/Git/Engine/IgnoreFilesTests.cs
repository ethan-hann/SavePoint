using Savepoint.Core.Git;


namespace Savepoint.Core.Tests.Git.Engine;

[Collection(EngineCollections.F)]
public sealed class IgnoreFilesTests : IDisposable
{
    private static readonly string[] _compressedExtensions =
    [
        "zip", "7z", "rar", "gz", "xz", "zst", "cab",
        "mp4", "webm", "mkv", "bik", "bk2", "usm", "ogg", "mp3", "flac",
        "pak", "ucas", "utoc", "bsa", "ba2", "archive", "vpk", "pck",
        "bundle", "unity3d", "assetbundle", "resource",
    ];

    // Unity's serialized data and streamed textures compress well and change a little between versions, so they keep both.
    private static readonly string[] _unityDataExtensions = ["assets", "resS", "sharedAssets"];

    private readonly EngineWorkspace _ws = new();

    public static TheoryData<string> CompressedExtensions => [.. _compressedExtensions];

    public static TheoryData<string> UnityDataExtensions => [.. _unityDataExtensions];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public void The_exclude_lines_are_the_patterns_then_the_marker_and_are_what_the_file_holds()
    {
        string[] patterns = ["keep/", string.Empty, "*.log"];

        var lines = RepoIgnoreFiles.ExcludeLines(patterns);

        Assert.Equal(["keep/", "*.log", "/.savepoint-managed.json*"], lines);
        Assert.Equal(lines, RepoIgnoreFiles.ExcludeText(patterns).Split('\n').Where(line => line.Length > 0 && line[0] != '#'));
    }

    [Fact]
    public async Task Init_excludes_only_the_marker_pattern()
    {
        var repo = await _ws.InitializedRepoAsync();

        var patterns = (EngineWorkspace.GitDirLines(repo, "info/exclude"))
            .Where(line => line.Length > 0 && !line.StartsWith('#'));
        Assert.Equal(["/.savepoint-managed.json*"], patterns);
    }

    [Fact]
    public async Task The_marker_and_its_temp_file_are_ignored_by_git()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, ".savepoint-managed.json.tmp", "half written");

        Assert.Equal(string.Empty, await _ws.GitOutAsync(repo, "ls-files", "-z", "--others", "--exclude-standard"));
    }

    [Fact]
    public async Task Init_writes_no_ignore_or_attributes_file_into_the_work_tree()
    {
        var repo = await _ws.InitializedRepoAsync();

        Assert.False(File.Exists(_ws.FileOf(repo, ".gitignore")));
        Assert.False(File.Exists(_ws.FileOf(repo, ".gitattributes")));
        Assert.False(Directory.Exists(_ws.FileOf(repo, ".git")));
    }

    [Fact]
    public async Task Attributes_stop_any_content_rewriting_for_every_path()
    {
        var repo = await _ws.InitializedRepoAsync();

        var lines = (await _ws.GitOutAsync(repo, "check-attr", "text", "filter", "diff", "ident", "working-tree-encoding", "--", "mods/readme.txt"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("mods/readme.txt: text: unset", lines);
        Assert.Contains("mods/readme.txt: filter: unset", lines);
        Assert.Contains("mods/readme.txt: diff: unset", lines);
        Assert.Contains("mods/readme.txt: ident: unset", lines);
        Assert.Contains("mods/readme.txt: working-tree-encoding: unspecified", lines);
    }

    [Fact]
    public async Task Attributes_beat_a_gitattributes_file_inside_the_game_folder()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, ".gitattributes", "* text eol=crlf\n*.txt ident\n*.u16 working-tree-encoding=UTF-16\n*.flt filter=foo\n");

        var lines = (await _ws.GitOutAsync(repo, "check-attr", "text", "filter", "ident", "working-tree-encoding", "--", "a.txt", "b.u16", "c.flt"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.DoesNotContain(lines, line => line.Contains(": text: set", StringComparison.Ordinal) || line.Contains(": text: auto", StringComparison.Ordinal));
        Assert.Contains("a.txt: ident: unset", lines);
        Assert.Contains("b.u16: working-tree-encoding: unspecified", lines);
        Assert.Contains("c.flt: filter: unset", lines);
    }

    [Theory]
    [MemberData(nameof(CompressedExtensions))]
    public async Task Already_compressed_formats_skip_delta_compression(string extension)
    {
        var repo = await _ws.InitializedRepoAsync();

        var line = await _ws.GitOutAsync(repo, "check-attr", "delta", "--", $"data/big.{extension}");

        Assert.Equal($"data/big.{extension}: delta: unset", line);
    }

    [Theory]
    [MemberData(nameof(UnityDataExtensions))]
    public async Task Unity_data_that_compresses_keeps_delta_compression(string extension)
    {
        var repo = await _ws.InitializedRepoAsync();

        var line = await _ws.GitOutAsync(repo, "check-attr", "delta", "--", $"data/big.{extension}");

        Assert.Equal($"data/big.{extension}: delta: unspecified", line);
    }

    [Fact]
    public async Task Other_files_keep_delta_compression()
    {
        var repo = await _ws.InitializedRepoAsync();

        var line = await _ws.GitOutAsync(repo, "check-attr", "delta", "--", "data/config.ini");

        Assert.Equal("data/config.ini: delta: unspecified", line);
    }

    [Fact]
    public async Task Init_writes_the_attributes_into_the_git_directory()
    {
        var repo = await _ws.InitializedRepoAsync();

        var first = (EngineWorkspace.GitDirLines(repo, "info/attributes")).First(line => !line.StartsWith('#'));
        Assert.Equal("* -text -filter -diff -ident !working-tree-encoding", first);
    }

    [Fact]
    public async Task Exclude_patterns_are_rewritten_from_the_current_list_with_the_marker_last()
    {
        var repo = await _ws.InitializedRepoAsync();

        await _ws.Engine.WriteExcludeAsync(repo, ["*.log", "!keep.log", "Cache/"], Ct);
        await _ws.Engine.WriteExcludeAsync(repo, ["*.tmp"], Ct);

        var patterns = (EngineWorkspace.GitDirLines(repo, "info/exclude"))
            .Where(line => line.Length > 0 && !line.StartsWith('#'));
        Assert.Equal(["*.tmp", "/.savepoint-managed.json*"], patterns);
    }

    [Fact]
    public async Task A_negated_pattern_cannot_bring_the_marker_back()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.Engine.WriteExcludeAsync(repo, ["!.savepoint-managed.json", "!*"], Ct);

        Assert.Equal(string.Empty, await _ws.GitOutAsync(repo, "ls-files", "-z", "--others", "--exclude-standard"));
    }

    [Fact]
    public async Task Exclude_patterns_hide_matching_files_from_git()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.log", "x");
        _ws.Write(repo, "saves/slot1.sav", "y");

        await _ws.Engine.WriteExcludeAsync(repo, ["*.log"], Ct);

        Assert.Equal("saves/slot1.sav", (await _ws.GitOutAsync(repo, "ls-files", "--others", "--exclude-standard")).Trim());
    }

    [Theory]
    [InlineData("two\nlines")]
    [InlineData("nul\0inside")]
    public async Task A_pattern_that_cannot_be_one_line_is_refused_and_nothing_is_written(string pattern)
    {
        var repo = await _ws.InitializedRepoAsync();
        var before = EngineWorkspace.GitDirText(repo, "info/exclude");

        await Assert.ThrowsAsync<ArgumentException>(() => _ws.Engine.WriteExcludeAsync(repo, [pattern], Ct));

        Assert.Equal(before, EngineWorkspace.GitDirText(repo, "info/exclude"));
    }

    [Fact]
    public async Task Rewriting_the_same_list_does_not_touch_the_file()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.Engine.WriteExcludeAsync(repo, ["*.log"], Ct);
        var file = Path.Combine(repo.GitDir, "info", "exclude");
        Assert.True(File.Exists(file), $"expected the engine to have written {file}");
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, stamp);

        await _ws.Engine.WriteExcludeAsync(repo, ["*.log"], Ct);

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(file));
    }
}
