using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

public sealed class GitPathspecTests : IDisposable
{
    private readonly GitWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public void Paths_move_from_the_command_line_to_nul_separated_stdin()
    {
        var original = new GitInvocation(["add", "-A"]);

        var moved = original.WithPathspecs(["a.txt", "dir/b c.txt"]);

        Assert.Equal<IEnumerable<string>>(new[] { "add", "-A", "--pathspec-from-file=-", "--pathspec-file-nul" }, moved.Arguments);
        Assert.Equal("a.txt\0dir/b c.txt\0", moved.Stdin);
        Assert.Equal<IEnumerable<string>>(new[] { "add", "-A" }, original.Arguments);
        Assert.Null(original.Stdin);
    }

    [Fact]
    public void Paths_are_read_literally_so_a_name_is_never_taken_for_a_pattern()
    {
        var moved = new GitInvocation(["add"]).WithPathspecs(["[x].txt"]);

        Assert.Equal("1", moved.ExtraEnvironment?.GetValueOrDefault("GIT_LITERAL_PATHSPECS"));
    }

    [Fact]
    public void Existing_extra_environment_is_kept()
    {
        var original = new GitInvocation(["add"]) { ExtraEnvironment = new Dictionary<string, string?> { ["KEEP"] = "me" } };

        var moved = original.WithPathspecs(["a.txt"]);

        Assert.Equal("me", moved.ExtraEnvironment?.GetValueOrDefault("KEEP"));
    }

    [Fact]
    public void Paths_cannot_be_combined_with_another_stdin()
    {
        var invocation = new GitInvocation(["add"]) { Stdin = "already" };

        Assert.Throws<InvalidOperationException>(() => invocation.WithPathspecs(["a.txt"]));
    }

    [Fact]
    public void An_empty_path_list_is_refused_because_git_would_read_it_as_every_path()
    {
        var invocation = new GitInvocation(["add", "-A"]);

        Assert.Throws<ArgumentException>(() => invocation.WithPathspecs([]));
    }

    [Fact]
    public void A_path_with_a_nul_character_is_refused()
    {
        var invocation = new GitInvocation(["add", "-A"]);

        Assert.Throws<ArgumentException>(() => invocation.WithPathspecs(["bad\0name"]));
    }

    [Fact]
    public async Task Only_the_listed_names_are_staged_and_brackets_are_not_patterns()
    {
        await _ws.InitAsync();
        foreach (var name in new[] { "[x].txt", "x.txt", "a[b]c.txt", "caf\u00e9.esp", "other.txt" })
        {
            _ws.Write(name, "content");
        }

        _ws.Write("dir/inside.txt", "content");

        await _ws.RunAsync(new GitInvocation(["add", "-A"]).WithPathspecs(["[x].txt", "a[b]c.txt", "caf\u00e9.esp", "dir"]));
        var staged = (await _ws.RunAsync("ls-files", "-z")).SplitStandardOutputOnNul();

        Assert.Equal(new[] { "[x].txt", "a[b]c.txt", "caf\u00e9.esp", "dir/inside.txt" }.Order(StringComparer.Ordinal), staged.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_path_list_far_beyond_the_command_line_limit_goes_through()
    {
        await _ws.InitAsync();
        const int count = 3000;
        var names = Enumerable.Range(0, count).Select(i => new string('n', 90) + i.ToString("D5") + ".txt").ToList();
        foreach (var name in names)
        {
            _ws.Write(name, "content");
        }

        Assert.True(names.Sum(name => name.Length + 1) > 200_000);

        await _ws.RunAsync(new GitInvocation(["add", "-A"]).WithPathspecs(names));
        var staged = (await _ws.RunAsync("ls-files", "-z")).SplitStandardOutputOnNul();

        Assert.Equal(count, staged.Count);
    }

    [Fact]
    public async Task Checkout_can_restore_a_long_path_list_from_a_commit()
    {
        await _ws.InitAsync();
        var names = Enumerable.Range(0, 400).Select(i => new string('c', 90) + i.ToString("D4") + ".txt").ToList();
        foreach (var name in names)
        {
            _ws.Write(name, "original");
        }

        await _ws.RunAsync("add", "-A");
        await _ws.RunAsync("commit", "-m", "base");
        foreach (var name in names)
        {
            _ws.Write(name, "changed");
        }

        await _ws.RunAsync(new GitInvocation(["checkout", "HEAD"]).WithPathspecs(names));

        Assert.All(names, name => Assert.Equal("original", File.ReadAllText(Path.Combine(_ws.WorkTree, name))));
    }
}
