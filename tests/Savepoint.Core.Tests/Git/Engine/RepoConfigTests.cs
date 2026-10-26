using Savepoint.Core.Errors;
using Savepoint.Core.Git;

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
        { "core.trustctime", "false" },
        { "index.version", "4" },
        { "checkout.workers", "0" },
        { "core.longpaths", "true" },
        { "core.quotepath", "false" },
        { "core.autocrlf", "false" },
        { "core.bigfilethreshold", "128m" },
        { "core.logallrefupdates", "false" },
        { "pack.compression", "0" },
        { "core.fsync", "committed,index,reference" },
        { "core.fsyncmethod", "batch" },
        { "savepoint.repoconfigversion", "7" },
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
        Assert.Equal(await _ws.PlainInitConfigAsync("core.ignorecase"), await _ws.ConfigAsync(repo, "core.ignorecase"));
    }

    [Fact]
    public async Task Init_again_reapplies_the_settings_when_the_stamp_differs()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.GitAsync(repo, "config", "--local", "savepoint.repoConfigVersion", "1");
        await _ws.GitAsync(repo, "config", "--local", "core.untrackedCache", "false");
        await _ws.GitAsync(repo, "config", "--local", "maintenance.auto", "true");

        await _ws.Engine.InitAsync(repo, ct: Ct);

        Assert.Equal("7", await _ws.ConfigAsync(repo, "savepoint.repoConfigVersion"));
        Assert.Equal("true", await _ws.ConfigAsync(repo, "core.untrackedCache"));
        Assert.Equal("false", await _ws.ConfigAsync(repo, "maintenance.auto"));
    }

    [Fact]
    public async Task The_managed_block_opens_each_section_once()
    {
        var repo = await _ws.InitializedRepoAsync();

        var sections = (await File.ReadAllLinesAsync(Path.Combine(repo.GitDir, "config"), Ct))
            .SkipWhile(line => line.Trim() != RepoConfigFile.BlockStart)
            .TakeWhile(line => line.Trim() != RepoConfigFile.BlockEnd)
            .Where(line => line.StartsWith('['))
            .ToArray();

        Assert.Equal(sections.Distinct().Count(), sections.Length);
    }

    [Fact]
    public async Task A_repository_stamped_5_takes_pack_compression_off_the_next_time_it_is_touched()
    {
        var repo = await _ws.InitializedRepoAsync();
        var path = Path.Combine(repo.GitDir, "config");
        var text = await File.ReadAllTextAsync(path, Ct);
        await File.WriteAllTextAsync(path, text.Replace("\tcompression = 0\n", string.Empty, StringComparison.Ordinal).Replace("[pack]\n", string.Empty, StringComparison.Ordinal).Replace("repoConfigVersion = 7", "repoConfigVersion = 5", StringComparison.Ordinal), Ct);
        Assert.Null(await _ws.ConfigAsync(repo, "pack.compression"));
        Assert.Equal("5", await _ws.ConfigAsync(repo, "savepoint.repoConfigVersion"));

        await _ws.Engine.InitAsync(repo, ct: Ct);

        Assert.Equal("0", await _ws.ConfigAsync(repo, "pack.compression"));
        Assert.Equal("7", await _ws.ConfigAsync(repo, "savepoint.repoConfigVersion"));
    }

    [Fact]
    public async Task A_repository_stamped_6_takes_the_flush_settings_the_next_time_it_is_touched()
    {
        var repo = await _ws.InitializedRepoAsync();
        var path = Path.Combine(repo.GitDir, "config");
        var text = await File.ReadAllTextAsync(path, Ct);
        await File.WriteAllTextAsync(path, text.Replace("\tfsync = committed,index,reference\n", string.Empty, StringComparison.Ordinal).Replace("\tfsyncMethod = batch\n", string.Empty, StringComparison.Ordinal).Replace("repoConfigVersion = 7", "repoConfigVersion = 6", StringComparison.Ordinal), Ct);
        Assert.Null(await _ws.ConfigAsync(repo, "core.fsync"));
        Assert.Null(await _ws.ConfigAsync(repo, "core.fsyncMethod"));
        Assert.Equal("6", await _ws.ConfigAsync(repo, "savepoint.repoConfigVersion"));

        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-1");

        Assert.Equal("committed,index,reference", await _ws.ConfigAsync(repo, "core.fsync"));
        Assert.Equal("batch", await _ws.ConfigAsync(repo, "core.fsyncMethod"));
        Assert.Equal("7", await _ws.ConfigAsync(repo, "savepoint.repoConfigVersion"));
    }

    [Fact]
    public async Task Init_again_leaves_a_config_alone_when_it_is_what_SavePoint_wrote()
    {
        var repo = await _ws.InitializedRepoAsync();
        var path = Path.Combine(repo.GitDir, "config");
        var old = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, old);

        await _ws.Engine.InitAsync(repo, ct: Ct);

        Assert.Equal(old, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task Init_again_puts_back_a_value_in_the_managed_block_that_someone_changed()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.GitAsync(repo, "config", "--local", "core.untrackedCache", "false");

        await _ws.Engine.InitAsync(repo, ct: Ct);

        Assert.Equal("true", await _ws.ConfigAsync(repo, "core.untrackedCache"));
    }

    // A repository from somewhere else can carry the current stamp and still hold a setting that names a program, so the stamp alone
    // never says a config is current.
    [Theory]
    [InlineData("\n[commit]\n\tgpgSign = true\n[gpg]\n\tprogram = evil\n")]
    [InlineData("\n[core]\n\tfsmonitor = evil\n")]
    [InlineData("\n[core]\n\tpager = evil\n")]
    [InlineData("\n[alias]\n\tst = !evil\n")]
    [InlineData("\n[include]\n\tpath = evil\n")]
    [InlineData("\n[core \"x\"]\n\tpager = evil\n")]
    [InlineData("\n[commit] gpgSign = true\n")]
    [InlineData("\n[core]\n\tbare = false\\\n\tpager = evil\n")]
    public async Task A_config_with_the_current_stamp_is_not_current_when_it_holds_a_setting_beyond_the_ones_SavePoint_keeps(string extra)
    {
        var repo = await _ws.InitializedRepoAsync();
        Assert.True(RepoConfigFile.IsCurrent(repo.GitDir), "a config SavePoint just wrote must be current, or every save would rewrite it");
        await File.AppendAllTextAsync(Path.Combine(repo.GitDir, "config"), extra, Ct);

        Assert.False(RepoConfigFile.IsCurrent(repo.GitDir));
    }

    [Fact]
    public async Task A_setting_slipped_inside_the_managed_block_makes_the_config_not_current()
    {
        var repo = await _ws.InitializedRepoAsync();
        var path = Path.Combine(repo.GitDir, "config");
        var text = await File.ReadAllTextAsync(path, Ct);
        await File.WriteAllTextAsync(path, text.Replace(RepoConfigFile.BlockEnd, "[commit]\n\tgpgSign = true\n" + RepoConfigFile.BlockEnd, StringComparison.Ordinal), Ct);

        Assert.False(RepoConfigFile.IsCurrent(repo.GitDir));
    }

    [Fact]
    public async Task A_config_that_was_rewritten_from_a_hostile_one_is_current()
    {
        var repo = await _ws.InitializedRepoAsync();
        var path = Path.Combine(repo.GitDir, "config");
        var hostile = (await File.ReadAllTextAsync(path, Ct)) + "[core]\n\tfsmonitor = evil\n[alias]\n\tst = !evil\n";
        await File.WriteAllTextAsync(path, hostile, Ct);

        RepoConfigFile.Apply(repo.GitDir, OperatingSystem.IsWindows());

        Assert.True(RepoConfigFile.IsCurrent(repo.GitDir));
        Assert.DoesNotContain("evil", await File.ReadAllTextAsync(path, Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_save_rewrites_a_config_that_has_the_current_stamp_and_names_a_program_to_sign_with()
    {
        var repo = await _ws.InitializedRepoAsync();
        var marker = Path.Combine(_ws.Root, "signer-ran.txt");
        var signer = Path.Combine(_ws.Root, "signer");
        await File.WriteAllTextAsync(signer, $"#!/bin/sh\necho ran >> '{marker.Replace('\\', '/')}'\nexit 1\n", Ct);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(signer, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var config = Path.Combine(repo.GitDir, "config");
        await File.AppendAllTextAsync(config, $"[commit]\n\tgpgSign = true\n[gpg]\n\tprogram = {signer.Replace('\\', '/')}\n", Ct);
        _ws.Write(repo, "a.txt", "a");

        var error = await Record.ExceptionAsync(() => _ws.CaptureAsync(repo, "profile-1"));

        Assert.Null(error);
        Assert.DoesNotContain("gpg", await File.ReadAllTextAsync(config, Ct), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(marker), "the program the config named ran");
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
        Assert.Equal(await _ws.PlainInitConfigAsync("core.ignorecase"), await _ws.ConfigAsync(repo, "core.ignorecase"));
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

    // What git init decided about the repository format and the file system is not SavePoint's to change, so it survives every rewrite.
    [Theory]
    [InlineData("core", "precomposeunicode", "true")]
    [InlineData("extensions", "objectformat", "sha256")]
    [InlineData("extensions", "refstorage", "reftable")]
    [InlineData("core", "repositoryformatversion", "1")]
    public void A_rewrite_keeps_what_git_init_decided_about_the_format_and_the_file_system(string section, string key, string value)
    {
        var text = $"[core]\n\trepositoryformatversion = 1\n\tbare = false\n\tprecomposeunicode = true\n[extensions]\n\tobjectformat = sha256\n\trefstorage = reftable\n";

        var rewritten = Savepoint.Core.Git.RepoConfigFile.Rewrite(text, isWindows: false);

        Assert.Contains($"[{section}]", rewritten, StringComparison.Ordinal);
        Assert.Contains($"\t{key} = {value}\n", rewritten, StringComparison.Ordinal);
        Assert.Equal(rewritten, Savepoint.Core.Git.RepoConfigFile.Rewrite(rewritten, isWindows: false));
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
        Assert.Equal("7", await _ws.ConfigAsync(repo, "savepoint.repoConfigVersion"));
    }
}
