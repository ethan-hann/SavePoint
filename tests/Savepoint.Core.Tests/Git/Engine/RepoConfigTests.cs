using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Git.Engine;

[Collection(EngineCollections.B)]
public sealed class RepoConfigTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    public static TheoryData<string, string> ExpectedSettings => new()
    {
        { "maintenance.auto", "false" },
        { "gc.auto", "0" },
        { "core.loosecompression", "0" },
        { "core.untrackedcache", "true" },
        { "index.version", "4" },
        { "checkout.workers", "0" },
        { "core.longpaths", "true" },
        { "core.quotepath", "false" },
        { "core.autocrlf", "false" },
        { "core.bigfilethreshold", "128m" },
        { "core.logallrefupdates", "false" },
        { "savepoint.repoconfigversion", "3" },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Theory]
    [MemberData(nameof(ExpectedSettings))]
    public async Task Init_applies_the_setting_and_git_reads_it_back(string key, string expected)
    {
        var repo = await _ws.InitializedRepoAsync();

        Assert.Equal(expected, await _ws.ConfigAsync(repo, key));
    }

    [Theory]
    [MemberData(nameof(ExpectedSettings))]
    public async Task Each_setting_is_defined_once_so_git_config_can_still_change_it(string key, string expected)
    {
        var repo = await _ws.InitializedRepoAsync();

        var all = await _ws.GitAsync(repo, "config", "--local", "--get-all", key);

        Assert.Equal([expected], all.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    [Fact]
    public async Task Init_turns_symlinks_off_on_windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "symlinks are only switched off on Windows");
        var repo = await _ws.InitializedRepoAsync();

        var all = await _ws.GitAsync(repo, "config", "--local", "--get-all", "core.symlinks");

        Assert.Equal("false", await _ws.ConfigAsync(repo, "core.symlinks"));
        Assert.Equal(["false"], all.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    [Fact]
    public async Task Reapplying_turns_symlinks_off_even_when_git_found_them_working()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "symlinks are only switched off on Windows");
        var repo = await _ws.InitializedRepoAsync();
        await _ws.GitAsync(repo, "config", "--local", "core.symlinks", "true");
        await _ws.GitAsync(repo, "config", "--local", "savepoint.repoConfigVersion", "1");

        await _ws.Engine.InitAsync(repo, ct: Ct);

        Assert.Equal("false", await _ws.ConfigAsync(repo, "core.symlinks"));
    }

    [Fact]
    public async Task Init_does_not_write_a_symlinks_setting_that_overrides_git_on_other_systems()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "on Windows the setting is written on purpose");
        var repo = await _ws.InitializedRepoAsync();

        Assert.NotEqual("false", await _ws.ConfigAsync(repo, "core.symlinks"));
    }

    [Fact]
    public async Task Captures_and_switches_leave_no_reflog_behind()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        await _ws.Engine.CheckoutCommitAsync(repo, points[0], ct: TestContext.Current.CancellationToken);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-1", ct: TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(Path.Combine(repo.GitDir, "logs")), "no reflog is kept: nothing in SavePoint reads one");
    }

    [Fact]
    public async Task Init_leaves_no_worktree_path_in_the_config()
    {
        var repo = await _ws.InitializedRepoAsync();

        Assert.Null(await _ws.ConfigAsync(repo, "core.worktree"));
        Assert.Equal("false", await _ws.ConfigAsync(repo, "core.bare"));
    }

    [Fact]
    public async Task Init_keeps_what_git_decided_about_the_file_system()
    {
        var repo = await _ws.InitializedRepoAsync();

        Assert.Equal("0", await _ws.ConfigAsync(repo, "core.repositoryformatversion"));
        Assert.NotNull(await _ws.ConfigAsync(repo, "core.ignorecase"));
    }

    [Fact]
    public async Task Init_again_reapplies_the_settings_when_the_stamp_differs()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.GitAsync(repo, "config", "--local", "savepoint.repoConfigVersion", "1");
        await _ws.GitAsync(repo, "config", "--local", "core.untrackedCache", "false");
        await _ws.GitAsync(repo, "config", "--local", "maintenance.auto", "true");

        await _ws.Engine.InitAsync(repo, ct: Ct);

        Assert.Equal("3", await _ws.ConfigAsync(repo, "savepoint.repoConfigVersion"));
        Assert.Equal("true", await _ws.ConfigAsync(repo, "core.untrackedCache"));
        Assert.Equal("false", await _ws.ConfigAsync(repo, "maintenance.auto"));
    }

    [Fact]
    public async Task Init_again_leaves_the_config_alone_when_the_stamp_matches()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.GitAsync(repo, "config", "--local", "core.untrackedCache", "false");

        await _ws.Engine.InitAsync(repo, ct: Ct);

        Assert.Equal("false", await _ws.ConfigAsync(repo, "core.untrackedCache"));
    }

    [Fact]
    public async Task Reapplying_replaces_the_managed_block_instead_of_adding_another()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.GitAsync(repo, "config", "--local", "savepoint.repoConfigVersion", "1");

        await _ws.Engine.InitAsync(repo, ct: Ct);
        await _ws.GitAsync(repo, "config", "--local", "savepoint.repoConfigVersion", "1");
        await _ws.Engine.InitAsync(repo, ct: Ct);

        var text = EngineWorkspace.GitDirText(repo, "config");
        Assert.Equal(1, text.Split('\n').Count(line => line.Contains("savepoint managed", StringComparison.Ordinal) && line.Contains(">>>", StringComparison.Ordinal)));
        Assert.Equal(1, text.Split('\n').Count(line => line.Contains("repoConfigVersion", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("user", "note", "user.note")]
    [InlineData("core", "fsmonitor", "core.fsmonitor")]
    [InlineData("core", "hooksPath", "core.hooksPath")]
    [InlineData("core", "sshCommand", "core.sshCommand")]
    [InlineData("core", "pager", "core.pager")]
    [InlineData("alias", "st", "alias.st")]
    [InlineData("include", "path", "include.path")]
    [InlineData("filter \"lfs\"", "clean", "filter.lfs.clean")]
    [InlineData("remote \"origin\"", "url", "remote.origin.url")]
    [InlineData("credential", "helper", "credential.helper")]
    public async Task Reapplying_drops_a_setting_that_git_init_does_not_write_and_that_is_not_its_own(string section, string key, string name)
    {
        var repo = await _ws.InitializedRepoAsync();

        // Added to the end of the file, the way a repository from somewhere else carries it, not inside the managed block.
        await File.AppendAllTextAsync(Path.Combine(repo.GitDir, "config"), $"[{section}]\n\t{key} = something\n", Ct);
        await _ws.GitAsync(repo, "config", "--local", "savepoint.repoConfigVersion", "1");

        await _ws.Engine.InitAsync(repo, ct: Ct);

        Assert.Null(await _ws.ConfigAsync(repo, name));
        Assert.Equal("0", await _ws.ConfigAsync(repo, "core.repositoryformatversion"));
        Assert.NotNull(await _ws.ConfigAsync(repo, "core.ignorecase"));
        Assert.Equal("false", await _ws.ConfigAsync(repo, "core.bare"));
    }

    [Fact]
    public void A_rewrite_keeps_exactly_the_keys_git_init_writes_plus_the_managed_block_whatever_the_file_held()
    {
        const string hostile = "[core]\n\trepositoryformatversion = 0\n\tfilemode = false\n\tbare = false\n\tignorecase = true\n\tFSMonitor = evil #\n\thooksPath = /tmp/evil\n[Alias]\n\tx = !evil\n[include]\n\tpath = /tmp/evil\n[includeIf \"gitdir:/\"]\n\tpath = /tmp/evil\n[core]\n\tpager = evil\n\tsymlinks = false\n[savepoint]\n\trepoConfigVersion = 3\n";

        var rewritten = Savepoint.Core.Git.RepoConfigFile.Rewrite(hostile, isWindows: false);

        Assert.DoesNotContain("evil", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[Alias]", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("[include", rewritten, StringComparison.Ordinal);
        Assert.Contains("repositoryformatversion = 0", rewritten, StringComparison.Ordinal);
        Assert.Contains("filemode = false", rewritten, StringComparison.Ordinal);
        Assert.Contains("ignorecase = true", rewritten, StringComparison.Ordinal);
        Assert.Contains("symlinks = false", rewritten, StringComparison.Ordinal);
        Assert.Contains(Savepoint.Core.Git.RepoConfigFile.BlockStart, rewritten, StringComparison.Ordinal);
        Assert.Equal(1, rewritten.Split('\n').Count(line => line.Contains("repoConfigVersion", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_config_that_cannot_be_read_is_an_error_and_is_not_rebuilt_from_nothing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets a file be replaced while it cannot be read");
        var repo = await _ws.InitializedRepoAsync();
        await _ws.GitAsync(repo, "config", "--local", "savepoint.repoConfigVersion", "1");
        var path = Path.Combine(repo.GitDir, "config");
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        Exception? error;
        using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Delete))
        {
            error = await Record.ExceptionAsync(() => _ws.Engine.InitAsync(repo, ct: TestContext.Current.CancellationToken));
        }

        Assert.IsType<GitOperationException>(error);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_config_nobody_stamped_is_applied_in_full()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.GitAsync(repo, "config", "--local", "--unset", "savepoint.repoConfigVersion");
        await _ws.GitAsync(repo, "config", "--local", "--unset", "core.bigFileThreshold");

        await _ws.Engine.InitAsync(repo, ct: Ct);

        Assert.Equal("128m", await _ws.ConfigAsync(repo, "core.bigFileThreshold"));
        Assert.Equal("3", await _ws.ConfigAsync(repo, "savepoint.repoConfigVersion"));
    }
}
