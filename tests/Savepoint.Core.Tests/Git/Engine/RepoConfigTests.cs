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
        { "savepoint.repoconfigversion", "2" },
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

        Assert.Equal("2", await _ws.ConfigAsync(repo, "savepoint.repoConfigVersion"));
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

    [Fact]
    public async Task Reapplying_keeps_settings_other_than_its_own()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.GitAsync(repo, "config", "--local", "user.note", "kept");
        await _ws.GitAsync(repo, "config", "--local", "savepoint.repoConfigVersion", "1");

        await _ws.Engine.InitAsync(repo, ct: Ct);

        Assert.Equal("kept", await _ws.ConfigAsync(repo, "user.note"));
        Assert.Equal("0", await _ws.ConfigAsync(repo, "core.repositoryformatversion"));
    }

    [Fact]
    public async Task A_config_nobody_stamped_is_applied_in_full()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.GitAsync(repo, "config", "--local", "--unset", "savepoint.repoConfigVersion");
        await _ws.GitAsync(repo, "config", "--local", "--unset", "core.bigFileThreshold");

        await _ws.Engine.InitAsync(repo, ct: Ct);

        Assert.Equal("128m", await _ws.ConfigAsync(repo, "core.bigFileThreshold"));
        Assert.Equal("2", await _ws.ConfigAsync(repo, "savepoint.repoConfigVersion"));
    }
}
